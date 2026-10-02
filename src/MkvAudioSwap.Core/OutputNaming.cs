namespace MkvAudioSwap.Core;

/// <summary>输出文件命名：与源视频同目录，`<源名>_替换音频.mkv`，撞名自动加 (1)(2)…</summary>
public static class OutputNaming
{
    public const string Suffix = "_替换音频";

    /// <summary>
    /// 生成不冲突的输出路径。
    /// 规则：
    ///   MV.mp4            → MV_替换音频.mkv
    ///   MV_替换音频.mp4    → MV_替换音频_2.mkv   （避免叠加出 _替换音频_替换音频）
    ///   已存在上面的文件    → MV_替换音频(1).mkv
    /// </summary>
    public static string BuildOutputPath(string sourceVideoPath, string? outputDirectory = null)
    {
        var dir = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(Path.GetFullPath(sourceVideoPath)) ?? "."
            : outputDirectory;

        var baseName = Path.GetFileNameWithoutExtension(sourceVideoPath);

        // 源文件本身已经带后缀时不要再叠一层，改用 _2 区分
        var name = baseName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)
            ? baseName + "_2"
            : baseName + Suffix;

        return MakeUnique(Path.Combine(dir, name + ".mkv"));
    }

    /// <summary>在 path 后面依次尝试 (1) (2) (3)… 直到找到一个不存在的名字。</summary>
    public static string MakeUnique(string desiredPath)
    {
        if (!File.Exists(desiredPath)) return desiredPath;

        var dir = Path.GetDirectoryName(desiredPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);

        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        // 极端情况：同一目录里堆了一万个同名文件
        return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
    }

    /// <summary>把输出路径变成可用于 ffmpeg 的临时文件名（保留扩展名，因为 ffmpeg 会据此推断容器）。</summary>
    public static string ToPartialPath(string finalPath)
    {
        var dir = Path.GetDirectoryName(finalPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(finalPath);
        var ext = Path.GetExtension(finalPath);
        return Path.Combine(dir, $"{name}.partial{ext}");
    }

    /// <summary>预览目录的覆盖点。默认是临时目录下的 MkvAudioSwap；测试会临时改掉它。</summary>
    internal static string? PreviewFolderOverride { get; set; }

    /// <summary>预览文件固定放在这里。刻意不放在用户的输出目录，避免污染他的文件夹。</summary>
    public static string PreviewFolder =>
        PreviewFolderOverride ?? Path.Combine(Path.GetTempPath(), "MkvAudioSwap");

    /// <summary>
    /// 本次预览的文件路径。**刻意使用固定文件名**，不带时间戳 ——
    /// 带时间戳（曾经的做法 preview_名字_HHmmss.mp4）意味着每预览一次就永久多一个文件，
    /// 几分钟就能在临时目录里堆出一堆几十 MB 的垃圾，而且永远不会被覆盖。
    /// 固定文件名 = 下一次预览自动覆盖上一次，临时目录里最多只有一个预览文件。
    /// </summary>
    public static string BuildPreviewPath(string sourceVideoName = "")
    {
        Directory.CreateDirectory(PreviewFolder);
        return Path.Combine(PreviewFolder, "preview.mp4");
    }

    /// <summary>
    /// 清理上一次运行遗留的预览文件。在程序启动时调用：
    /// 此时不可能有预览正在被播放器读取，所以删除是安全的。
    ///
    /// 刻意【不】在关闭偏移弹窗时删：系统播放器可能还开着预览文件，
    /// 而且用户可能想再回看一遍。清理时机放在"启动时"而不是"每次预览后"，
    /// 既避免了误删正在使用的文件，也不会让垃圾无限堆积。
    /// </summary>
    public static int CleanupPreviewFiles()
    {
        var deleted = 0;
        try
        {
            if (!Directory.Exists(PreviewFolder)) return 0;

            foreach (var file in Directory.EnumerateFiles(PreviewFolder, "*.mp4"))
            {
                try { File.Delete(file); deleted++; }
                catch { /* 文件可能被占用，留着下次再删 */ }
            }
        }
        catch
        {
            // 清理失败绝不能影响程序启动
        }

        return deleted;
    }
}
