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

            // 从测试程序集所在目录逐级向上找。
            // 路径形如 tests\MkvAudioSwap.Tests\bin\Debug\net9.0\win-x64，
            // 到仓库根要 6 层，留一点余量用 8 层。
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir, "测试素材");
                TriedPaths.Add(candidate);

                if (HasSamples(candidate))
                {
                    _resolved = candidate;
                    return _resolved;
                }

                // 用 GetFullPath 归一化后再取父目录：
                // 直接对带尾部分隔符的路径调 GetDirectoryName 行为不够稳，
                // 而且分隔符有 '\' 和 '/' 两种，TrimEnd 容易漏掉一种。
                var normalized = Path.GetFullPath(dir);
                var parent = Path.GetDirectoryName(normalized);

                // 到根了就停（父目录等于自己）
                if (string.IsNullOrEmpty(parent) || parent == normalized) break;
                dir = parent;
            }

            return null;
        }
    }

    private static bool HasSamples(string dir) =>
        !string.IsNullOrWhiteSpace(dir) &&
        File.Exists(Path.Combine(dir, "01_正常视频_30秒.mp4"));

    /// <summary>
    /// 自动查找过程中试过的路径。查找失败时会连同这些路径一起报出来 ——
    /// 排查"为什么没找到素材"时，看这个列表比读代码快得多。
    /// </summary>
    private static readonly List<string> TriedPaths = new();

    /// <summary>
    /// 素材是否可用。
    ///
    /// 注意这里<b>静默跳过</b>（return 而不是 Assert.Fail）：素材是生成物，不是仓库的一部分，
    /// 没生成过的机器上跑测试不该红。
    ///
    /// 但代价是：一旦查找逻辑坏掉，测试会全部悄悄跳过，而报告仍然全绿 ——
    /// 看起来"测过了"，其实什么都没测。这个坑真踩过（一条端到端测试 2ms 就"通过"了）。
    /// 所以 <see cref="EnvironmentTests"/> 里有一条专门把查找过程断言出来的测试，
    /// 用来看住这个机制。
    /// </summary>
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

    // ────────────────────────── 无损压缩 / 有损编码 ──────────────────────────

    /// <summary>
    /// 无损压缩编码（FLAC / ALAC / WavPack）必须放行 ——
    /// 旧实现只认 pcm_ 前缀，把它们全拦了；实测才发现它们同样能 copy 进 MKV，而且仍然无损。
    /// </summary>
    [Theory]
    [InlineData("06_音频是FLAC_无损放行.flac", "flac")]
    [InlineData("07_音频是ALAC_无损放行.m4a", "alac")]
    [InlineData("08_音频是WavPack_无损放行.wv", "wavpack")]
    public void 无损压缩编码放行(string file, string expectedCodec)
    {
        if (!Available) return;

        var info = Probe(file);
        var result = SlotEvaluator.EvaluateAudio(info);

        Assert.Equal(expectedCodec, info.FirstAudio!.CodecName);
        Assert.Equal(SlotStatus.Ready, result.Status);
        Assert.True(info.FirstAudio.IsLosslessAudio);

        // 无损的不该出现"已经有损"的警示
        Assert.DoesNotContain(result.Notes, n => n.Text.Contains("已经有损"));
    }

    /// <summary>
    /// 有损编码也放行，但必须有明确警示 ——
    /// 工具不替用户否决，但也不能让他以为成品是无损的。
    /// </summary>
    [Theory]
    [InlineData("06b_音频是MP3_有损但放行.mp3", "mp3")]
    [InlineData("07b_音频是AAC_有损但放行.m4a", "aac")]
    [InlineData("08b_音频是Opus_有损但放行.opus", "opus")]
    public void 有损编码放行但警示(string file, string expectedCodec)
    {
        if (!Available) return;

        var info = Probe(file);
        var result = SlotEvaluator.EvaluateAudio(info);

        Assert.Equal(expectedCodec, info.FirstAudio!.CodecName);
        Assert.Equal(SlotStatus.Ready, result.Status);
        Assert.False(info.FirstAudio.IsLosslessAudio);

        Assert.Contains(result.Notes, n => n.Level == NoteLevel.Caution && n.Text.Contains("已经有损"));
    }

    /// <summary>裸 TrueHD 实测无法封进 MKV，是唯一硬拦的编码。</summary>
    [Fact]
    public void 裸TrueHD会被拦()
    {
        if (!Available) return;

        var info = Probe("08c_音频是TrueHD_会被拦.thd");
        var result = SlotEvaluator.EvaluateAudio(info);

        Assert.Equal("truehd", info.FirstAudio!.CodecName);
        Assert.Equal(SlotStatus.Blocked, result.Status);
        Assert.Contains("truehd", result.BlockReason);
    }

    /// <summary>
    /// 端到端：FLAC 真的能被 copy 进输出的 MKV，而且音频数据一字节没变。
    ///
    /// 这条是本次功能扩张的核心证据 ——
    /// 单元测试只能证明"我放行了"，这条才能证明"放行之后 ffmpeg 真的做得成，且仍然无损"。
    /// </summary>
    [Fact]
    public async Task FLAC能真的封进MKV且音频未变()
    {
        if (!Available) return;

        var ffmpeg = TestTools.FfmpegOrNull();
        if (ffmpeg is null) return;

        var video = Path.Combine(SampleDir!, "03_视频30秒.mp4");
        var flac = Path.Combine(SampleDir!, "06_音频是FLAC_无损放行.flac");
        var outPath = Path.Combine(Path.GetTempPath(), "mkvswap_flac_" + Guid.NewGuid().ToString("N")[..8] + ".mkv");

        try
        {
            var runner = new RemuxRunner(ffmpeg);
            await runner.SwapAsync(video, flac, outPath, 0, 30, null, CancellationToken.None);

            Assert.True(File.Exists(outPath));

            // 输出的音频流应当仍是 flac，而不是被转成了别的东西
            var probed = ProbeAbsolute(outPath);
            Assert.Equal("flac", probed.FirstAudio!.CodecName);

            using var fixture = new Fixture();
            var crcSource = fixture.StreamCrc(flac, "0:a:0");
            var crcOutput = fixture.StreamCrc(outPath, "0:a:0");
            Assert.Equal(crcSource, crcOutput);
        }
        finally
        {
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
        }
    }

    /// <summary>按绝对路径探测媒体信息。</summary>
    private static MediaFileInfo ProbeAbsolute(string path)
    {
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
