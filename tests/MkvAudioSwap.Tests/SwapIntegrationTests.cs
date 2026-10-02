using System.Diagnostics;
using System.Text.Json;
using MkvAudioSwap.Core;

namespace MkvAudioSwap.Tests;

/// <summary>
/// 端到端测试：真的调用 ffmpeg 生成素材、真的跑一遍替换、再用 ffprobe 检查产物。
/// 找不到 ffmpeg 时静默跳过（返回而不是失败），这样纯逻辑测试在任何机器上都能跑。
/// </summary>
public class SwapIntegrationTests
{
    private static string Ffprobe() => TestTools.FfprobeOrNull()
        ?? throw new InvalidOperationException("找不到 ffprobe，无法运行端到端测试");

    private static (double videoStart, double audioStart, string videoCodec, string audioCodec, int bits, int rate, int channels)
        Probe(string file)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Ffprobe()!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in new[] { "-v", "error", "-print_format", "json", "-show_streams", file })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var json = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();

        using var doc = JsonDocument.Parse(json);
        double vs = -999, asr = -999;
        string vc = "", ac = "";
        int bits = 0, rate = 0, ch = 0;

        foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var type = s.GetProperty("codec_type").GetString();
            var start = s.TryGetProperty("start_time", out var st) && double.TryParse(st.GetString(),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv)
                ? sv : 0;

            if (type == "video") { vs = start; vc = s.GetProperty("codec_name").GetString()!; }
            if (type == "audio")
            {
                asr = start;
                ac = s.GetProperty("codec_name").GetString()!;
                if (s.TryGetProperty("bits_per_raw_sample", out var b)) int.TryParse(b.GetString(), out bits);
                if (s.TryGetProperty("sample_rate", out var r)) int.TryParse(r.GetString(), out rate);
                if (s.TryGetProperty("channels", out var c)) ch = c.GetInt32();
            }
        }

        return (vs, asr, vc, ac, bits, rate, ch);
    }

    /// <summary>
    /// 核心承诺：视频流和音频流都是逐字节拷贝过去的，没有任何重编码损失。
    /// 用 CRC 直接证明，而不是"相信 -c copy 会生效"。
    /// </summary>
    [Fact]
    public async Task 两个流都必须逐字节不变地拷进输出()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        var video = f.MakeVideo("src.mp4", 5);
        var pcm = f.MakePcm("new.caf", "pcm_s24le", 48000, 2, 5, "caf");
        var outPath = Path.Combine(f.Dir, "out.mkv");
        var expectedVideoCrc = f.StreamCrc(video, "0:v:0");
        var expectedAudioCrc = f.StreamCrc(pcm, "0:a:0");

        var runner = new RemuxRunner(f.Ffmpeg!);
        await runner.SwapAsync(video, pcm, outPath, 0, 5, null, CancellationToken.None);

        Assert.True(File.Exists(outPath));
        Assert.Equal(expectedVideoCrc, f.StreamCrc(outPath, "0:v:0"));
        Assert.Equal(expectedAudioCrc, f.StreamCrc(outPath, "0:a:0"));

        var probe = Probe(outPath);
        Assert.Equal("h264", probe.videoCodec);
        Assert.Equal("pcm_s24le", probe.audioCodec);
        Assert.Equal(24, probe.bits);
        Assert.Equal(48000, probe.rate);
        Assert.Equal(2, probe.channels);
    }

    /// <summary>
    /// 负偏移的实测断言。这是整个功能里唯一"错了也不报错"的地方：
    /// 如果没有显式写 -avoid_negative_ts disabled，ffmpeg 会把视频推到 0.3 秒。
    /// </summary>
    [Fact]
    public async Task 负偏移_音频提前且视频起点保持不动()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        var video = f.MakeVideo("src.mp4", 5);
        var pcm = f.MakePcm("new.caf", "pcm_s16le", 48000, 2, 5, "caf");
        var outPath = Path.Combine(f.Dir, "neg.mkv");

        var runner = new RemuxRunner(f.Ffmpeg!);
        await runner.SwapAsync(video, pcm, outPath, -0.3, 5, null, CancellationToken.None);

        var probe = Probe(outPath);

        Assert.Equal(0.0, probe.videoStart, 3);          // 视频绝不能被推迟
        Assert.Equal(-0.3, probe.audioStart, 3);         // 音频必须真的提前
    }

    [Fact]
    public async Task 正偏移_音频推到后面且视频不动()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        var video = f.MakeVideo("src.mp4", 5);
        var pcm = f.MakePcm("new.caf", "pcm_s16le", 48000, 2, 5, "caf");
        var outPath = Path.Combine(f.Dir, "pos.mkv");

        var runner = new RemuxRunner(f.Ffmpeg!);
        await runner.SwapAsync(video, pcm, outPath, 0.25, 5, null, CancellationToken.None);

        var probe = Probe(outPath);
        Assert.Equal(0.0, probe.videoStart, 3);
        Assert.Equal(0.25, probe.audioStart, 3);
    }

    /// <summary>输出里不该残留 .partial 临时文件。</summary>
    [Fact]
    public async Task 成功后不留临时文件()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        var video = f.MakeVideo("src.mp4", 2);
        var pcm = f.MakePcm("new.wav", "pcm_s16le", 44100, 1, 2, "wav");
        var outPath = Path.Combine(f.Dir, "out.mkv");

        var runner = new RemuxRunner(f.Ffmpeg!);
        await runner.SwapAsync(video, pcm, outPath, 0, 2, null, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(f.Dir, "*.partial.mkv"));
    }

    /// <summary>只保留第一条视频流和第一条音频流，其余（含源音轨）必须消失。</summary>
    [Fact]
    public async Task 源视频自带音轨会被新音频替换而不是并存()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        // 源视频带一条 AAC 音轨，频率和我们的新音频不同，便于区分
        var video = f.MakeVideo("src.mp4", 3, audio: "sine=frequency=880:sample_rate=44100:duration=3");
        var pcm = f.MakePcm("new.wav", "pcm_s16le", 48000, 2, 3, "wav");
        var outPath = Path.Combine(f.Dir, "out.mkv");

        var runner = new RemuxRunner(f.Ffmpeg!);
        await runner.SwapAsync(video, pcm, outPath, 0, 3, null, CancellationToken.None);

        var psi = new ProcessStartInfo
        {
            FileName = Ffprobe()!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in new[] { "-v", "error", "-print_format", "json", "-show_streams", outPath })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var json = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();

        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();

        Assert.Equal(2, streams.Count);
        Assert.Single(streams, s => s.GetProperty("codec_type").GetString() == "video");
        Assert.Single(streams, s => s.GetProperty("codec_type").GetString() == "audio");

        var probe = Probe(outPath);
        Assert.Equal("pcm_s16le", probe.audioCodec);   // 不是 aac，说明源音轨真的被丢掉了
        Assert.Equal(48000, probe.rate);
    }

    /// <summary>音频比视频短时，输出不该被截断到音频长度。</summary>
    [Fact]
    public async Task 音频较短时输出仍保留完整视频长度()
    {
        using var f = new Fixture();
        if (!f.HasFfmpeg) return;

        var video = f.MakeVideo("src.mp4", 5);
        var pcm = f.MakePcm("short.wav", "pcm_s16le", 48000, 2, 2, "wav");
        var outPath = Path.Combine(f.Dir, "out.mkv");

        var runner = new RemuxRunner(f.Ffmpeg!);
        await runner.SwapAsync(video, pcm, outPath, 0, 5, null, CancellationToken.None);

        var args = new[] { "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", outPath };
        var psi = new ProcessStartInfo
        {
            FileName = Ffprobe()!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var durText = p.StandardOutput.ReadToEnd().Trim();
        p.StandardError.ReadToEnd();
        p.WaitForExit();

        var duration = double.Parse(durText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(duration > 4.5, $"输出时长应保持视频长度，实际 {duration}");
    }
}
