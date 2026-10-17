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
    ///
    /// ★ 偏移在预览里【不能】用 -itsoffset，必须用音频滤镜，理由见
    /// <see cref="PreviewAudioFilter"/> 与 ADR-022。
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
        });

        var audioFilter = PreviewAudioFilter(offsetSeconds);
        if (audioFilter is not null)
        {
            args.Add("-af");
            args.Add(audioFilter);
        }

        args.AddRange(new[]
        {
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

    /// <summary>
    /// 预览里的音频偏移，用音频滤镜表达（正偏移插静音、负偏移裁掉开头）。
    /// 返回 null 表示不需要滤镜（偏移为 0）。
    ///
    /// ★★★ 预览绝对不能用 -itsoffset ★★★
    ///
    /// 正式输出是 mkv，`-avoid_negative_ts disabled` 能保住音频的负时间戳（ADR-002）。
    /// 但预览是 **mp4**，而 mp4 根本存不了负时间戳 —— mov 复用器会无视
    /// `avoid_negative_ts disabled`，自己把负的部分"处理"掉。实测（ffmpeg n8.0，
    /// 30 秒素材，`-t 10`）：
    ///
    /// | 偏移 | 预览的音频时长 | 说明 |
    /// |:--|:--|:--|
    /// | 0    | 10.00 秒 | 正常 |
    /// | -0.3 |  9.70 秒 | 结尾少 0.3 秒 |
    /// | -3   |  7.00 秒 | 结尾少 3 秒 |
    /// | -8   |  2.00 秒 | 结尾少 8 秒 |
    /// | ≥ -10 | 没有音频 | 用户看到的就是"偏移对预览不生效" |
    ///
    /// 规律是**音频时长 = 10 + 偏移**，也就是负偏移越大，预览的音频被砍得越多，
    /// 而这一切**不报任何错**。滤镜方案（atrim 裁开头 / adelay 插静音）
    /// 产生的都是非负时间戳，mp4 能原样存下来，实测与正式输出的对齐完全一致。
    /// </summary>
    private static string? PreviewAudioFilter(double offsetSeconds)
    {
        if (Math.Abs(offsetSeconds) < 0.0005) return null;

        if (offsetSeconds < 0)
        {
            // 负偏移 = 音频提前：等价于把音频开头裁掉 |偏移| 秒。
            // asetpts 把时间戳归零（否则裁完还剩原来的起点）；
            // apad 兜住"偏移比音频还长"这种极端情况 —— 否则音频一个采样都不剩，
            // ffmpeg 会警告 "No filtered frames for output stream"，
            // 补成静音才是正确语义（正式输出里这段音频本来就在画面之前，听不到）。
            var start = Math.Abs(offsetSeconds).ToString("0.###", CultureInfo.InvariantCulture);
            return $"atrim=start={start},asetpts=PTS-STARTPTS,apad";
        }

        // 正偏移 = 音频晚出现：等价于在音频前面插 |偏移| 毫秒静音。
        // all=1 让每个声道都延迟，否则多声道要逐个写延迟值。
        var ms = Math.Round(offsetSeconds * 1000).ToString("0", CultureInfo.InvariantCulture);
        return $"adelay={ms}:all=1";
    }

    private static List<string> BaseArgs() =>
    [
        "-hide_banner",
        "-nostdin",
        "-loglevel", "error",
        "-progress", "pipe:1",
    ];

    /// <summary>
    /// 给【正式输出】的音频输入加 -itsoffset。
    /// 只有 mkv 用得起这招（负时间戳合法 + 配合 -avoid_negative_ts disabled）；
    /// 预览是 mp4，必须改用 <see cref="PreviewAudioFilter"/>。
    /// </summary>
    private static void AppendOffset(List<string> args, double offsetSeconds)
    {
        if (Math.Abs(offsetSeconds) < 0.0005) return;

        args.Add("-itsoffset");
        args.Add(offsetSeconds.ToString("0.###", CultureInfo.InvariantCulture));
    }
}
