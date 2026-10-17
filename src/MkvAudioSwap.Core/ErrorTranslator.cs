namespace MkvAudioSwap.Core;

/// <summary>
/// 把 ffmpeg / ffprobe 的英文报错翻译成用户看得懂的话。
/// 原则：先给结论（怎么办），必要时再给原因，绝不让用户直接面对英文原文。
/// 无法识别的一律退回原文 + 提供"复制详情"。
/// </summary>
public static class ErrorTranslator
{
    public static string TranslateProbeError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return "无法读取这个文件的信息（ffprobe 没有返回任何内容）。";

        if (Contains(stderr, "Invalid data found when processing input"))
            return "这个文件无法读取，可能没有下载完整，或者文件已损坏。";

        if (Contains(stderr, "No such file or directory"))
            return "文件不存在，可能已被移动或改名。";

        if (Contains(stderr, "Permission denied"))
            return "没有权限读取这个文件。请检查文件是否被其他程序独占，或换个位置再试。";

        if (Contains(stderr, "moov atom not found"))
            return "这是一个没有写完的 MP4 文件（缺少索引），通常意味着下载未完成。请重新获取这个视频。";

        return "无法读取这个文件的信息。";
    }

    /// <summary>
    /// 翻译 ffmpeg 的失败原因。返回 (用户看到的标题, 具体建议)。
    /// </summary>
    public static (string Title, string Advice) TranslateFfmpegError(string stderr, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return ("处理失败", "ffmpeg 没有返回错误信息。请点\"复制详情\"并把内容反馈给作者。");

        // ── 输入侧问题（必须排在输出侧之前）──
        // 注意顺序：map 失败时 ffmpeg 通常【同时】报 "Error opening output file"。
        // 如果先匹配输出侧，用户会看到"没有写入权限"这种完全错误的建议。
        if (Contains(stderr, "matches no streams") || Contains(stderr, "Stream map"))
            return ("音频文件里没有找到音频流",
                    "这个文件可能已损坏，或者不是真正的音频文件。请确认你选的文件能在播放器里正常播放。");

        if (Contains(stderr, "Invalid data found when processing input"))
            return ("文件无法读取", "输入文件没有下载完整或已损坏。请重新获取源视频，或确认音频文件能正常播放。");

        if (Contains(stderr, "Could not find codec parameters"))
            return ("流信息不完整", "源视频的编码信息读取不到，建议换一个源文件。");

        // ── 输出侧问题 ──
        if (Contains(stderr, "Permission denied") || Contains(stderr, "Error opening output"))
            return ("无法写入输出文件",
                    $"输出位置可能没有写入权限，或文件正被其他程序占用。\n请点下面的\"更改\"换一个保存位置，例如桌面。\n\n目标位置：{SafeDir(outputPath)}");

        if (Contains(stderr, "No space left on device"))
            return ("磁盘空间不足", "目标磁盘已经没有足够空间写入这个文件。请清理空间或换个保存位置。");

        if (Contains(stderr, "File exists"))
            return ("输出文件已存在", "目标文件已经存在。请稍微等一下重试，或换个保存位置。");

        // ── 其余 ──
        if (Contains(stderr, "Error while opening encoder") || Contains(stderr, "Unknown encoder"))
            return ("这个 ffmpeg 版本缺少所需编码器",
                    "预览功能需要 libx264 和 aac 编码器。你使用的 ffmpeg 可能是一个残缺的精简版。\n请重新运行 build.bat，或换成官方完整版 ffmpeg。");

        if (Contains(stderr, "not supported in container") || Contains(stderr, "Could not write header"))
            return ("源视频的编码无法装进 mkv 容器",
                    "这个源视频用了 mkv 不支持的编码格式。请换一个源视频（mp4/mov 常见的 H.264、H.265、AV1 都没问题）。\n\n注意：正式输出全程不重编码，所以这里不会自动帮你转码 —— 那样就违背了这个工具的初衷。");

        if (Contains(stderr, "Too many packets buffered") || Contains(stderr, "Application provided invalid"))
            return ("源文件的时间戳异常",
                    "这个文件的时间戳有问题，导致封装失败。请尝试换一个源文件，或先用其他工具重新封装一次。");

        if (Contains(stderr, "No such file or directory"))
            return ("文件找不到了", "输入或输出路径失效，可能文件在开始处理后被移动。请重新选择文件。");

        if (Contains(stderr, "filename or extension is too long") || Contains(stderr, "path too long"))
            return ("路径太长", "输出路径过长。请把文件移到更浅的目录（例如 E:\\视频\\）再试，或把保存位置改到桌面。");

        // ── 兜底 ──
        var lastLine = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .LastOrDefault(l => l.Length > 0) ?? "未知错误";

        return ("处理失败", $"ffmpeg 返回了一个未预期的错误：\n{lastLine}\n\n请点\"复制详情\"把完整信息反馈给作者。");
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string SafeDir(string path)
    {
        try { return Path.GetDirectoryName(path) ?? path; }
        catch { return path; }
    }
}
