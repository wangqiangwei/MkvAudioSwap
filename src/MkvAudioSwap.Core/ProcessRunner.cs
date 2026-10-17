using System.Diagnostics;

namespace MkvAudioSwap.Core;

/// <summary>
/// ffmpeg / ffprobe 的定位。查找顺序：AppContext.BaseDirectory\bin → BaseDirectory → PATH。
/// 刻意不缓存结果，因为用户可能在程序运行时补上 bin 目录，然后点"环境检查"。
/// </summary>
public static class ToolLocator
{
    public static string? FindFfmpeg() => Find("ffmpeg.exe");
    public static string? FindFfprobe() => Find("ffprobe.exe");

    public static string? Find(string exeName)
    {
        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "bin", exeName),
            Path.Combine(baseDir, exeName),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        // 回退到 PATH（开发者场景：不打包也能跑）
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim().Trim('"'), exeName);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // PATH 里可能有非法条目，忽略
            }
        }

        return null;
    }

    /// <summary>把工具路径显示成对用户友好的形式（相对 BaseDirectory 时只显示 bin\xxx）。</summary>
    public static string DescribeForUser(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return "(未找到)";

        var baseDir = AppContext.BaseDirectory;
        if (exePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return exePath[baseDir.Length..];

        return exePath;
    }
}

/// <summary>子进程执行结果。</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool Cancelled)
{
    public bool Success => ExitCode == 0 && !TimedOut && !Cancelled;

    /// <summary>stderr 的自然语言描述，用于错误翻译。</summary>
    public string ErrorText => StandardError;
}

public static class ProcessRunner
{
    /// <summary>
    /// 安全地跑一个外部程序。
    /// 关键约束（不可省略，全部是实测踩出来的）：
    ///  1. 必须用 ArgumentList 而不是拼接字符串 —— 路径里有中文、空格、引号时拼接必崩。
    ///  2. 必须重定向并指定 UTF-8 —— 否则中文错误信息会变成乱码。
    ///  3. 必须能超时 —— 半个文件/坏头的 mkv 会让 ffprobe 永久卡住，界面就永远转圈。
    ///  4. 必须能取消 —— 用户点取消要能真的杀掉进程，否则会留下半成品文件。
    ///  5. CreateNoWindow —— 不能让黑框闪出来吓到用户。
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string exePath,
        IEnumerable<string> arguments,
        TimeSpan? timeout = null,
        Action<string>? onStdoutLine = null,
        Action<string>? onStderrLine = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        foreach (var a in arguments) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        var stdoutLock = new object();
        var stderrLock = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdoutLock) { AppendCapped(stdout, e.Data); }
            onStdoutLine?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderrLock) { AppendCapped(stderr, e.Data); }
            onStderrLine?.Invoke(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", $"无法启动程序 {exePath}：{ex.Message}", false, false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 关闭 stdin，保证 ffmpeg 不会因为等待输入而挂死
        try { process.StandardInput.Close(); } catch { /* 忽略 */ }

        using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts?.Token ?? CancellationToken.None);

        var timedOut = false;
        var cancelled = false;

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;

            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 进程可能已经退出
            }
        }

        int exitCode;
        try { exitCode = process.HasExited ? process.ExitCode : -1; }
        catch { exitCode = -1; }

        string outText, errText;
        lock (stdoutLock) { outText = stdout.ToString(); }
        lock (stderrLock) { errText = stderr.ToString(); }

        return new ProcessResult(exitCode, outText, errText, timedOut, cancelled);
    }

    /// <summary>只保留末尾若干字符：长转码的 stderr 会非常大，但我们只需要结尾的报错。</summary>
    private static void AppendCapped(System.Text.StringBuilder sb, string line, int cap = 16000)
    {
        if (sb.Length > cap)
        {
            var keep = sb.ToString()[^Math.Min(cap / 2, sb.Length)..];
            sb.Clear();
            sb.Append(keep);
        }

        sb.AppendLine(line);
    }
}
