using MkvAudioSwap.Core;

namespace MkvAudioSwap.Tests;

public class SlotEvaluatorTests
{
    private static MediaFileInfo Info(params MediaStreamInfo[] streams) => new()
    {
        Path = "x.mp4",
        FormatName = "mov,mp4",
        FormatDurationSeconds = streams
            .Where(s => s.DurationSeconds is > 0)
            .Select(s => s.DurationSeconds!.Value)
            .DefaultIfEmpty(5)
            .Max(),
        Streams = streams,
    };

    private static MediaStreamInfo Video(double duration = 5) => new()
    {
        Index = 0, CodecType = "video", CodecName = "h264",
        Width = 1920, Height = 1080, FrameRate = "30000/1001",
        PixelFormat = "yuv420p", DurationSeconds = duration,
    };

    /// <summary>
    /// 造一条音频流。名字刻意不叫 Pcm —— 现在要测的编码不止 PCM 一种，
    /// 之前的命名（Pcm("aac")）读起来自相矛盾。
    /// </summary>
    private static MediaStreamInfo Audio(string codec = "pcm_s24le", int bits = 24, int rate = 48000,
        int channels = 2, double duration = 5, string? sampleFormat = null, long? bitRate = null) => new()
    {
        Index = 1, CodecType = "audio", CodecName = codec,
        SampleRate = rate, Channels = channels, BitsPerSample = bits,
        SampleFormat = sampleFormat ?? "s32",
        DurationSeconds = duration,
        BitRate = bitRate,
    };

    /// <summary>兼容旧调用点的别名。</summary>
    private static MediaStreamInfo Pcm(string codec = "pcm_s24le", int bits = 24, int rate = 48000,
        int channels = 2, double duration = 5, string? sampleFormat = null) =>
        Audio(codec, bits, rate, channels, duration, sampleFormat);

    private static MediaStreamInfo Aac(double duration = 5, int rate = 44100) => new()
    {
        Index = 0, CodecType = "audio", CodecName = "aac",
        SampleRate = rate, Channels = 2, DurationSeconds = duration,
    };

    // ─────────── 硬拦截：只有这两种 ───────────

    [Fact]
    public void 没有视频流的文件会被拦()
    {
        var r = SlotEvaluator.EvaluateVideo(Info(Aac()));
        Assert.Equal(SlotStatus.Blocked, r.Status);
        Assert.Contains("没有找到视频画面", r.BlockReason);
    }

