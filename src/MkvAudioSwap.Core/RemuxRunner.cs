using System.Globalization;

namespace MkvAudioSwap.Core;

/// <summary>处理失败，带按类型分好的信息。</summary>
public sealed class SwapFailedException : Exception
{
    public SwapFailedException(string title, string advice, string commandLine, string rawError)
        : base(title)
    {
        Title = title;
        Advice = advice;
        CommandLine = commandLine;
        RawError = rawError;
    }

    public string Title { get; }
    public string Advice { get; }
    public string CommandLine { get; }
    public string RawError { get; }

    /// <summary>给"复制详情"按钮用的完整文本。</summary>
    public string Details =>
        $"""
         【{Title}】
         {Advice}

         ── 命令行 ──
         {CommandLine}

         ── ffmpeg 输出 ──
         {RawError}
         """;
}

public sealed record SwapProgress(double Fraction, TimeSpan? OutTime, string RawSpeed)
{
    public int Percent => (int)Math.Round(Math.Clamp(Fraction, 0, 1) * 100);
}

/// <summary>执行替换。负责临时文件、进度、超时、取消、错误翻译。</summary>
public sealed class RemuxRunner
{
    /// <summary>
    /// 整段处理的兜底时间上限。正常情况下几秒到几十秒就结束；
    /// 2 小时还没完说明已经不正常了，宁可报错也不要让界面永远转圈。
    /// </summary>
    public static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(2);

    private readonly string _ffmpegPath;

    public RemuxRunner(string ffmpegPath) => _ffmpegPath = ffmpegPath;

    /// <summary>
    /// 正式替换：源视频 + 新音频 → 输出 mkv（全程不重编码）。
    /// </summary>
    /// <param name="onProgress">进度回调，可能在后台线程触发。</param>
    /// <returns>实际生成的输出文件路径。</returns>
    public async Task<string> SwapAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        double offsetSeconds,
        double? expectedDurationSeconds,
        IProgress<SwapProgress>? onProgress,
        CancellationToken ct)
    {
        // 先写 .partial.mkv，成功了再改名。
        // 目的：中途失败/用户取消时，绝不留下一个看起来能用、实际是半截的 mkv。
        var partial = OutputNaming.ToPartialPath(outputPath);
        TryDelete(partial);

        var args = CommandBuilder.BuildSwap(videoPath, audioPath, partial, offsetSeconds);
        var commandLine = BuildCommandLine(_ffmpegPath, args);

        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(
                _ffmpegPath,
                args,
                AbsoluteTimeout,
                onStdoutLine: line => ReportProgress(line, expectedDurationSeconds, onProgress),
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TryDelete(partial);
            throw new SwapFailedException("处理失败", $"启动 ffmpeg 时出错：{ex.Message}", commandLine, ex.ToString());
        }

        if (result.Cancelled)
        {
            TryDelete(partial);
            throw new OperationCanceledException(ct);
        }

        if (result.TimedOut)
        {
            TryDelete(partial);
            throw new SwapFailedException(
                "处理超时",
                "超过 2 小时仍未完成，已中止。这个源文件可能有问题，建议换一个源视频。",
                commandLine, result.ErrorText);
        }

        if (!result.Success)
        {
            TryDelete(partial);
            var (title, advice) = ErrorTranslator.TranslateFfmpegError(result.ErrorText, outputPath);
            throw new SwapFailedException(title, advice, commandLine, result.ErrorText);
        }

        if (!File.Exists(partial))
        {
            throw new SwapFailedException(
                "处理失败",
                "ffmpeg 报告成功，但没有生成输出文件。请把详情反馈给作者。",
                commandLine, result.ErrorText);
        }

        // 原子改名：到这里文件已经完整了
        try
        {
            File.Move(partial, outputPath, overwrite: false);
        }
        catch (IOException)
        {
            // 极小概率：名字被别人抢了。再找一个唯一名字。
            var fallback = OutputNaming.MakeUnique(outputPath);
            File.Move(partial, fallback);
            outputPath = fallback;
        }

        onProgress?.Report(new SwapProgress(1.0, expectedDurationSeconds is > 0 ? TimeSpan.FromSeconds(expectedDurationSeconds.Value) : null, ""));
        return outputPath;
    }

    /// <summary>预览：低清重编码，用来看对齐对不对。返回预览文件路径。</summary>
    public async Task<string> PreviewAsync(
        string videoPath,
        string audioPath,
        string previewOutputPath,
        double offsetSeconds,
        double? expectedDurationSeconds,
        IProgress<SwapProgress>? onProgress,
        CancellationToken ct)
    {
        var partial = OutputNaming.ToPartialPath(previewOutputPath);
        TryDelete(partial);

        var args = CommandBuilder.BuildPreview(videoPath, audioPath, partial, offsetSeconds);
        var commandLine = BuildCommandLine(_ffmpegPath, args);

        var result = await ProcessRunner.RunAsync(
            _ffmpegPath,
            args,
            AbsoluteTimeout,
            onStdoutLine: line => ReportProgress(line, expectedDurationSeconds, onProgress),
            cancellationToken: ct).ConfigureAwait(false);

        if (result.Cancelled)
        {
            TryDelete(partial);
            throw new OperationCanceledException(ct);
        }

        if (!result.Success || !File.Exists(partial))
        {
            TryDelete(partial);
            var (title, advice) = ErrorTranslator.TranslateFfmpegError(result.ErrorText, previewOutputPath);
            throw new SwapFailedException(title, advice, commandLine, result.ErrorText);
        }

        File.Move(partial, previewOutputPath, overwrite: true);
        return previewOutputPath;
    }

    /// <summary>
    /// 解析 ffmpeg 的 -progress 输出。
    /// 用 -progress pipe:1 而不是解析 stderr，是因为 stderr 的进度行格式会随版本变化，
    /// 而 -progress 是给程序读的稳定键值对。
    /// </summary>
    private static void ReportProgress(string line, double? expectedDurationSeconds, IProgress<SwapProgress>? onProgress)
    {
        if (onProgress is null) return;
        if (expectedDurationSeconds is not > 0) return;

        var idx = line.IndexOf('=');
        if (idx <= 0) return;

        var key = line[..idx].Trim();
        var value = line[(idx + 1)..].Trim();

        // out_time_us / out_time_ms 都可能是微秒（ffmpeg 的历史遗留命名），优先用 us
        if (key is not ("out_time_us" or "out_time_ms")) return;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micro)) return;
        if (micro < 0) return;

        var fraction = micro / 1_000_000.0 / expectedDurationSeconds.Value;
        onProgress.Report(new SwapProgress(fraction, TimeSpan.FromSeconds(micro / 1_000_000.0), ""));
    }

    public static string BuildCommandLine(string exe, IEnumerable<string> args)
    {
        static string Quote(string s) => s.Contains(' ') || s.Contains('"') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;
        return Quote(exe) + " " + string.Join(" ", args.Select(Quote));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 尽力而为 */ }
    }
}
