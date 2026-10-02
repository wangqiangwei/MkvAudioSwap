namespace MkvAudioSwap.Core;

/// <summary>输出体积预估。PCM 比 AAC 大 10~20 倍，这是用户最容易踩到的"意外"。</summary>
public static class SizeEstimator
{
    /// <summary>
    /// 估算输出体积（字节）。取源视频的视频码率 + 新音频的原始 PCM 码率。
    /// 拿不到源视频码率时返回 null（不猜）。
    /// </summary>
    public static long? EstimateOutputBytes(MediaFileInfo video, MediaStreamInfo pcmAudio)
    {
        if (video.FormatDurationSeconds is not > 0) return null;

        var duration = video.FormatDurationSeconds.Value;

        // 源视频总码率 - 源音轨码率 ≈ 视频流码率
        var totalBitrateValue = video.FormatBitRate;
        if (totalBitrateValue is not > 0) return null;

        double totalBitrate = totalBitrateValue.Value;
        var sourceAudioBitrate = video.FirstAudio?.BitRate ?? 0;
        var videoBitrate = Math.Max(totalBitrate - sourceAudioBitrate, totalBitrate * 0.2);

        var pcmBitrate = (double)pcmAudio.SampleRate * pcmAudio.Channels * Math.Max(pcmAudio.EffectiveBits, 16);

        var bytes = (videoBitrate + pcmBitrate) / 8.0 * duration;

        // 容器开销留 2% 余量
        return (long)(bytes * 1.02);
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }

    /// <summary>返回目标目录所在磁盘的可用空间（字节）。失败时返回 null。</summary>
    public static long? GetAvailableFreeSpace(string directory)
    {
        try
        {
            var full = Path.GetFullPath(directory);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return null;

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }
}
