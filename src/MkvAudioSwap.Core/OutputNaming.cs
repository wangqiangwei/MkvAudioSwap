using System.Globalization;

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

    /// <summary>预览文件一律放在这里。刻意不放在用户的输出目录，避免污染他的文件夹。</summary>
    public static string PreviewFolder =>
        PreviewFolderOverride ?? Path.Combine(Path.GetTempPath(), "MkvAudioSwap");

    /// <summary>
    /// 本次预览的文件路径。**每次都用一个新的文件名**，不再固定叫 preview.mp4。
    ///
    /// 固定文件名的两个实测后果（用户反馈"偏移对预览不生效"就是从这里来的）：
    /// 1. 系统播放器已经打开着上一次的 preview.mp4 时，覆盖同名文件后播放器多半
    ///    继续显示它已经读进内存的那一份 —— 用户看到的还是上一次的画面/声音，
    ///    改了偏移也"没反应"；
    /// 2. 遇到占着文件的播放器，`File.Move(overwrite: true)` 会直接抛 IOException，
    ///    预览整个失败。
    /// 文件名变一次，播放器就必须去读新文件，也不存在撞名。
    ///
    /// 为了不堆垃圾，调用方在生成新预览前会先清掉上一次的（<see cref="CleanupPreviewFiles"/>），
    /// 启动和退出时还会各清一次 —— 任何时刻临时目录里最多只有一个预览文件。
    /// </summary>
    public static string BuildPreviewPath(string sourceVideoName = "")
    {
        Directory.CreateDirectory(PreviewFolder);

        // 时间戳给"人看的顺序"，序号保证同一毫秒内多次调用也不会撞名
        // （只靠时间戳时，两次调用落在同一毫秒就会拿到同一个路径）。
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var serial = Interlocked.Increment(ref _previewSerial);

        return MakeUnique(Path.Combine(PreviewFolder, $"preview-{stamp}-{serial}.mp4"));
    }

    private static int _previewSerial;

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
