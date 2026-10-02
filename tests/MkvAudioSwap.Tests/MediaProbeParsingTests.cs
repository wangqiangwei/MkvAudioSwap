using MkvAudioSwap.Core;

namespace MkvAudioSwap.Tests;

/// <summary>
/// 下面这段 JSON 是从真实 ffprobe（ffmpeg n8.0）输出里截取并裁剪的，
/// 刻意保留了"字段类型不统一"的特征：duration 是字符串，channels 是数字，
/// 因为 ffprobe 在不同版本/不同字段上确实混用这两种形式。
/// </summary>
public class MediaProbeParsingTests
{
    private const string RealJson = """
    {
        "streams": [
            {
                "index": 0,
                "codec_name": "h264",
                "codec_type": "video",
                "width": 1920,
                "height": 1080,
                "pix_fmt": "yuv420p",
                "r_frame_rate": "30000/1001",
                "avg_frame_rate": "30000/1001",
                "start_time": "0.000000",
                "duration": "252.345000",
                "bits_per_raw_sample": "8",
                "codec_tag_string": "[0][0][0][0]"
            },
            {
                "index": 1,
                "codec_name": "pcm_s24le",
                "codec_type": "audio",
                "sample_rate": "48000",
                "channels": 2,
                "channel_layout": "stereo",
                "bits_per_sample": 24,
                "bits_per_raw_sample": "24",
                "sample_fmt": "s32",
                "duration": "252.345000",
                "codec_tag_string": "[0][0][0][0]"
            }
        ],
        "format": {
            "format_name": "matroska,webm",
            "format_long_name": "Matroska / WebM",
            "duration": "252.345000",
            "size": "1461335",
            "bit_rate": "46328"
        }
    }
    """;

    [Fact]
    public void 解析真实ffprobe输出()
    {
        var info = MediaProbe.ParseJson(RealJson, @"E:\测试 视频.mkv");

        Assert.Equal("matroska,webm", info.FormatName);
        Assert.Equal(252.345, info.FormatDurationSeconds!.Value, 3);
        Assert.Equal(46328, info.FormatBitRate);
        Assert.Equal(2, info.Streams.Count);
        Assert.Equal("测试 视频.mkv", info.FileName);
    }

    [Fact]
    public void 视频流字段解析正确()
    {
        var v = MediaProbe.ParseJson(RealJson, "x").FirstVideo!;

        Assert.Equal(1920, v.Width);
        Assert.Equal(1080, v.Height);
        Assert.Equal("h264", v.CodecName);
        Assert.Equal("yuv420p", v.PixelFormat);
        Assert.Equal(30, MediaFileInfo.ParseFrameRate(v.FrameRate));
    }

    [Fact]
    public void 音频流字段解析正确_位深不受sample_fmt干扰()
    {
        var a = MediaProbe.ParseJson(RealJson, "x").FirstAudio!;

        Assert.True(a.IsLinearPcm);
        Assert.Equal(48000, a.SampleRate);
        Assert.Equal(2, a.Channels);
        Assert.Equal("stereo", a.ChannelLayout);
        Assert.Equal("s32", a.SampleFormat);   // 这是底层容器格式
        Assert.Equal(24, a.EffectiveBits);     // 但用户看到的位深必须是 24
    }

    [Fact]
    public void 数字型duration也能解析()
    {
        // 有些 ffprobe 构建会把 duration 输出成数字而不是字符串
        const string json = """
        {"streams":[{"index":0,"codec_type":"audio","codec_name":"pcm_s16le","sample_rate":44100,
        "channels":2,"bits_per_sample":16,"duration":12.5}],
        "format":{"format_name":"wav","duration":12.5}}
        """;

        var info = MediaProbe.ParseJson(json, "x.wav");
        Assert.Equal(12.5, info.DurationSeconds!.Value, 3);
        Assert.Equal(44100, info.FirstAudio!.SampleRate);
    }

    /// <summary>部分 mkv 的 stream 级 duration 为空，此时必须回退到容器级时长。</summary>
    [Fact]
    public void 流级时长缺失时回退到容器时长()
    {
        const string json = """
        {"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":640,"height":480,
        "avg_frame_rate":"25/1"}],
        "format":{"format_name":"matroska,webm","duration":"100.0"}}
        """;

        var info = MediaProbe.ParseJson(json, "x.mkv");
        Assert.Null(info.FirstVideo!.DurationSeconds);
        Assert.Equal(100.0, info.DurationSeconds!.Value, 3);
    }

    [Fact]
    public void 空流列表不会崩()
    {
        var info = MediaProbe.ParseJson("""{"streams":[],"format":{}}""", "x");

        Assert.Empty(info.Streams);
        Assert.Null(info.FirstVideo);
        Assert.Null(info.FirstAudio);
        Assert.Null(info.DurationSeconds);
    }

    [Fact]
    public void pcm系列都能通过线性PCM判定()
    {
        string[] pcmCodecs = ["pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_f32le", "pcm_u8", "pcm_s24be"];
        foreach (var c in pcmCodecs)
        {
            var s = new MediaStreamInfo { CodecType = "audio", CodecName = c };
            Assert.True(s.IsLinearPcm, $"{c} 应被判定为线性 PCM");
        }

        foreach (var c in new[] { "aac", "mp3", "flac", "alac", "opus", "vorbis" })
        {
            var s = new MediaStreamInfo { CodecType = "audio", CodecName = c };
            Assert.False(s.IsLinearPcm, $"{c} 不应被判定为线性 PCM");
        }
    }
}
