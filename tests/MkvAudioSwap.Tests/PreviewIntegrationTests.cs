using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MkvAudioSwap.Core;

namespace MkvAudioSwap.Tests;

/// <summary>
/// 预览的端到端测试：真的跑 ffmpeg 生成预览，再回读产物量它。
///
/// 锁住的是 ADR-022 —— 预览必须和正式输出表达同一个偏移，而且产物必须是合法 mp4：
/// 不能带负时间戳，音频也不能被截短。
/// 背景：预览曾经用 -itsoffset，而 mp4 存不了负时间戳，mov 复用器会静默把音频
/// 砍成"音频时长 = 10 + 偏移"，用户看到的就是"偏移对预览不生效"。
///
/// 找不到 ffmpeg 时静默跳过（返回而不是失败），和 SwapIntegrationTests 一致。
/// </summary>
public class PreviewIntegrationTests
{
    // ────────────────────────── 素材 ──────────────────────────

    /// <summary>
    /// 合成"前 N 秒静音、之后是 1kHz 正弦"的 PCM。
    /// 用静音开头是为了能直接听/量出偏移有没有真的把开头裁掉。
    /// </summary>
    private static string MakeLeadSilencePcm(Fixture f, string name, double silenceSeconds, double toneSeconds)
    {
        var path = Path.Combine(f.Dir, name);
        var silence = silenceSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var tone = toneSeconds.ToString("0.###", CultureInfo.InvariantCulture);

        var exit = f.Run(
            "-hide_banner", "-nostdin", "-loglevel", "error",
            "-f", "lavfi", "-i", $"anullsrc=r=48000:cl=stereo:d={silence}",
            "-f", "lavfi", "-i", $"sine=frequency=1000:sample_rate=48000:duration={tone}",
            "-filter_complex", "[0:a][1:a]concat=n=2:v=0:a=1[o]",
            "-map", "[o]", "-c:a", "pcm_s16le", "-n", path);

        Assert.Equal(0, exit);
        return path;
    }

    // ────────────────────────── 测量 ──────────────────────────

    private sealed record StreamInfo(double VideoSeconds, double AudioSeconds, double AudioMinPts);

