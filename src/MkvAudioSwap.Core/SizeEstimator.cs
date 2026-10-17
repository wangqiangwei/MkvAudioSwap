namespace MkvAudioSwap.Core;

/// <summary>输出体积预估。PCM 比 AAC 大 10~20 倍，这是用户最容易踩到的"意外"。</summary>
public static class SizeEstimator
{
    /// <summary>
    /// 估算输出体积（字节）。取源视频的视频码率 + 新音频的码率。
    /// 拿不到源视频码率时返回 null（不猜）。
    /// </summary>
    public static long? EstimateOutputBytes(MediaFileInfo video, MediaStreamInfo audio)
    {
        if (video.FormatDurationSeconds is not > 0) return null;

        var duration = video.FormatDurationSeconds.Value;

        // 源视频总码率 - 源音轨码率 ≈ 视频流码率
        var totalBitrateValue = video.FormatBitRate;
        if (totalBitrateValue is not > 0) return null;

        double totalBitrate = totalBitrateValue.Value;
        var sourceAudioBitrate = video.FirstAudio?.BitRate ?? 0;
        var videoBitrate = Math.Max(totalBitrate - sourceAudioBitrate, totalBitrate * 0.2);

        var bytes = (videoBitrate + EstimateAudioBitrate(audio)) / 8.0 * duration;

        // 容器开销留 2% 余量
        return (long)(bytes * 1.02);
    }

    /// <summary>
    /// 新音轨的码率（bits/s）。
    ///
    /// 分两种情况，混用会算错得很离谱：
    ///   · 未压缩 PCM：码率由 采样率 × 声道 × 位深 决定 —— 文件里通常没有 bit_rate 字段，
    ///     必须自己算（48kHz/24bit/立体声 = 2304 kbps）。
    ///   · 压缩格式（FLAC/ALAC/MP3/AAC…）：直接用文件里记录的码率。
    ///     如果对这种格式套 PCM 公式，会高估十几倍（比如 128kbps 的 MP3 被算成 1.5Mbps）。
    /// </summary>
    private static double EstimateAudioBitrate(MediaStreamInfo audio)
    {
        if (!AudioCodecs.IsLinearPcm(audio.CodecName) && audio.BitRate is > 0)
            return audio.BitRate.Value;

        if (audio.SampleRate > 0 && audio.Channels > 0)
            return (double)audio.SampleRate * audio.Channels * Math.Max(audio.EffectiveBits, 16);

        // 压缩格式且没有码率信息时，按 256 kbps 粗估（宁可高估也不要低估到误导用户）
        return 256_000;
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
