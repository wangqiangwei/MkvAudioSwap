using System.Globalization;
using System.Text.Json;

namespace MkvAudioSwap.Core;

public sealed class MediaProbeException : Exception
{
    public MediaProbeException(string message, string rawError, bool timedOut = false)
        : base(message)
    {
        RawError = rawError;
        TimedOut = timedOut;
    }

    public string RawError { get; }
    public bool TimedOut { get; }
}

/// <summary>用 ffprobe 读取媒体信息。只读元数据，不解码内容，通常几十毫秒。</summary>
public sealed class MediaProbe
{
    /// <summary>
    /// ffprobe 超时。必须存在：被写坏头的 mkv 或只下载了一半的文件会让 ffprobe 永久阻塞，
    /// 没有超时的话用户点一下"选择文件"，界面就永远转圈 —— 比报错更糟。
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly string _ffprobePath;

    public MediaProbe(string ffprobePath) => _ffprobePath = ffprobePath;

    public async Task<MediaFileInfo> ProbeAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new MediaProbeException("文件不存在或已被移动", "");

        var args = new[]
        {
            "-v", "error",
            "-print_format", "json",
            "-show_streams",
            "-show_format",
            filePath,
        };

        var result = await ProcessRunner
            .RunAsync(_ffprobePath, args, DefaultTimeout, cancellationToken: ct)
            .ConfigureAwait(false);

        if (result.TimedOut)
            throw new MediaProbeException(
                "读取文件信息超时。这个文件可能没有下载完整或已经损坏。", result.ErrorText, timedOut: true);

        if (result.Cancelled) throw new OperationCanceledException(ct);

        if (result.ExitCode != 0)
        {
            var msg = ErrorTranslator.TranslateProbeError(result.ErrorText);
            throw new MediaProbeException(msg, result.ErrorText);
        }

        return ParseJson(result.StandardOutput, filePath);
    }

    /// <summary>解析 ffprobe 的 JSON。字段类型不统一（有的版本给字符串有的给数字），全部容错处理。</summary>
    public static MediaFileInfo ParseJson(string json, string filePath)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var streams = new List<MediaStreamInfo>();
        if (root.TryGetProperty("streams", out var streamsEl) && streamsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in streamsEl.EnumerateArray())
                streams.Add(ParseStream(s));
        }

        string formatName = "", formatLongName = "";
        double? formatDuration = null;
        long? formatBitRate = null;
        long? fileSize = null;

        if (root.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.Object)
        {
            formatName = GetString(fmt, "format_name");
            formatLongName = GetString(fmt, "format_long_name");
            formatDuration = GetDouble(fmt, "duration");
            formatBitRate = (long?)GetDouble(fmt, "bit_rate");
            fileSize = (long?)GetDouble(fmt, "size");
        }

        return new MediaFileInfo
        {
            Path = filePath,
            FormatName = formatName,
            FormatLongName = formatLongName,
            FormatDurationSeconds = formatDuration,
            FormatBitRate = formatBitRate,
            FileSizeBytes = fileSize,
            Streams = streams,
        };
    }

    private static MediaStreamInfo ParseStream(JsonElement s) => new()
    {
        Index = (int)(GetDouble(s, "index") ?? 0),
        CodecType = GetString(s, "codec_type"),
        CodecName = GetString(s, "codec_name"),
        Width = (int)(GetDouble(s, "width") ?? 0),
        Height = (int)(GetDouble(s, "height") ?? 0),
        PixelFormat = GetString(s, "pix_fmt"),
        FrameRate = GetString(s, "avg_frame_rate") is { Length: > 0 } afr && afr != "0/0"
            ? afr
            : GetString(s, "r_frame_rate"),
        Profile = GetString(s, "profile"),
        ColorSpace = GetString(s, "color_space"),
        BitsPerRawSample = (int)(GetDouble(s, "bits_per_raw_sample") ?? 0),
        SampleRate = (int)(GetDouble(s, "sample_rate") ?? 0),
        Channels = (int)(GetDouble(s, "channels") ?? 0),
        ChannelLayout = GetString(s, "channel_layout"),
        BitsPerSample = (int)(GetDouble(s, "bits_per_sample") ?? 0),
        SampleFormat = GetString(s, "sample_fmt"),
        CodecTagString = GetString(s, "codec_tag_string"),
        DurationSeconds = GetDouble(s, "duration"),
        BitRate = (long?)GetDouble(s, "bit_rate"),
    };

    private static string GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return "";

        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.ToString(),
            _ => "",
        };
    }

    private static double? GetDouble(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;

        if (el.ValueKind == JsonValueKind.Number)
            return el.TryGetDouble(out var n) ? n : null;

        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;

        return null;
    }
}
