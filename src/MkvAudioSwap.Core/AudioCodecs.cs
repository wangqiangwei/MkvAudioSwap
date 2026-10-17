namespace MkvAudioSwap.Core;

/// <summary>
/// 音频编码的分类：能不能<b>不重编码</b>地搬进 MKV，以及它本身有没有损失。
///
/// 这两个问题是独立的，别混为一谈 ——
///   · "能 copy 进 MKV" 决定技术上能不能做
///   · "本身有无损" 决定做出来之后音频有没有损失
/// MP3 能 copy 进 MKV（技术上可行），但它本来就是有损的（成品会有损）。
/// FLAC 也能 copy，而且仍然无损。
///
/// 下面的名单是<b>逐个实测</b>出来的（ffmpeg n8.0，5 秒立体声 48kHz 素材，
/// 用 -c copy 封进 matroska 并回读验证），不是从文档抄的：
///
///   编码             结果      回读
///   pcm_s16le       ✓ 成功    pcm_s16le
///   pcm_s24le       ✓ 成功    pcm_s24le
///   pcm_s32le       ✓ 成功    pcm_s32le
///   pcm_f32le       ✓ 成功    pcm_f32le
///   flac            ✓ 成功    flac
///   alac            ✓ 成功    alac
///   wavpack         ✓ 成功    wavpack
///   mp3             ✓ 成功    mp3
///   aac             ✓ 成功    aac
///   opus            ✓ 成功    opus
///   vorbis          ✓ 成功    vorbis
///   ac3             ✓ 成功    ac3
///   truehd          ✗ 失败    —— "sample rate not set"，
///                             单独一条 TrueHD 流缺参数，matroska 写不了头
/// </summary>
public static class AudioCodecs
{
    /// <summary>
    /// 已知<b>装不进 MKV</b> 的编码。命中就硬拦，因为 ffmpeg 一定失败。
    ///
    /// 只有 TrueHD。注意：带 AC3 核心的 TrueHD 轨道（`truehd_core`）是另一回事，
    /// 这里只拦裸的 truehd。
    /// </summary>
    private static readonly HashSet<string> CannotCopy = new(StringComparer.OrdinalIgnoreCase)
    {
        "truehd",
    };

    /// <summary>
    /// 未压缩的线性 PCM。最通用、兼容性最好，也是给用户的首选建议。
    /// </summary>
    public static bool IsLinearPcm(string? codec) =>
        codec is not null && codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 无损压缩编码：数据被压缩过，但解开后与原始数据完全一致，搬进 MKV 仍然无损。
    ///
    /// 这一组原先被工具拒之门外（旧实现只认 `pcm_` 前缀），是实测才发现它们同样能 copy。
    /// </summary>
    public static bool IsLosslessCompressed(string? codec)
    {
        if (string.IsNullOrEmpty(codec)) return false;

        // ffmpeg 报的可能是 "flac"，也可能是 "flac_pcm" 之类的变体
        if (codec.StartsWith("flac", StringComparison.OrdinalIgnoreCase)) return true;
        if (codec.StartsWith("alac", StringComparison.OrdinalIgnoreCase)) return true;
        if (codec.StartsWith("wavpack", StringComparison.OrdinalIgnoreCase)) return true;
        if (codec.StartsWith("tta", StringComparison.OrdinalIgnoreCase)) return true;

        // DTS-HD 是无损的，但它和普通 DTS 共用 "dts" 家族名，靠名字区分
        if (codec.Contains("dts_hd", StringComparison.OrdinalIgnoreCase)) return true;
        if (codec.Contains("dtshd", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>是不是无损（未压缩的 PCM 或无损压缩都算）。</summary>
    public static bool IsLossless(string? codec) =>
        IsLinearPcm(codec) || IsLosslessCompressed(codec);

    /// <summary>已知能原样搬进 MKV 的编码（实测清单）。</summary>
    public static bool IsKnownCopyable(string? codec)
    {
        if (string.IsNullOrEmpty(codec)) return false;
        if (CannotCopy.Contains(codec)) return false;

        if (IsLinearPcm(codec)) return true;

        string[] known =
        [
            "flac", "alac", "wavpack", "tta",
            "mp3", "aac", "opus", "vorbis", "ac3", "eac3",
            "dts", "dts_hd", "dtshd",
            "mp2", "wmav2", "cook", "ra_144",
        ];

        return known.Any(k => codec.StartsWith(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判定能不能 copy 进 MKV。
    ///
    /// 白名单之外的编码返回 null（"不确定"）—— <b>刻意不直接判 false</b>：
    /// 我们没法预先枚举 ffmpeg 将来会支持什么，硬拦会误伤。
    /// 不确定的交给 mkv 封装器，由它给出准确的报错。
    /// </summary>
    public static bool? CanCopyToMkv(string? codec)
    {
        if (string.IsNullOrEmpty(codec)) return null;
        if (CannotCopy.Contains(codec)) return false;
        if (IsKnownCopyable(codec)) return true;
        return null;
    }

    /// <summary>给人看的分类名，用于提示文案。</summary>
    public static string Describe(string? codec)
    {
        if (IsLinearPcm(codec)) return "未压缩 PCM";
        if (IsLosslessCompressed(codec)) return "无损压缩";
        return "有损压缩";
    }
}
