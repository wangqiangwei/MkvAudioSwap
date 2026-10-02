namespace MkvAudioSwap.Core;

public enum NoteLevel
{
    /// <summary>灰字信息。用户可能想知道，但不是问题。</summary>
    Info,

    /// <summary>琥珀色提醒。可能有问题，但按钮照常可用，决定权在用户。</summary>
    Caution,

    /// <summary>红色。物理上不可能成功（例如文件里根本没有 PCM 流）。</summary>
    Danger,
}

public sealed record Note(NoteLevel Level, string Text);

/// <summary>
/// 一个槽位的状态。只有 <see cref="SlotStatus.Blocked"/> 会让"开始"变灰，
/// 其余一律允许用户继续 —— 这是刻意的产品决定：程序不替用户判断质量，
/// 只拦"物理上不可能成功"的情况（没有视频流 / 没有 PCM 流）。
/// </summary>
public enum SlotStatus
{
    Empty,
    Ready,
    Blocked,
}

public sealed record SlotEvaluation(
    SlotStatus Status,
    string? BlockReason,
    IReadOnlyList<Note> Notes);

/// <summary>时长比对结论，显示在两个槽位之间。</summary>
public enum DurationMatch
{
    /// <summary>放不出来（数据不足）</summary>
    Unknown,
    /// <summary>一致</summary>
    Match,
    /// <summary>差一点点（不影响使用，但值得提一句）</summary>
    Close,
    /// <summary>明显不一致</summary>
    Mismatch,
}

public sealed record DurationComparison(DurationMatch Kind, string Text, double? DeltaSeconds);

/// <summary>把探测结果翻译成界面要显示的状态。刻意与 UI 无关，方便单元测试。</summary>
public static class SlotEvaluator
{
    /// <summary>超过这个差值才算"明显不一致"。</summary>
    public const double MismatchThresholdSeconds = 0.5;

    /// <summary>低于这个差值连提都不提。</summary>
    public const double CloseThresholdSeconds = 0.1;

    /// <summary>估算体积超过这个值就提醒（PCM 很容易就到这里）。</summary>
    public const long BigOutputBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// 汇总两个槽位之间的提醒（时长不一致、采样率不同、体积等）。
    /// 返回和其它提醒相同的数据结构，好让界面把所有来源合并成一份清单。
    /// </summary>
    public static IReadOnlyList<Note> CollectCrossChecks(
        MediaFileInfo? video, MediaFileInfo? audio, string outputDirectory)
    {
        var items = new List<Note>();
        var comparison = CompareDuration(video, audio);

        if (comparison.Kind == DurationMatch.Mismatch && comparison.DeltaSeconds is { } delta)
        {
            var abs = Math.Abs(delta);
            // 差值本身已经在时长条上显示了，这里只补"后果"，
            // 否则同一个数字会在两处各出现一次。
            items.Add(new Note(NoteLevel.Caution, delta < 0
                ? $"成品结尾会静音 {MediaFileInfo.FormatDuration(abs)}"
                : $"超出画面的 {MediaFileInfo.FormatDuration(abs)} 听不到"));
        }

        var sampleRate = CompareSampleRate(video, audio);
        if (sampleRate is not null) items.Add(sampleRate);

        if (video is not null && audio is not null && !string.IsNullOrEmpty(outputDirectory))
        {
            var sizeNote = CheckSize(video, audio, outputDirectory);
            if (sizeNote is not null) items.Add(sizeNote);
        }

        return items;
    }

    // ────────────────────────── 视频槽 ──────────────────────────

    public static SlotEvaluation EvaluateVideo(MediaFileInfo info)
    {
        var video = info.FirstVideo;
        if (video is null)
        {
            return new SlotEvaluation(
                SlotStatus.Blocked,
                "这个文件里没有找到视频画面。它可能是纯音频文件，或者已损坏。",
                Array.Empty<Note>());
        }

        var notes = new List<Note>();

        // 始终给一条说明。用户最容易误解的两点：成品里只有这一条音轨（不会和原曲混音），
        // 以及想保留伴奏/和声必须自己在 DAW 里混好。
        var src = info.FirstAudio;
        notes.Add(new Note(NoteLevel.Info, src is null
            ? "这个视频没有音轨。成品里只会包含你选的音频。"
            : "成品里只有这一条音轨，不会和源视频的原曲混音。若要保留伴奏或和声，" +
              "请在 DAW 里先把它们混进这个导出文件。"));

        if (info.VideoStreamCount > 1)
            notes.Add(new Note(NoteLevel.Info, $"文件里有 {info.VideoStreamCount} 条视频流，只使用第 1 条"));

        if (info.AudioStreamCount > 1)
            notes.Add(new Note(NoteLevel.Caution, $"源视频有 {info.AudioStreamCount} 条音轨，全部会被丢弃"));

        return new SlotEvaluation(SlotStatus.Ready, null, notes);
    }

    // ────────────────────────── 音频槽 ──────────────────────────