    /// <summary>回读产物：两条流的时长，以及音频包里最早的时间戳。</summary>
    private static StreamInfo Probe(string ffprobe, string file)
    {
        var streams = RunCapture(ffprobe,
            "-v", "error", "-print_format", "json", "-show_streams", file);
        var packets = RunCapture(ffprobe,
            "-v", "error", "-print_format", "json", "-show_packets", "-select_streams", "a", file);

        using var sDoc = JsonDocument.Parse(streams);
        using var pDoc = JsonDocument.Parse(packets);

        double video = 0, audio = 0;
        foreach (var s in sDoc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var type = s.GetProperty("codec_type").GetString();
            var duration = s.TryGetProperty("duration", out var d)
                ? double.Parse(d.GetString()!, CultureInfo.InvariantCulture)
                : 0;
            if (type == "video") video = duration;
            if (type == "audio") audio = duration;
        }

        var minPts = double.MaxValue;
        foreach (var p in pDoc.RootElement.GetProperty("packets").EnumerateArray())
        {
            if (!p.TryGetProperty("pts_time", out var pt)) continue;
            if (!double.TryParse(pt.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
            if (v < minPts) minPts = v;
        }
        if (minPts == double.MaxValue) minPts = double.NaN;

        return new StreamInfo(video, audio, minPts);
    }

    /// <summary>
    /// 某一段窗口的平均音量（dBFS）。~ -91 表示这段是静音。
    /// 用 atrim+volumedetect 而不是 -ss，避开 seek 语义带来的偏差。
    ///
    /// 注意：窗口里一个采样都没有时 ffmpeg 不会打印 mean_volume
    /// （例如旧实现里音频整体被推到 2 秒之后，0~1 秒这一段就没有音频包）。
    /// 那种情况和数字静音对耳朵是一回事，所以按静音处理。
    /// </summary>
    private static double MeanVolumeDb(string ffmpeg, string file, double startSeconds, double seconds)
    {
        var start = startSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var end = (startSeconds + seconds).ToString("0.###", CultureInfo.InvariantCulture);

        var text = RunCapture(ffmpeg,
            "-hide_banner", "-nostdin", "-i", file, "-map", "0:a:0",
            "-af", $"atrim=start={start}:end={end},volumedetect", "-f", "null", "-");

        var m = System.Text.RegularExpressions.Regex.Match(text, @"mean_volume:\s*(-?[\d.]+)");
        if (!m.Success) return -91;     // 没有任何采样 = 没声音

        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>跑一个外部程序并把 stdout+stderr 一起收回来。</summary>
    private static string RunCapture(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return stdout + "\n" + stderr;
    }

    private static async Task<string> RunPreviewAsync(Fixture f, string video, string audio, string name, double offset)
    {
        var outPath = Path.Combine(f.Dir, name);
        var runner = new RemuxRunner(f.Ffmpeg!);
        await runner.PreviewAsync(video, audio, outPath, offset, 20, null, CancellationToken.None);
        return outPath;
    }

    /// <summary>静音判定阈值（dBFS）：低于它算"这一段没声音"。</summary>
    private const double SilenceDb = -60;

    // ────────────────────────── 测试 ──────────────────────────

    /// <summary>
    /// 负偏移曾经会让预览的音频变成"10 + 偏移"秒（-3 秒 → 只有 7 秒），
    /// 也就是开头被处理对了、结尾却整段没声音。
    /// </summary>
    [Fact]
    public async Task 负偏移_预览的音频不再被截短()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        var video = f.MakeVideo("src.mp4", 20);
        var pcm = f.MakePcm("new.wav", "pcm_s16le", 48000, 2, 20, "wav");

        var preview = await RunPreviewAsync(f, video, pcm, "preview.mp4", offset: -3);

        var info = Probe(TestTools.FfprobeOrNull()!, preview);
        Assert.Equal(10.0, info.VideoSeconds, 1);
        Assert.Equal(10.0, info.AudioSeconds, 1);   // 修之前这里是 7.0
    }

    /// <summary>
    /// 核心承诺：负偏移在预览里真的把音频提前了。
    /// 素材前 3 秒是静音，所以 0 偏移时开头没声音，-3 秒时开头就应该是正弦。
    /// 同时检查 9 秒处仍有声音 —— 那正是"结尾被吃掉"的旧 bug 的藏身处。
    /// </summary>
    [Fact]
    public async Task 负偏移_预览里音频真的提前了且结尾没被吃掉()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        var video = f.MakeVideo("src.mp4", 20);
        var pcm = MakeLeadSilencePcm(f, "lead3.wav", silenceSeconds: 3, toneSeconds: 17);

        var ffmpeg = f.Ffmpeg!;
        var baseline = await RunPreviewAsync(f, video, pcm, "zero.mp4", offset: 0);
        var shifted = await RunPreviewAsync(f, video, pcm, "minus3.mp4", offset: -3);

        // 0 偏移：前 0.5 秒是素材自带的静音
        Assert.True(MeanVolumeDb(ffmpeg, baseline, 0, 0.5) < SilenceDb,
            "0 偏移时预览开头应该还是静音");

        // -3 秒偏移：开头那 3 秒静音被裁掉，一上来就该有声音
        Assert.True(MeanVolumeDb(ffmpeg, shifted, 0, 0.5) > SilenceDb,
            "-3 秒偏移时预览的音频开头就该出声（说明音频真的提前了）");

        // 结尾 9~9.5 秒也必须还有声音（旧实现里音频到 7 秒就没了）
        Assert.True(MeanVolumeDb(ffmpeg, shifted, 9.0, 0.5) > SilenceDb,
            "-3 秒偏移时预览的音频不该在 9 秒前就断掉");
    }

    /// <summary>正偏移：音频被推到后面，开头应该是静音。</summary>
    [Fact]
    public async Task 正偏移_预览里音频被推到后面()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        var video = f.MakeVideo("src.mp4", 20);
        var pcm = f.MakePcm("tone.wav", "pcm_s16le", 48000, 2, 20, "wav");

        var ffmpeg = f.Ffmpeg!;
        var shifted = await RunPreviewAsync(f, video, pcm, "plus2.mp4", offset: 2);

        Assert.True(MeanVolumeDb(ffmpeg, shifted, 0, 1.0) < SilenceDb,
            "+2 秒偏移时预览开头应该是静音");
        Assert.True(MeanVolumeDb(ffmpeg, shifted, 2.5, 0.5) > SilenceDb,
            "+2 秒偏移时 2.5 秒处应该有声音");
    }

    /// <summary>
    /// 根因守卫：预览是 mp4，产物里不允许出现负时间戳
    /// （旧实现里音频首个包在 -1.02 秒，mov 复用器据此把音频砍短）。
    /// 允许 AAC 编码器自带的约 21 毫秒 priming。
    /// </summary>
    [Fact]
    public async Task 预览的音频不带负时间戳()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        var video = f.MakeVideo("src.mp4", 20);
        var pcm = f.MakePcm("new.wav", "pcm_s16le", 48000, 2, 20, "wav");

        var preview = await RunPreviewAsync(f, video, pcm, "neg.mp4", offset: -3);

        var info = Probe(TestTools.FfprobeOrNull()!, preview);
        Assert.False(double.IsNaN(info.AudioMinPts), "应该能读到音频包");
        Assert.True(info.AudioMinPts >= -0.05,
            $"预览的音频不该有负时间戳，实测最早 {info.AudioMinPts:0.###} 秒");
    }
}
