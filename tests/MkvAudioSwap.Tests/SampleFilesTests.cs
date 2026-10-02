using MkvAudioSwap.Core;

namespace MkvAudioSwap.Tests;

/// <summary>
/// 对 `测试素材\` 里那批"故意带问题"的素材做断言。
///
/// 目的：交接前先确认"每个文件应该触发什么"这句话本身是对的。
///
/// 路径不写死：依次从这几个地方找，全都找不到就整组跳过（不失败）。
///   1. 环境变量 MKVAUDIOSWAP_SAMPLES
///   2. 从测试程序集所在目录向上最多 6 层，找 `测试素材\01_正常视频_30秒.mp4`
///
/// 素材不是代码仓库的一部分，由 docs\生成测试素材.ps1 生成。
/// </summary>
public class SampleFilesTests
{
    private static string? _resolved;

    private static string? SampleDir
    {
        get
        {
            if (_resolved is not null) return _resolved.Length == 0 ? null : _resolved;

            _resolved = "";

            var fromEnv = Environment.GetEnvironmentVariable("MKVAUDIOSWAP_SAMPLES");
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                // 显式指定就显式报错，不静默回退到自动查找 ——
                // 否则用户以为环境变量生效了，实际测的是别处的素材（或者根本没测）。
                if (!HasSamples(fromEnv))
                {
                    throw new InvalidOperationException(
                        $"MKVAUDIOSWAP_SAMPLES 指向的目录里没有素材：{fromEnv}\n" +
                        "该目录下应能找到 01_正常视频_30秒.mp4。");
                }

                _resolved = fromEnv;
                return _resolved;
            }

            // 从测试程序集所在目录逐级向上找：
            // bin\Debug\net9.0 → bin\Debug → bin → <仓库根>，所以 6 层足够
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir, "测试素材");
                if (HasSamples(candidate))
                {
                    _resolved = candidate;
                    return _resolved;
                }

                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }

            return null;
        }
    }

    private static bool HasSamples(string dir) =>
        !string.IsNullOrWhiteSpace(dir) &&
        File.Exists(Path.Combine(dir, "01_正常视频_30秒.mp4"));

    private static bool Available =>
        Environment.GetEnvironmentVariable("SKIP_SAMPLE_TESTS") != "1"
        && SampleDir is not null;

    private static string Ffprobe() => TestTools.FfprobeOrNull()
        ?? throw new InvalidOperationException("找不到 ffprobe，无法运行素材测试");

    private MediaFileInfo Probe(string name)
    {
        var path = Path.Combine(SampleDir!, name);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Ffprobe(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in new[] { "-v", "quiet", "-print_format", "json", "-show_streams", "-show_format", path })
            psi.ArgumentList.Add(a);

        using var p = System.Diagnostics.Process.Start(psi)!;
        var json = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();

        return MediaProbe.ParseJson(json, path);
    }

    // ────────────────────────── 正常基线 ──────────────────────────

    [Fact]
    public void 正常视频和音频都通过()
    {
        if (!Available) return;

        var video = Probe("01_正常视频_30秒.mp4");
        var audio = Probe("02_正常音频_30秒.caf");

        Assert.Equal(SlotStatus.Ready, SlotEvaluator.EvaluateVideo(video).Status);
        Assert.Equal(SlotStatus.Ready, SlotEvaluator.EvaluateAudio(audio).Status);

        // 素材规格：pcm_s24le / 48000 / 24bit / 立体声
        var pcm = audio.FirstAudio!;
        Assert.Equal("pcm_s24le", pcm.CodecName);
        Assert.Equal(48000, pcm.SampleRate);
        Assert.Equal(24, pcm.EffectiveBits);
        Assert.Equal(2, pcm.Channels);

        Assert.Equal(DurationMatch.Match, SlotEvaluator.CompareDuration(video, audio).Kind);
    }

    // ────────────────────────── 时长不一致 ──────────────────────────

    [Fact]
    public void 音频短22秒_只提醒不拦()
    {
        if (!Available) return;

        var video = Probe("03_视频30秒.mp4");
        var audio = Probe("04_音频只有8秒.caf");

        // 关键：格式上完全合格，所以按钮不能变灰
        Assert.Equal(SlotStatus.Ready, SlotEvaluator.EvaluateVideo(video).Status);
        Assert.Equal(SlotStatus.Ready, SlotEvaluator.EvaluateAudio(audio).Status);

        var cmp = SlotEvaluator.CompareDuration(video, audio);
        Assert.Equal(DurationMatch.Mismatch, cmp.Kind);
        Assert.True(cmp.DeltaSeconds < -20, $"实际差值 {cmp.DeltaSeconds} 秒，应约 -22 秒");

        // 提醒里要说清后果，而不是只重复差值
        var notes = SlotEvaluator.CollectCrossChecks(video, audio, SampleDir!);
        Assert.Contains(notes, n => n.Text.Contains("静音"));
    }

    [Fact]
    public void 音频长15秒_只提醒不拦()
    {
        if (!Available) return;

        var cmp = SlotEvaluator.CompareDuration(
            Probe("03_视频30秒.mp4"), Probe("05_音频45秒比视频长.caf"));

        Assert.Equal(DurationMatch.Mismatch, cmp.Kind);
        Assert.True(cmp.DeltaSeconds > 14, $"实际差值 {cmp.DeltaSeconds} 秒，应约 +15 秒");
    }

    // ────────────────────────── 非 PCM 应被拦 ──────────────────────────

    [Theory]
    [InlineData("06_音频是MP3_会被拦.mp3", "mp3")]
    [InlineData("07_音频是AAC_会被拦.m4a", "aac")]
    [InlineData("08_音频是FLAC_会被拦.flac", "flac")]
    public void 非线性PCM一律拦下并说明编码(string file, string expectedCodec)
    {
        if (!Available) return;

        var info = Probe(file);
        var result = SlotEvaluator.EvaluateAudio(info);

        Assert.Equal(SlotStatus.Blocked, result.Status);
        Assert.Contains(expectedCodec, result.BlockReason);
        Assert.Contains("DAW", result.BlockReason);
    }

    /// <summary>
    /// FLAC 单独再强调一次：它是无损压缩，最容易被误以为"无损就能直接拷"。
    /// 但它是压缩过的，不能 -c copy，必须拦住。
    /// </summary>
    [Fact]
    public void FLAC虽无损但不是线性PCM_必须拦()
    {
        if (!Available) return;

        var flac = Probe("08_音频是FLAC_会被拦.flac").FirstAudio!;
        Assert.Equal("flac", flac.CodecName);
        Assert.False(flac.IsLinearPcm);
    }

    // ────────────────────────── 规格异常只提醒 ──────────────────────────

    [Fact]
    public void 低采样率单声道_只提醒不拦()
    {
        if (!Available) return;

        var audio = Probe("09_音频22050Hz单声道_低采样率.wav");
        Assert.Equal(SlotStatus.Ready, SlotEvaluator.EvaluateAudio(audio).Status);

        var note = SlotEvaluator.CompareSampleRate(Probe("03_视频30秒.mp4"), audio);
        Assert.NotNull(note);
        Assert.Contains("22050", note!.Text);
    }

    [Fact]
    public void 八位音频_提醒但可继续()
    {
        if (!Available) return;

        var info = Probe("10_音频只有8位_音质低.wav");
        var result = SlotEvaluator.EvaluateAudio(info);

        Assert.Equal(SlotStatus.Ready, result.Status);
        Assert.Equal(8, info.FirstAudio!.EffectiveBits);
        Assert.Contains(result.Notes, n => n.Level == NoteLevel.Caution && n.Text.Contains("8 位"));
    }

    [Fact]
    public void 浮点PCM_提醒但可继续()
    {
        if (!Available) return;

        var info = Probe("11_浮点PCM_建议改定点.wav");
        var result = SlotEvaluator.EvaluateAudio(info);

        Assert.Equal(SlotStatus.Ready, result.Status);
        Assert.Contains(result.Notes, n => n.Level == NoteLevel.Caution && n.Text.Contains("浮点"));
    }

    // ────────────────────────── 结构特殊 ──────────────────────────

    [Fact]
    public void 两条音轨的视频_仍可用且提醒丢弃()
    {
        if (!Available) return;

        var info = Probe("12_视频有两条音轨.mkv");
        var result = SlotEvaluator.EvaluateVideo(info);

        Assert.Equal(SlotStatus.Ready, result.Status);
        Assert.Equal(2, info.AudioStreamCount);
        Assert.Contains(result.Notes, n => n.Level == NoteLevel.Caution && n.Text.Contains("2 条音轨"));
    }

    [Fact]
    public void 没有音轨的视频_可用且说明没有音轨()
    {
        if (!Available) return;

        var info = Probe("15_视频没有音轨.mp4");
        var result = SlotEvaluator.EvaluateVideo(info);

        Assert.Equal(SlotStatus.Ready, result.Status);
        Assert.Equal(0, info.AudioStreamCount);
        Assert.Contains(result.Notes, n => n.Text.Contains("没有音轨"));
    }

    [Fact]
    public void 纯音频拖进视频槽会被拦()
    {
        if (!Available) return;

        var result = SlotEvaluator.EvaluateVideo(Probe("14_纯音频拖进视频槽会被拦.mp3"));

        Assert.Equal(SlotStatus.Blocked, result.Status);
        Assert.Contains("没有找到视频画面", result.BlockReason);
    }

    // ────────────────────────── 时长一致但内容错位 ──────────────────────────

    /// <summary>
    /// 这条测试的用意是【记录工具的局限】，不是验证它能查出问题。
    /// 素材 13 的前 3 秒是静音，时长和视频完全一致 ——
    /// 工具只会说"时长一致"，它看不见内容。这是刻意的边界。
    /// </summary>
    [Fact]
    public void 前3秒静音的文件_工具判定为时长一致()
    {
        if (!Available) return;

        var video = Probe("03_视频30秒.mp4");
        var audio = Probe("13_音频前3秒是静音_工具查不出.wav");

        var cmp = SlotEvaluator.CompareDuration(video, audio);

        // 时长确实一致 —— 工具查不出错位，所以要靠用户听或靠偏移校正
        Assert.Equal(DurationMatch.Match, cmp.Kind);

        // 这条断言是在提醒：Match 不等于"内容对得上"
        Assert.True(Math.Abs(cmp.DeltaSeconds ?? 1) < 0.5);
    }

    // ────────────────────────── 输出命名 ──────────────────────────

    [Fact]
    public void 源名已含后缀时输出加_2而不是叠加()
    {
        if (!Available) return;

        var src = Path.Combine(SampleDir!, "18_名字已带后缀_替换音频.mp4");
        var output = OutputNaming.BuildOutputPath(src);

        Assert.Equal("18_名字已带后缀_替换音频_2.mkv", Path.GetFileName(output));
    }

    [Fact]
    public void 已有同名输出时自动加序号_且不覆盖原文件()
    {
        if (!Available) return;

        var existing = Path.Combine(SampleDir!, "19_已有同名输出_替换音频.mkv");
        Assert.True(File.Exists(existing), "素材 19 应该已存在，否则这条测试没有意义");

        var before = File.GetLastWriteTimeUtc(existing);

        var src = Path.Combine(SampleDir!, "03_视频30秒.mp4");
        var output = OutputNaming.BuildOutputPath(src, SampleDir!);   // 会撞上 19 吗？不会，名字不同
        Assert.Contains("03_视频30秒", Path.GetFileName(output));

        // 直接验证唯一化逻辑本身
        var unique = OutputNaming.MakeUnique(existing);
        Assert.Equal("19_已有同名输出_替换音频(1).mkv", Path.GetFileName(unique));
        Assert.Equal(before, File.GetLastWriteTimeUtc(existing));
    }
}