    public static SlotEvaluation EvaluateAudio(MediaFileInfo info)
    {
        var audio = info.FirstAudio;

        if (audio is null)
        {
            return new SlotEvaluation(
                SlotStatus.Blocked,
                "这个文件里没有找到音频流。它可能是视频文件，或者已损坏。",
                Array.Empty<Note>());
        }

        // 只有"确定装不进 MKV"才硬拦（目前就裸 TrueHD 一个）。
        // 白名单之外的编码一律放行 —— 我们没法预先枚举 ffmpeg 支持什么，
        // 硬拦会误伤；真装不进去时 mkv 封装器会给出准确报错。
        if (AudioCodecs.CanCopyToMkv(audio.CodecName) == false)
        {
            return new SlotEvaluation(
                SlotStatus.Blocked,
                $"这个文件的音频编码（{audio.CodecName}）无法原样封进 MKV。\n" +
                "请换一个音频文件，或在 DAW 里重新导出（FLAC / WAV / CAF 都可以）。",
                Array.Empty<Note>());
        }

        var notes = new List<Note>();
        var lossless = AudioCodecs.IsLossless(audio.CodecName);

        // 说明这个文件会被怎么用。
        // 用户最容易误解的是"能不能顺便保留原曲伴奏"，以及"有损文件还有没有救"。
        notes.Add(lossless
            ? new Note(NoteLevel.Info,
                "这个文件会作为成品里唯一的音轨，音频数据逐字节搬运，不做任何压缩或增益处理。")
            : new Note(NoteLevel.Caution,
                $"这个文件是{AudioCodecs.Describe(audio.CodecName)}（{audio.CodecName}），" +
                "音频本身已经有损。工具不会再次编码，会原样搬进成品 —— " +
                "所以成品的声音和这个文件完全一样，但不可能比它更好。"));

        if (info.AudioStreamCount > 1)
            notes.Add(new Note(NoteLevel.Caution, $"文件里有 {info.AudioStreamCount} 条音频流，只使用第 1 条"));

        // 下面这几条只对 PCM 有意义：压缩格式的"位深/采样格式"不是用户控制的东西
        if (AudioCodecs.IsLinearPcm(audio.CodecName))
        {
            var bits = audio.EffectiveBits;
            if (bits is > 0 and <= 8)
            {
                notes.Add(new Note(NoteLevel.Caution,
                    $"位深只有 {bits} 位，音质很低，而且这一步会原样保留它。" +
                    "建议在 DAW 里重新导出为 16 位或 24 位。"));
            }

            var fmt = (audio.SampleFormat ?? "").ToLowerInvariant();
            if (fmt.Contains("flt") || fmt.Contains("dbl"))
            {
                notes.Add(new Note(NoteLevel.Caution,
                    $"这是浮点 PCM（{audio.CodecName}）。能正常使用，" +
                    "但建议导出为定点（16/24 位整数）以避免兼容性问题。"));
            }
        }

        return new SlotEvaluation(SlotStatus.Ready, null, notes);
    }

    // ────────────────────────── 交叉判断 ──────────────────────────

    /// <summary>
    /// 时长比对。这是整个界面上唯一值得上色的东西 ——
    /// 因为时长不一致是唯一会"静默出错"的问题：声音听着挺好，直到结尾突然静音。
    /// </summary>
    public static DurationComparison CompareDuration(MediaFileInfo? video, MediaFileInfo? audio)
    {
        var vd = video?.DurationSeconds;
        var ad = audio?.DurationSeconds;

        if (vd is not > 0 || ad is not > 0)
            return new DurationComparison(DurationMatch.Unknown, "时长未知", null);

        var delta = ad.Value - vd.Value;
        var abs = Math.Abs(delta);

        var vText = MediaFileInfo.FormatDuration(vd);
        var aText = MediaFileInfo.FormatDuration(ad);

        if (abs <= CloseThresholdSeconds)
            return new DurationComparison(DurationMatch.Match, $"时长一致 ({vText} ↔ {aText})", delta);

        if (abs <= MismatchThresholdSeconds)
            return new DurationComparison(DurationMatch.Close,
                $"时长基本一致（差 {abs:0.00} 秒）", delta);

        var direction = delta < 0 ? "短" : "长";
        var detail = delta < 0
            ? $"结尾会静音 {MediaFileInfo.FormatDuration(abs)}"
            : $"音频结尾会超出画面 {MediaFileInfo.FormatDuration(abs)}";

        return new DurationComparison(DurationMatch.Mismatch,
            $"音频比视频{direction} {MediaFileInfo.FormatDuration(abs)}　（视频 {vText} / 音频 {aText}）\n{detail}",
            delta);
    }

    /// <summary>采样率交叉比对。只陈述事实，不判断对错。</summary>
    public static Note? CompareSampleRate(MediaFileInfo? video, MediaFileInfo? audio)
    {
        var vsr = video?.FirstAudio?.SampleRate;
        var asr = audio?.FirstAudio?.SampleRate;

        if (vsr is not > 0 || asr is not > 0) return null;
        if (vsr == asr) return null;

        return new Note(NoteLevel.Caution,
            $"源视频音轨是 {vsr} Hz，你的音频是 {asr} Hz。两者采样率不同，成品里会同时存在；" +
            "如果对不上拍子，多半是这里的原因。");
    }

    /// <summary>体积预估提醒。</summary>
    public static Note? CheckSize(MediaFileInfo? video, MediaFileInfo? audio, string outputDirectory)
    {
        if (video is null || audio?.FirstAudio is null) return null;

        var estimate = SizeEstimator.EstimateOutputBytes(video, audio.FirstAudio);
        if (estimate is null) return null;

        var free = SizeEstimator.GetAvailableFreeSpace(outputDirectory);

        if (free is not null && estimate.Value > free.Value)
        {
            return new Note(NoteLevel.Caution,
                $"输出预计约 {SizeEstimator.FormatBytes(estimate.Value)}，" +
                $"但保存位置只剩 {SizeEstimator.FormatBytes(free.Value)}，空间可能不够。");
        }

        if (estimate.Value > BigOutputBytes)
        {
            return new Note(NoteLevel.Caution,
                $"输出预计约 {SizeEstimator.FormatBytes(estimate.Value)}。" +
                "不重编码意味着音频以原始 PCM 存储，体积会比源视频里的 AAC 大 10~20 倍 —— 这是正常现象。");
        }

        return null;
    }
}
