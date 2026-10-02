using MkvAudioSwap.Core;

namespace MkvAudioSwap.App.Services;

/// <summary>
/// 把 Core 的探测/转封装包装成界面能用的形式，并集中处理"工具找不到"这类环境问题。
/// </summary>
public sealed class MediaInspector
{
    private string? _ffmpegPath;
    private string? _ffprobePath;

    public string? FfmpegPath => _ffmpegPath;
    public string? FfprobePath => _ffprobePath;

    /// <summary>每次调用都重新定位，这样用户补上 bin 目录后不用重启程序。</summary>
    public void Refresh()
    {
        _ffmpegPath = ToolLocator.FindFfmpeg();
        _ffprobePath = ToolLocator.FindFfprobe();
    }

    public bool IsReady => _ffmpegPath is not null && _ffprobePath is not null;

    public async Task<MediaFileInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        Refresh();
        if (_ffprobePath is null)
            throw new MediaProbeException("找不到 ffprobe.exe。请确认程序目录下的 bin 文件夹还在。", "");

        return await new MediaProbe(_ffprobePath).ProbeAsync(path, ct).ConfigureAwait(false);
    }

    public RemuxRunner CreateRunner()
    {
        Refresh();
        if (_ffmpegPath is null)
            throw new InvalidOperationException("找不到 ffmpeg.exe。请确认程序目录下的 bin 文件夹还在。");

        return new RemuxRunner(_ffmpegPath);
    }

    /// <summary>环境检查：只验证两个 exe 存在且能跑（按约定不做封装能力测试）。</summary>
    public async Task<List<(string Label, bool Ok, string Detail)>> SelfCheckAsync(CancellationToken ct = default)
    {
        Refresh();
        var results = new List<(string, bool, string)>();

        results.Add(await CheckOneAsync("ffmpeg", _ffmpegPath, ct).ConfigureAwait(false));
        results.Add(await CheckOneAsync("ffprobe", _ffprobePath, ct).ConfigureAwait(false));

        return results;
    }

    private static async Task<(string Label, bool Ok, string Detail)> CheckOneAsync(
        string label, string? path, CancellationToken ct)
    {
        if (path is null)
            return (label, false, "未找到。请重新解压，确保 bin 文件夹和 exe 在同一层。");

        var result = await ProcessRunner
            .RunAsync(path, ["-version"], TimeSpan.FromSeconds(20), cancellationToken: ct)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            var why = result.TimedOut ? "执行超时" : $"退出码 {result.ExitCode}";
            return (label, false, $"找到了文件但无法运行（{why}）。可能被杀毒软件拦截，或这个文件已损坏。");
        }

        var firstLine = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "";

        if (firstLine.Length > 90) firstLine = firstLine[..90] + "…";

        return (label, true, $"{ToolLocator.DescribeForUser(path)} — {firstLine}");
    }
}