    /// <summary>
    /// 有损编码（MP3/AAC 等）现在<b>放行</b>，但必须明确警示。
    ///
    /// 行为变过：旧实现只认 pcm_ 前缀，把 MP3/AAC/FLAC 一律拦下。实测发现
    /// 它们都能用 -c copy 搬进 MKV，拦住是错的。有损的放行但要说清"成品会有损"。
    /// </summary>
    [Theory]
    [InlineData("aac")]
    [InlineData("mp3")]
    [InlineData("opus")]
    [InlineData("vorbis")]
    [InlineData("ac3")]
    public void 有损音频放行但明确警示(string codec)
    {
        var r = SlotEvaluator.EvaluateAudio(Info(Pcm(codec)));

        Assert.Equal(SlotStatus.Ready, r.Status);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Caution && n.Text.Contains("原样搬进成品"));
    }

    /// <summary>无损压缩（FLAC/ALAC/WavPack）放行，而且不该出现"有损"的警示。</summary>
    [Theory]
    [InlineData("flac")]
    [InlineData("alac")]
    [InlineData("wavpack")]
    [InlineData("flac_pcm")]
    public void 无损压缩放行且不报有损(string codec)
    {
        var r = SlotEvaluator.EvaluateAudio(Info(Pcm(codec)));

        Assert.Equal(SlotStatus.Ready, r.Status);
        Assert.DoesNotContain(r.Notes, n => n.Text.Contains("已经有损"));
    }

    /// <summary>裸 TrueHD 实测无法封进 MKV（"sample rate not set"），是唯一硬拦的编码。</summary>
    [Fact]
    public void TrueHD会被拦下()
    {
        var r = SlotEvaluator.EvaluateAudio(Info(Pcm("truehd")));

        Assert.Equal(SlotStatus.Blocked, r.Status);
        Assert.Contains("truehd", r.BlockReason);
        Assert.Contains("FLAC", r.BlockReason);
    }

    /// <summary>
    /// 白名单之外的编码<b>不硬拦</b> —— 我们没法预先枚举 ffmpeg 将来支持什么。
    /// 真装不进去时由 mkv 封装器给出准确报错。
    /// </summary>
    [Fact]
    public void 白名单外的编码放行并提示来源()
    {
        var r = SlotEvaluator.EvaluateAudio(Info(Pcm("some_future_codec")));

        Assert.Equal(SlotStatus.Ready, r.Status);
        Assert.Contains(r.Notes, n => n.Text.Contains("some_future_codec"));
    }

    [Fact]
    public void 编码分类判据()
    {
        // 无损：未压缩 PCM + 无损压缩
        Assert.True(AudioCodecs.IsLossless("pcm_s24le"));
        Assert.True(AudioCodecs.IsLossless("flac"));
        Assert.True(AudioCodecs.IsLossless("alac"));
        Assert.True(AudioCodecs.IsLossless("wavpack"));

        // 有损
        Assert.False(AudioCodecs.IsLossless("aac"));
        Assert.False(AudioCodecs.IsLossless("mp3"));
        Assert.False(AudioCodecs.IsLossless("opus"));

        // 能否 copy 进 MKV
        Assert.True(AudioCodecs.CanCopyToMkv("flac"));
        Assert.True(AudioCodecs.CanCopyToMkv("pcm_s16le"));
        Assert.True(AudioCodecs.CanCopyToMkv("mp3"));
        Assert.False(AudioCodecs.CanCopyToMkv("truehd"));   // 实测失败
        Assert.Null(AudioCodecs.CanCopyToMkv("未知编码"));    // 不确定 → 放行由封装器裁决

        // DTS 家族：普通 DTS 是压缩有损，DTS-HD 是无损，靠名字区分
        Assert.False(AudioCodecs.IsLossless("dts"));
        Assert.True(AudioCodecs.IsLossless("dts_hd_ma"));
    }

    [Fact]
    public void DTS家族能copy进MKV()
    {
        Assert.True(AudioCodecs.CanCopyToMkv("dts"));
        Assert.True(AudioCodecs.CanCopyToMkv("dts_hd"));
    }

    [Fact]
    public void 没有音频流的文件会被拦()
    {
        var r = SlotEvaluator.EvaluateAudio(Info(Video()));
        Assert.Equal(SlotStatus.Blocked, r.Status);
    }

    // ─────────── 只提醒、不拦截 ───────────

    /// <summary>
    /// 刻意的产品决定：程序不替用户判断质量。时长不一致只是黄字提醒，按钮照常可用。
    /// </summary>
    [Fact]
    public void 时长不一致不拦截_只产生提醒()
    {
        var video = Info(Video(300));
        var audio = Info(Pcm(duration: 200));

        Assert.Equal(SlotStatus.Ready, SlotEvaluator.EvaluateAudio(audio).Status);

        var cmp = SlotEvaluator.CompareDuration(video, audio);
        Assert.Equal(DurationMatch.Mismatch, cmp.Kind);
        Assert.Contains("短", cmp.Text);
    }

    [Fact]
    public void 时长差异在容差内算一致()
    {
        // 视频音频天然存在帧对齐误差，不能因为差几毫秒就报黄
        var cmp = SlotEvaluator.CompareDuration(Info(Video(300)), Info(Pcm(duration: 300.05)));
        Assert.Equal(DurationMatch.Match, cmp.Kind);
    }

    [Fact]
    public void 时长差异在0点1到0点5秒之间只算基本一致()
    {
        var cmp = SlotEvaluator.CompareDuration(Info(Video(300)), Info(Pcm(duration: 300.3)));
        Assert.Equal(DurationMatch.Close, cmp.Kind);
    }

    [Fact]
    public void 音频比视频长时提示超出画面()
    {
        var cmp = SlotEvaluator.CompareDuration(Info(Video(100)), Info(Pcm(duration: 160)));
        Assert.Equal(DurationMatch.Mismatch, cmp.Kind);
        Assert.Contains("长", cmp.Text);
        Assert.Contains("超出画面", cmp.Text);
    }

    [Fact]
    public void 低采样率和单声道不算问题()
    {
        // 44.1kHz 和单声道都是完全正常的导出设置，不该报警
        var r = SlotEvaluator.EvaluateAudio(Info(Pcm(rate: 44100, channels: 1)));
        Assert.Equal(SlotStatus.Ready, r.Status);
        Assert.DoesNotContain(r.Notes, n => n.Level == NoteLevel.Caution);
    }

    [Fact]
    public void 八位位深会提醒()
    {
        var r = SlotEvaluator.EvaluateAudio(Info(Pcm("pcm_u8", bits: 8)));
        Assert.Equal(SlotStatus.Ready, r.Status);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Caution && n.Text.Contains("8 位"));
    }

    [Fact]
    public void 浮点PCM会提醒但仍可继续()
    {
        var r = SlotEvaluator.EvaluateAudio(Info(Pcm("pcm_f32le", bits: 32, sampleFormat: "flt")));
        Assert.Equal(SlotStatus.Ready, r.Status);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Caution && n.Text.Contains("浮点"));
    }

    [Fact]
    public void 采样率不同会陈述但用词不是警告()
    {
        var video = Info(Video(), Aac());          // 源音轨 44100
        var audio = Info(Pcm(rate: 48000));        // 新音频 48000

        var note = SlotEvaluator.CompareSampleRate(video, audio);
        Assert.NotNull(note);
        Assert.Contains("44100", note!.Text);
        Assert.Contains("48000", note.Text);
    }

    [Fact]
    public void 采样率相同时不产生备注()
    {
        var video = Info(Video(), Aac(rate: 48000));
        Assert.Null(SlotEvaluator.CompareSampleRate(video, Info(Pcm(rate: 48000))));
    }

    /// <summary>
    /// 视频槽始终给一条说明，讲清"成品里只有这一条音轨、不会混音"这个最容易被误解的点。
    /// 说明文字刻意不复述卡片上已经写着的"源音轨 xxx 将被替换"，
    /// 避免同一句话在界面不同位置各出现一次。
    /// </summary>
    [Fact]
    public void 视频槽给出不会混音的说明()
    {
        var r = SlotEvaluator.EvaluateVideo(Info(Video(), Aac()));

        Assert.Equal(SlotStatus.Ready, r.Status);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Info && n.Text.Contains("不会和源视频的原曲混音"));

        // 不复述卡片上已有的内容
        Assert.DoesNotContain(r.Notes, n => n.Text.Contains("将被替换"));
        Assert.DoesNotContain(r.Notes, n => n.Text.Contains("aac"));
    }

    /// <summary>但"丢掉的比显示的多"这种情况必须提醒。</summary>
    [Fact]
    public void 视频槽在多音轨时提醒全部会被丢弃()
    {
        var r = SlotEvaluator.EvaluateVideo(Info(Video(), Aac(), Aac()));

        Assert.Equal(SlotStatus.Ready, r.Status);
        Assert.Contains(r.Notes, n => n.Level == NoteLevel.Caution && n.Text.Contains("2 条音轨"));
    }

    // ─────────── 位深不能从 sample_fmt 推 ───────────

    /// <summary>
    /// 实测：pcm_s24le 的 sample_fmt 是 "s32"（24bit 打包在 32bit 里）。
    /// 如果按 sample_fmt 推断位深，界面上会把 24bit 显示成 32bit。
    /// </summary>
    [Fact]
    public void 位深取bits_per_sample而不是sample_fmt()
    {
        var s = new MediaStreamInfo
        {
            CodecType = "audio", CodecName = "pcm_s24le",
            SampleFormat = "s32", BitsPerSample = 24, BitsPerRawSample = 0,
        };

        Assert.Equal(24, s.EffectiveBits);
    }

    [Fact]
    public void 位深回退到bits_per_raw_sample()
    {
        var s = new MediaStreamInfo
        {
            CodecType = "audio", CodecName = "pcm_s16le",
            BitsPerSample = 0, BitsPerRawSample = 16,
        };

        Assert.Equal(16, s.EffectiveBits);
    }

    [Fact]
    public void 时长格式化()
    {
        Assert.Equal("4:12", MediaFileInfo.FormatDuration(252));
        Assert.Equal("1:02:03", MediaFileInfo.FormatDuration(3723));
        Assert.Equal("—", MediaFileInfo.FormatDuration(null));
        Assert.Equal("—", MediaFileInfo.FormatDuration(0));
    }
}
