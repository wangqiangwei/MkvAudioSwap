namespace MkvAudioSwap.Tests;

/// <summary>
/// 测试用的工具查找。刻意不写死任何绝对路径 ——
/// 换成别人的机器、或者仓库搬到别处，测试都要能跑。
///
/// 查找顺序：
///   1. 系统 PATH
///   2. 从测试程序集所在目录逐级向上找 tools\ 和 dist\替音工具\bin\
///      （build.bat 会把 ffmpeg 放进这两个位置）
/// </summary>
public static class TestTools
{
    /// <summary>找 ffmpeg.exe。找不到返回 null（调用方自行决定跳过还是失败）。</summary>
    public static string? Find(string exeName)
    {
        foreach (var dir in PathCandidates())
        {
            try
            {
                var full = Path.Combine(dir, exeName);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // PATH 里可能有非法条目，忽略
            }
        }

        return null;
    }

    public static string? FfmpegOrNull() => Find("ffmpeg.exe");

    public static string? FfprobeOrNull() => Find("ffprobe.exe");

    private static IEnumerable<string> PathCandidates()
    {
        // 1) 系统 PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return dir.Trim().Trim('"');

        // 2) 仓库内：从测试输出目录向上找
        //    bin\Debug\net9.0 → bin\Debug → bin → tests\<项目> → tests → <仓库根>
        var dir2 = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir2); i++)
        {
            yield return Path.Combine(dir2, "tools");
            yield return Path.Combine(dir2, "dist", "替音工具", "bin");
            yield return Path.Combine(dir2, "bin");

            dir2 = Path.GetDirectoryName(dir2.TrimEnd(Path.DirectorySeparatorChar));
        }
    }
}
