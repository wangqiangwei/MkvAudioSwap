namespace MkvAudioSwap.Tests;

/// <summary>
/// 环境自检：确认外部依赖真的找得到。
///
/// 为什么需要这个：大部分端到端测试在"找不到 ffmpeg"时会静默 return（跳过），
/// 这样在没装 ffmpeg 的机器上不会红。但副作用是 ——
/// <b>它们可能一直没在跑，而测试报告仍然全绿</b>。
/// 这个类把依赖状态显式断言出来，避免"以为测过了"。
/// </summary>
public class EnvironmentTests
{
    [Fact]
    public void ffmpeg与ffprobe都能找到()
    {
        var ffmpeg = TestTools.FfmpegOrNull();
        var ffprobe = TestTools.FfprobeOrNull();

        Assert.True(ffmpeg is not null,
            "找不到 ffmpeg.exe。端到端测试会静默跳过 —— 先跑一次 build.bat，" +
            "或把 ffmpeg 放进 tools\\，或装到 PATH。");

        Assert.True(ffprobe is not null,
            "找不到 ffprobe.exe。同上。");
    }

    [Fact]
    public void 它们真的能执行()
    {
        var ffmpeg = TestTools.FfmpegOrNull();
        if (ffmpeg is null) return;   // 上一条测试已经会报出来

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-version");

        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.Equal(0, p.ExitCode);
        Assert.Contains("ffmpeg version", output);
    }

    /// <summary>
    /// 素材在不在，也显式报出来 —— 不要靠"测试全绿"去猜。
    /// 素材缺失不算失败（它本来就不是仓库的一部分），但要能看到状态。
    /// </summary>
    [Fact]
    public void 测试素材状态可见()
    {
        var dir = Environment.GetEnvironmentVariable("MKVAUDIOSWAP_SAMPLES");

        // 这里刻意不断言"必须存在"：素材是生成物，不是仓库的一部分。
        // 但如果它在，主素材必须是可读的。
        if (string.IsNullOrWhiteSpace(dir)) return;

        var main = Path.Combine(dir, "01_正常视频_30秒.mp4");
        Assert.True(File.Exists(main),
            $"MKVAUDIOSWAP_SAMPLES 指向 {dir}，但里面没有 01_正常视频_30秒.mp4。");
    }

    /// <summary>
    /// 把"从测试目录向上找素材"的过程摊开，便于排查为什么没找到。
    ///
    /// 背景：SampleFilesTests 靠这个机制自动定位素材。一旦它失效，
    /// 那些测试会静默返回，而报告仍然全绿 —— 看起来"测过了"，实际什么都没测。
    /// 所以这里把搜索过程断言出来（找不到就直接失败并列出所有尝试过的路径）。
    /// </summary>
    [Fact]
    public void 自动查找素材的路径可诊断()
    {
        // 允许用环境变量显式跳过（例如 CI 上没生成素材）
        if (Environment.GetEnvironmentVariable("SKIP_SAMPLE_TESTS") == "1") return;

        const string marker = "01_正常视频_30秒.mp4";

        var tried = new List<string>();
        var dir = AppContext.BaseDirectory;

        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "测试素材");
            tried.Add(candidate);
            if (File.Exists(Path.Combine(candidate, marker))) return;   // 找到了

            var parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (parent == dir) break;
            dir = parent;
        }

        // 没找到：列出所有尝试过的路径，并给出明确指引
        Assert.Fail(
            "自动查找没找到测试素材。已尝试以下路径：" + Environment.NewLine +
            string.Join(Environment.NewLine, tried.Select(p => "  " + p)) + Environment.NewLine +
            Environment.NewLine +
            "素材不是仓库的一部分，需要先生成：" + Environment.NewLine +
            "  powershell -ExecutionPolicy Bypass -File docs\\生成测试素材.ps1" + Environment.NewLine +
            "或者用环境变量显式指定目录：MKVAUDIOSWAP_SAMPLES" + Environment.NewLine +
            "或者在 CI 上设 SKIP_SAMPLE_TESTS=1 跳过。");
    }
}
