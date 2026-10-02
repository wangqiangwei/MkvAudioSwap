using System.Globalization;

namespace MkvAudioSwap.Core;

/// <summary>
/// 构造 ffmpeg 命令行。这里是整个程序最需要小心的部分，
/// 下面每一行都对应一个实测出来的坑，改动前请先读注释。
/// </summary>
public static class CommandBuilder
{
    /// <summary>预览的视频长度上限（秒）。实际长度取 min(这个值, 视频时长)。</summary>
    public const double PreviewMaxSeconds = 10;

    public static double EffectivePreviewSeconds(double? videoDurationSeconds)
    {
        if (videoDurationSeconds is not > 0) return PreviewMaxSeconds;
        return Math.Min(PreviewMaxSeconds, videoDurationSeconds.Value);
    }

    /// <summary>
    /// 正式输出：完全不重编码，只把第一条视频流和第一条音频流拷进新的 mkv。
    /// 参数语义：输入 0 = 视频，输入 1 = 音频。
    /// </summary>
    /// <param name="outputPath">输出文件。调用方必须保证它还不存在（本程序自己生成唯一文件名）。</param>
    /// <param name="offsetSeconds">音频偏移。正=音频晚出现（往后推），负=音频早出现（往前提）。</param>
    public static List<string> BuildSwap(
        string videoPath,
        string audioPath,
        string outputPath,
        double offsetSeconds = 0)
    {
        var args = BaseArgs();

        // ★ 顺序关键：[视频] → [偏移 音频]。-itsoffset 只作用于【紧跟其后】的那一个输入。
        // 写错位置的后果不是报错，而是偏移静默失效 —— 用户填了 -300，成品却是 0 偏移。
        // 尤其注意：绝不能在视频输入之前也插一个 -itsoffset，那会把画面整体推走。
        args.Add("-i");
        args.Add(videoPath);

        AppendOffset(args, offsetSeconds);
        args.Add("-i");
        args.Add(audioPath);

        args.AddRange(new[]
        {
            // 只保留源视频的第一条视频流 + 新音频的第一条音频流，其余全部丢弃
            "-map", "0:v:0",
            "-map", "1:a:0",

            "-c", "copy",

            // 丢弃全部容器元数据（标题/艺人/编码时间戳）。
            // 理由：这些字段是按旧音轨写的，复制到新文件可能和新音轨实际时长冲突，
            // 某些播放器会据此显示错误的进度条。宁可少带信息，不要带错信息。
            "-map_metadata", "-1",

            // ★★★ 绝对不可以删掉这一行 ★★★
            // 偏移为负时音轨时间戳会变成负数。mkv 容器的默认行为（avoid_negative_ts=auto）
            // 会把负数"修正"掉 —— 但修正方式是把【视频】整体往后推，而不是裁掉音频开头。
            // 实测（-itsoffset -0.3 封进 mkv）：
            //     默认参数   → 视频 start=0.300  音频 start=0.000   ← 静默错误：画面被推迟了
            //     disabled   → 视频 start=0.000  音频 start=-0.300  ← 正确：音频真的提前了
            // 前者不报任何错，用户只会觉得"画面和原来差了一点"，几乎不可能发现。
            "-avoid_negative_ts", "disabled",

            // 输出文件名由程序保证唯一，所以不带 -y。万一撞名，ffmpeg 直接失败退出，
            // 而不是弹一个看不见的交互提问把进程挂死。
            "-n",

            // 临时文件名保留了 .mkv 后缀，但显式指定容器更稳妥
            "-f", "matroska",

            outputPath,
        });

        return args;
    }

    /// <summary>低清快速预览：视频重编码、音频 AAC、音频保留完整时长。</summary>
    /// <remarks>
    /// 音频刻意【不】裁到 10 秒：AAC 编码 5 分钟只要 1 秒左右，
    /// 而"结尾有没有被切断"正是用户要靠预览检查的事情之一，裁掉就没意义了。
    /// </remarks>
    public static List<string> BuildPreview(
        string videoPath,
        string audioPath,
        string previewOutputPath,
        double offsetSeconds = 0)
    {
        var args = BaseArgs();

        args.Add("-i");
        args.Add(videoPath);

        AppendOffset(args, offsetSeconds);
        args.Add("-i");
        args.Add(audioPath);

        var seconds = PreviewMaxSeconds.ToString("0.###", CultureInfo.InvariantCulture);

        args.AddRange(new[]
        {
            "-map", "0:v:0",
            "-map", "1:a:0",

            // 只截前 N 秒【视频】；音频不加 -t，保留完整时长
            "-t", seconds,

            "-vf", "scale=640:-2",
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-crf", "28",
            "-pix_fmt", "yuv420p",

            "-c:a", "aac",
            "-b:a", "128k",

            "-map_metadata", "-1",
            "-avoid_negative_ts", "disabled",
            "-n",
            "-f", "mp4",
            previewOutputPath,
        });

        return args;
    }

    private static List<string> BaseArgs() =>
    [
        "-hide_banner",
        "-nostdin",
        "-loglevel", "error",
        "-progress", "pipe:1",
    ];

    private static void AppendOffset(List<string> args, double offsetSeconds)
    {
        if (Math.Abs(offsetSeconds) < 0.0005) return;

        args.Add("-itsoffset");
        args.Add(offsetSeconds.ToString("0.###", CultureInfo.InvariantCulture));
    }
}
