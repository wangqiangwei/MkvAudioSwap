using System.Globalization;

namespace MkvAudioSwap.Core;

/// <summary>单个媒体流的探测结果（ffprobe stream 对象的子集）。</summary>
public sealed class MediaStreamInfo
{
    public int Index { get; init; }
    public string CodecType { get; init; } = "";
    public string CodecName { get; init; } = "";

    // 视频
    public int Width { get; init; }
    public int Height { get; init; }
    public string PixelFormat { get; init; } = "";
    public string FrameRate { get; init; } = "";
    public string Profile { get; init; } = "";
    public string ColorSpace { get; init; } = "";
    public int BitsPerRawSample { get; init; }

    // 音频
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public string ChannelLayout { get; init; } = "";

    /// <summary>
    /// 位深。重要：不能用 ffprobe 的 sample_fmt 推导。
    /// 实测 pcm_s24le 的 sample_fmt 是 "s32"（24bit 打包在 32bit 容器里），
    /// 用 sample_fmt 会把 24bit 误判成 32bit。
    /// </summary>
    public int BitsPerSample { get; init; }

    public string SampleFormat { get; init; } = "";
    public string CodecTagString { get; init; } = "";

    public double? DurationSeconds { get; init; }

    /// <summary>流码率（bits/s）。可能缺失（mkv 的 PCM 流通常没有）。</summary>
    public long? BitRate { get; init; }

    public bool IsVideo => string.Equals(CodecType, "video", StringComparison.OrdinalIgnoreCase);
    public bool IsAudio => string.Equals(CodecType, "audio", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否线性 PCM（未压缩）。判据与分类逻辑都在 <see cref="AudioCodecs"/>，这里只是转发，
    /// 避免"什么算可用"这件事散落在多个文件里。
    /// </summary>
    public bool IsLinearPcm => IsAudio && AudioCodecs.IsLinearPcm(CodecName);

    /// <summary>无损压缩（FLAC / ALAC / WavPack / TTA / DTS-HD）。搬进 MKV 后仍然无损。</summary>
    public bool IsLosslessCompressed => IsAudio && AudioCodecs.IsLosslessCompressed(CodecName);

    /// <summary>是不是无损：未压缩 PCM 或无损压缩。</summary>
    public bool IsLosslessAudio => IsAudio && AudioCodecs.IsLossless(CodecName);

    /// <summary>有损压缩（MP3 / AAC / Opus / Vorbis / AC3 / 普通 DTS）。</summary>
    public bool IsLossyAudio => IsAudio && !AudioCodecs.IsLossless(CodecName);

    /// <summary>取实际可用的位深：优先 bits_per_sample，回退 bits_per_raw_sample。</summary>
    public int EffectiveBits => BitsPerSample > 0 ? BitsPerSample : BitsPerRawSample;
}

/// <summary>一个媒体文件的探测结果。</summary>
public sealed class MediaFileInfo
{
    public string Path { get; init; } = "";
    public string FormatName { get; init; } = "";
    public string FormatLongName { get; init; } = "";

    /// <summary>容器级时长。优先生效：stream 级 duration 在部分 mkv 里为空。</summary>
    public double? FormatDurationSeconds { get; init; }

    /// <summary>容器级总码率（bits/s）。</summary>
    public long? FormatBitRate { get; init; }

    /// <summary>文件在磁盘上的字节数。ffprobe 的 format.size。</summary>
    public long? FileSizeBytes { get; init; }

    public IReadOnlyList<MediaStreamInfo> Streams { get; init; } = Array.Empty<MediaStreamInfo>();

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>第一条视频流。与 -map 0:v:0 的语义一致。</summary>
    public MediaStreamInfo? FirstVideo =>
        Streams.FirstOrDefault(s => s.IsVideo);

    public MediaStreamInfo? FirstAudio =>
        Streams.FirstOrDefault(s => s.IsAudio);

    public int AudioStreamCount => Streams.Count(s => s.IsAudio);
    public int VideoStreamCount => Streams.Count(s => s.IsVideo);

    /// <summary>
    /// 用于界面显示的时长：优先容器级，回退所有流的最大值。
    /// </summary>
    public double? DurationSeconds
    {
        get
        {
            if (FormatDurationSeconds is > 0) return FormatDurationSeconds;

            var candidates = Streams
                .Where(s => s.DurationSeconds is > 0)
                .Select(s => s.DurationSeconds!.Value)
                .ToList();

            return candidates.Count > 0 ? candidates.Max() : null;
        }
    }

    public static string FormatDuration(double? seconds)
    {
        if (seconds is not > 0) return "—";
        var t = TimeSpan.FromSeconds(seconds.Value);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>格式化秒数为带符号的秒数，用于时长差异提示。</summary>
    public static string FormatDelta(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Abs(seconds));
        var text = t.TotalMinutes >= 1
            ? $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}"
            : $"{t.TotalSeconds:0.0}";

        return (seconds >= 0 ? "+" : "−") + text + " 秒";
    }

    public static int? ParseFrameRate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den != 0)
        {
            return (int)Math.Round(num / den);
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? (int)Math.Round(v)
            : null;
    }
}
