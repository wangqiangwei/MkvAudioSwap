using System.Text;
using Avalonia.Media;
using MkvAudioSwap.App.Services;
using MkvAudioSwap.Core;

namespace MkvAudioSwap.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly MediaInspector _inspector = new();

    /// <summary>
    /// 串行化文件载入。用户连续拖入两个文件时，两次探测会并发进行，
    /// 而 Busy 标志和槽位状态是共享的 —— 不加这把锁会出现
    /// "第二个文件偶尔没载入"这种偶发且难以复现的问题。
    /// </summary>
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private MediaFileInfo? _videoInfo;
    private MediaFileInfo? _audioInfo;

    private string? _manualOutputDirectory;
    private string _outputDirectory = "";
    private string _outputDirectoryShort = "";

    private string _durationSymbol = "";
    private string _durationText = "选好两个文件后检查时长";
    private IBrush _durationBrush = Palette.Muted;
    private bool _hasWarning;
    private string _warningSummary = "";

    private bool _busy;
    private double _progress;
    private string _progressText = "";

    private string _statusText = "";
    private IBrush _statusBrush = Palette.Text;

    // ── 合成摘要：输出文件名 / 体积 / 保存位置 / 时长可视化 ──
    private string _outputFileName = "选好视频后显示输出文件名";
    private string _outputFilePath = "";
    private string _outputFolderText = "";
    private string _estimatedSizeText = "选好音频后估算体积";
    private bool _hasOutputName;
    private bool _hasEstimatedSize;
    private bool _hasOutputFolder;

    private string _offsetText = "0";
    private double _appliedOffsetSeconds;

    private bool _hasResult;
    private string _resultPath = "";
    private string _resultDetail = "";

    public MainViewModel()
    {
        Video = new SlotViewModel(
            "视频", "把视频拖到这里", "保留第 1 条视频流，画面零损失", "选择视频",
            step: "01", role: "源视频", isVideo: true);
        Audio = new SlotViewModel(
            "音频", "把音频拖到这里", "作为成品里唯一的音轨，逐字节搬运", "选择音频",
            step: "02", role: "你的音频", isVideo: false);

        Video.PropertyChanged += OnSlotChanged;
        Audio.PropertyChanged += OnSlotChanged;

        RefreshOutputDirectory();
    }

    private void OnSlotChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SlotViewModel.HasFile) or nameof(SlotViewModel.Status)) Recompute();
    }

    public SlotViewModel Video { get; }
    public SlotViewModel Audio { get; }

    public MediaInspector Inspector => _inspector;

    // ────────────────────────── 状态 ──────────────────────────

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            Raise(nameof(CanStart));
            Raise(nameof(CanPickFiles));
            Raise(nameof(IsIdle));
            RaiseHint();
        }
    }

    public bool IsIdle => !Busy;

    /// <summary>
    /// "开始"仅在两个槽位都装入结构上能成功的文件时可用。
    /// 槽位为空 → 灰；文件里没有视频流 / 没有 PCM 流 → 灰（在卡片里说明原因）。
    /// 质量类问题（时长、体积、位深、容器）永远不让它变灰。
    /// </summary>
    public bool CanStart =>
        !Busy &&
        Video.Status == SlotStatus.Ready &&
        Audio.Status == SlotStatus.Ready;

    public bool CanPickFiles => !Busy;

    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    /// <summary>主界面唯一的一行状态文字（临时消息 / 错误 / 进度）。</summary>
    public string StatusText { get => _statusText; private set { if (Set(ref _statusText, value)) RaiseHint(); } }
    public IBrush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    /// <summary>
    /// 常驻提示。只在"用户需要被引导"的时候才给文字：
    ///   · 什么都还没选 → 告诉他该干什么
    ///   · 只差一个文件   → 告诉他缺哪个
    ///   · 文件不能处理   → 告诉他去看卡片
    ///
    /// 【刻意留空的情况】两个文件都就绪、可以开始时，返回空字符串。
    /// 此时按钮是通栏高亮的，"开始"两个字含义自明，下面再挂一句说明只是视觉噪音。
    /// 同理也不为"有提醒"写提示：提醒自己就在上面一行、还带着「详情」链接。
    /// 这一行空了，整个 Grid 行会塌缩，不会留下空白。
    /// </summary>
    public string HintText
    {
        get
        {
            if (CanStart) return "";
            if (Video.Status == SlotStatus.Blocked || Audio.Status == SlotStatus.Blocked)
                return "有文件无法处理，请看卡片内的红色说明。";
            if (Video.IsEmpty && Audio.IsEmpty) return "先选好视频和音频，或者把它们拖进上面的框";
            if (Video.IsEmpty) return "还差一个视频（源画面）";
            if (Audio.IsEmpty) return "还差一个音频（你在 DAW 里导出的线性 PCM）";
            return "";
        }
    }

    /// <summary>当前显示的常驻提示（次要灰）。</summary>
    public string HintTextDisplay => HintText;

    /// <summary>是否显示状态行。只在这一行真的有内容时才占位，否则整行塌缩。</summary>
    public bool ShowStatusArea =>
        Busy || !string.IsNullOrWhiteSpace(StatusLine);

    /// <summary>实际显示在主按钮下方的那一行：有临时消息就显示临时消息，否则显示常驻提示。</summary>
    public string StatusLine => string.IsNullOrWhiteSpace(StatusText) ? HintText : StatusText;

    /// <summary>当前显示的是常驻提示（用次要灰），还是临时消息（用 StatusBrush 的颜色）。</summary>
    public IBrush StatusLineBrush =>
        string.IsNullOrWhiteSpace(StatusText) ? Palette.Muted : StatusBrush;

    private void RaiseHint()
    {
        Raise(nameof(HintText));
        Raise(nameof(HintTextDisplay));
        Raise(nameof(StatusLine));
        Raise(nameof(ShowStatusArea));
        Raise(nameof(StatusLineBrush));
    }

    // ── 时长比对 ──
    public string DurationSymbol { get => _durationSymbol; private set => Set(ref _durationSymbol, value); }
    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }
    public IBrush DurationBrush { get => _durationBrush; private set => Set(ref _durationBrush, value); }

    public bool IsDurationMatch { get; private set; }
    public bool IsDurationClose { get; private set; }
    public bool IsDurationMismatch { get; private set; }
    public bool HasDurationData => VideoTimelineSeconds > 0 && AudioTimelineSeconds > 0;

    /// <summary>音频条的颜色：一致时用浅一档的绿，不一致时用琥珀，避免两条糊成一块。</summary>
    public IBrush TimelineAudioBrush => IsDurationMismatch ? Palette.Warn : Palette.TimelineAudio;

    /// <summary>合成摘要里的双轨时间轴：视频时长（秒）。</summary>
    public double VideoTimelineSeconds => _videoInfo?.DurationSeconds ?? 0;

    /// <summary>合成摘要里的双轨时间轴：音频时长（秒）。</summary>
    public double AudioTimelineSeconds => _audioInfo?.DurationSeconds ?? 0;

    /// <summary>合成摘要里的双轨时间轴：当前输入框里的偏移（秒，未提交也生效）。</summary>
    public double TimelineOffsetSeconds => ParseOffsetSeconds();

    // ── 合并后的提醒区（把以前散落三处的同类信息收成一条）──
    public bool HasWarning { get => _hasWarning; private set => Set(ref _hasWarning, value); }
    public string WarningSummary { get => _warningSummary; private set => Set(ref _warningSummary, value); }

    // ── 合成摘要 ──
    public string OutputFileName { get => _outputFileName; private set => Set(ref _outputFileName, value); }
    public string OutputFilePath { get => _outputFilePath; private set => Set(ref _outputFilePath, value); }
    public string OutputFolderText { get => _outputFolderText; private set => Set(ref _outputFolderText, value); }
    public string EstimatedSizeText { get => _estimatedSizeText; private set => Set(ref _estimatedSizeText, value); }
    public bool HasOutputName { get => _hasOutputName; private set => Set(ref _hasOutputName, value); }
    public bool HasEstimatedSize { get => _hasEstimatedSize; private set => Set(ref _hasEstimatedSize, value); }
    public bool HasOutputFolder { get => _hasOutputFolder; private set => Set(ref _hasOutputFolder, value); }

    /// <summary>输出文件名可能很长，Tooltip 里给完整路径。</summary>
    public string OutputFileTooltip => string.IsNullOrEmpty(OutputFilePath) ? OutputFileName : OutputFilePath;

    /// <summary>
    /// 时长条中间的引导语。时长不一致时把"可以试试音频偏移"直接说出来 ——
    /// 否则用户只看到一个差值，不知道偏移动能就是为这种情况准备的。
    /// </summary>
    public string DurationHint { get; private set; } = "";
    public bool HasDurationHint => !string.IsNullOrEmpty(DurationHint);

    private void SetDurationHint(string text)
    {
        if (DurationHint == text) return;
        DurationHint = text;
        Raise(nameof(DurationHint));
        Raise(nameof(HasDurationHint));
    }

    // ── 提醒入口 ──
    // 完整提醒在独立的"提醒"弹窗里显示，主界面不再做行内展开。
    // 原因：行内展开会把卡片和窗口撑高、把布局挤出窗口边界，
    // 实测表现为"点了详情什么都没出现"。弹窗让主界面高度恒定，
    // 从根上不存在这个问题，也和偏移/设置的做法一致。

    /// <summary>
    /// 提醒入口文案。刻意就是"详情"两个字 —— 短、明确。
    /// 曾经写成"查看全部 3 条提醒"，结果和左边的摘要挤在同一行，两边都显得局促。
    /// </summary>
    public string WarningLinkText => "详情";

    /// <summary>提醒数量，例如“2 处”。只有多于一条时才显示，避免一条提醒也占两个位置。</summary>
    public string WarningCountLabel => CautionCount > 1 ? $"{CautionCount} 处" : "";
    public bool HasWarningCount => CautionCount > 1;

    /// <summary>
    /// 摘要的短标签，例如"提醒 2 处"。
    /// 拆成"标签 + 描述"两段是为了让右侧的「详情」按钮有独立位置，
    /// 而描述行可以省略号截断 —— 这样一条提醒也不会把按钮挤走。
    /// </summary>
    public string WarningTag => CautionCount > 1 ? $"⚠ {CautionCount} 处" : "⚠";

    // ── 偏移 ──
    public string OffsetText
    {
        get => _offsetText;
        set
        {
            if (!Set(ref _offsetText, value)) return;
            Raise(nameof(OffsetLabel));
            Raise(nameof(HasOffset));
            Raise(nameof(TimelineOffsetSeconds));
        }
    }

    public double AppliedOffsetSeconds
    {
        get => _appliedOffsetSeconds;
        private set
        {
            if (!Set(ref _appliedOffsetSeconds, value)) return;
            Raise(nameof(OffsetLabel));
            Raise(nameof(HasOffset));
            Raise(nameof(TimelineOffsetSeconds));
        }
    }

    public bool HasOffset => Math.Abs(ParseOffsetSeconds()) > 0.0005;

    /// <summary>主界面上的偏移入口文案。没设置时只显示"无"，不占视觉重量。</summary>
    public string OffsetLabel
    {
        get
        {
            var ms = ParseOffsetSeconds() * 1000;
            return Math.Abs(ms) < 0.5 ? "无" : $"{ms:+0;-0;0} 毫秒";
        }
    }

    public void CommitOffset() => AppliedOffsetSeconds = ParseOffsetSeconds();

    // ── 输出位置 ──
    public string OutputDirectory { get => _outputDirectory; private set => Set(ref _outputDirectory, value); }
    public string OutputDirectoryShort { get => _outputDirectoryShort; private set => Set(ref _outputDirectoryShort, value); }
    public bool OutputDirectoryIsDefault => _manualOutputDirectory is null;

    // ── 结果 ──
    public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }
    public string ResultPath { get => _resultPath; private set => Set(ref _resultPath, value); }
    public string ResultDetail { get => _resultDetail; private set => Set(ref _resultDetail, value); }

    // ────────────────────────── 选择文件 ──────────────────────────

    public Task LoadVideoAsync(string path) => LoadAsync(path, isVideo: true);

    public Task LoadAudioAsync(string path) => LoadAsync(path, isVideo: false);

    /// <summary>
    /// 拖入文件时按扩展名归类。只取第一个，其余忽略 —— 但会明确告诉用户忽略了几个，
    /// 否则用户会以为程序没响应。
    /// </summary>
    public async Task LoadDroppedAsync(IEnumerable<string> paths)
    {
        // 先按扩展名归类，再顺序载入（LoadAsync 内部有锁，会排队）。
        // 拖入时按类型自动分派，多余的忽略 —— 但必须告诉用户忽略了几个，
        // 否则用户会以为程序没响应。
        var list = paths.Where(File.Exists).ToList();
        if (list.Count == 0) return;

        var video = list.FirstOrDefault(IsVideoExtension);
        var audio = list.FirstOrDefault(IsAudioExtension);

        // 扩展名不认识时：按内容试一次，让 ffprobe 决定它是什么
        if (video is null && audio is null && Video.Status == SlotStatus.Empty)
            video = list[0];

        var used = new HashSet<string>();
        if (video is not null) used.Add(video);
        if (audio is not null) used.Add(audio);
        var ignored = list.Count - used.Count;

        if (video is not null) await LoadVideoAsync(video).ConfigureAwait(true);
        if (audio is not null) await LoadAudioAsync(audio).ConfigureAwait(true);

        if (video is null && audio is null)
        {
            SetStatus("这个文件既不是视频也不是音频，请换一个。", Palette.Warn);
            return;
        }

        // 提示必须在两个文件都载入完之后再设，否则会被后一个文件的载入过程覆盖掉
        var parts = new List<string>();
        if (video is not null) parts.Add("1 个视频");
        if (audio is not null) parts.Add("1 个音频");
        var msg = $"已放入 {string.Join(" + ", parts)}";
        if (ignored > 0) msg += $"；忽略了 {ignored} 个（一次只处理一组）";

        ShowTransientStatus(msg, Palette.Muted);
    }

    private int _statusToken;

    /// <summary>
    /// 显示一条会自己消失的提示。用户不需要读第二遍的信息不该一直占着界面底部。
    /// 用序号丢弃过期的延时清空任务，避免连续拖入时旧的清空把新提示打断。
    /// </summary>
    private void ShowTransientStatus(string text, IBrush brush)
    {
        SetStatus(text, brush);
        var token = ++_statusToken;

        _ = Task.Run(async () =>
        {
            await Task.Delay(4000).ConfigureAwait(false);
            if (Volatile.Read(ref _statusToken) != token) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _statusToken) != token) return;
                SetStatus("", Palette.Text);
            }).GetTask().ConfigureAwait(false);
        });
    }

    private static readonly string[] VideoExtensions =
        [".mp4", ".mov", ".mkv", ".m4v", ".webm", ".avi", ".ts", ".flv", ".wmv", ".mpg", ".mpeg", ".m2ts", ".vob"];

    private static readonly string[] AudioExtensions =
        [".caf", ".wav", ".wave", ".bwf", ".rf64", ".aif", ".aiff", ".aifc", ".flac", ".alac", ".m4a", ".mp3", ".aac", ".ogg", ".opus", ".wma"];

    /// <summary>选择框的文件类型过滤。和拖拽归类共用同一份扩展名列表，避免两处不一致。</summary>
    public static readonly string[] VideoPickerPatterns = VideoExtensions.Select(e => "*" + e).ToArray();
    public static readonly string[] AudioPickerPatterns = AudioExtensions.Select(e => "*" + e).ToArray();

    public static bool IsVideoExtension(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsAudioExtension(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private async Task LoadAsync(string path, bool isVideo)
    {
        // 串行执行，避免两次并发探测互相覆盖状态
        await _loadLock.WaitAsync().ConfigureAwait(true);

        var slot = isVideo ? Video : Audio;

        try
        {
            Busy = true;
            SetStatus("正在读取文件信息…", Palette.Muted);
            slot.Clear();
            if (isVideo) _videoInfo = null; else _audioInfo = null;
            Recompute();

            await ProbeIntoSlotAsync(path, isVideo, slot).ConfigureAwait(true);
        }
        finally
        {
            Busy = false;
            _loadLock.Release();
            Recompute();
        }
    }

    private async Task ProbeIntoSlotAsync(string path, bool isVideo, SlotViewModel slot)
    {
        try
        {
            var info = await _inspector.ProbeAsync(path).ConfigureAwait(true);
            slot.FileName = info.FileName;
            slot.FilePath = info.Path;
            slot.FileSizeText = info.FileSizeBytes is > 0
                ? SizeEstimator.FormatBytes(info.FileSizeBytes.Value)
                : "";

            var evaluation = isVideo
                ? SlotEvaluator.EvaluateVideo(info)
                : SlotEvaluator.EvaluateAudio(info);

            slot.Status = evaluation.Status;
            slot.BlockReason = evaluation.BlockReason;

            if (evaluation.Status == SlotStatus.Blocked)
            {
                // 被拦住的文件不填规格行：继续显示规格会让用户以为它还能用
                slot.SpecLine = "";
                slot.SpecChips.Clear();
                slot.SecondaryLine = "";
                slot.Notes.Clear();
                slot.HasFile = true;

                SetStatus("这个文件不能用于替换，原因见卡片内说明。", Palette.Danger);
            }
            else
            {
                FillSpec(slot, info, isVideo);
                slot.Notes.Clear();
                // 探测时就把来源写进备注，汇总清单里才不会分不清是哪边的提醒
                var source = isVideo ? "来自视频文件" : "来自音频文件";
                foreach (var n in evaluation.Notes)
                    slot.Notes.Add(new NoteViewModel(n, source));
                slot.HasFile = true;

                if (isVideo)
                {
                    _videoInfo = info;
                    if (_manualOutputDirectory is null) RefreshOutputDirectory();
                }
                else
                {
                    _audioInfo = info;
                }

                SetStatus("", Palette.Text);
            }
        }
        catch (MediaProbeException ex)
        {
            slot.Status = SlotStatus.Blocked;
            slot.BlockReason = ex.Message;
            slot.FileName = Path.GetFileName(path);
            slot.FilePath = path;
            slot.FileSizeText = "";
            slot.SpecChips.Clear();
            slot.HasFile = true;
            if (isVideo) _videoInfo = null; else _audioInfo = null;
            SetStatus("读不出这个文件的信息。", Palette.Danger);
        }
        catch (Exception ex)
        {
            slot.Status = SlotStatus.Blocked;
            slot.BlockReason = $"读取文件时出错：{ex.Message}";
            slot.FileName = Path.GetFileName(path);
            slot.FilePath = path;
            slot.FileSizeText = "";
            slot.SpecChips.Clear();
            slot.HasFile = true;
            if (isVideo) _videoInfo = null; else _audioInfo = null;
            SetStatus("读不出这个文件的信息。", Palette.Danger);
        }
    }

    /// <summary>
    /// 填写卡片里的规格。卡片是“快速核对”的地方，不是信息面板：
    /// 只把用户需要核对的字段拆成 chips，更细的编码细节放进“提醒”弹窗。
    /// </summary>
    private static void FillSpec(SlotViewModel slot, MediaFileInfo info, bool isVideo)
    {
        slot.SpecChips.Clear();

        if (isVideo)
        {
            var v = info.FirstVideo!;
            var fps = MediaFileInfo.ParseFrameRate(v.FrameRate);

            if (v.Width > 0 && v.Height > 0) slot.SpecChips.Add($"{v.Width}×{v.Height}");
            if (fps is > 0) slot.SpecChips.Add($"{fps}fps");
            if (!string.IsNullOrWhiteSpace(v.CodecName)) slot.SpecChips.Add(DisplayCodec(v.CodecName));
            slot.SpecChips.Add(MediaFileInfo.FormatDuration(info.DurationSeconds));

            slot.SpecLine =
                $"{v.Width}×{v.Height}" +
                (fps is > 0 ? $" · {fps}fps" : "") +
                $" · {v.CodecName}" +
                $" · {MediaFileInfo.FormatDuration(info.DurationSeconds)}";

            var src = info.FirstAudio;
            slot.SecondaryLine = src is null
                ? "源视频没有音轨"
                : $"源音轨 {DisplayCodec(src.CodecName)}" +
                  (src.SampleRate > 0 ? $" {src.SampleRate}Hz" : "") +
                  (src.Channels > 0 ? $" {DescribeChannels(src.Channels)}" : "") +
                  "（将被替换）";
        }
        else
        {
            var a = info.FirstAudio!;

            if (!string.IsNullOrWhiteSpace(a.CodecName)) slot.SpecChips.Add(DisplayCodec(a.CodecName));
            if (a.SampleRate > 0) slot.SpecChips.Add($"{a.SampleRate} Hz");
            if (a.EffectiveBits > 0) slot.SpecChips.Add($"{a.EffectiveBits} bit");
            if (a.Channels > 0) slot.SpecChips.Add(DescribeChannels(a.Channels));
            slot.SpecChips.Add(MediaFileInfo.FormatDuration(info.DurationSeconds));

            slot.SpecLine =
                $"{a.CodecName}" +
                (a.SampleRate > 0 ? $" · {a.SampleRate}Hz" : "") +
                (a.EffectiveBits > 0 ? $" · {a.EffectiveBits}bit" : "") +
                (a.Channels > 0 ? $" · {DescribeChannels(a.Channels)}" : "") +
                $" · {MediaFileInfo.FormatDuration(info.DurationSeconds)}";

            slot.SecondaryLine = "";
        }
    }

    /// <summary>把 ffprobe 的编码名变成用户认得的写法。未知编码不隐藏，原样大写显示。</summary>
    private static string DisplayCodec(string codec) => codec.ToLowerInvariant() switch
    {
        "h264" => "H.264",
        "hevc" or "h265" => "HEVC",
        "vp9" => "VP9",
        "av1" => "AV1",
        "pcm_s16le" => "PCM",
        "pcm_s24le" => "PCM",
        "pcm_s32le" => "PCM",
        "pcm_f32le" => "PCM float",
        "pcm_f64le" => "PCM float",
        "flac" => "FLAC",
        "alac" => "ALAC",
        "aac" => "AAC",
        "mp3" => "MP3",
        "opus" => "Opus",
        "vorbis" => "Vorbis",
        "ac3" => "AC-3",
        "wavpack" => "WavPack",
        "tta" => "TTA",
        "truehd" => "TrueHD",
        _ => codec.ToUpperInvariant(),
    };

    /// <summary>拼出更细的文件信息，用于"提醒"弹窗的补充说明。</summary>
    private static string ComposeFileMeta(MediaFileInfo info, string container, int streamIndex, int streamCount, string kind)
    {
        var meta = new List<string>();

        if (info.FileSizeBytes is > 0) meta.Add($"源文件 {FormatBytes(info.FileSizeBytes.Value)}");
        if (container.Length > 0) meta.Add($"容器 {container}");
        if (info.FormatBitRate is > 0) meta.Add($"总码率 {FormatBitrate(info.FormatBitRate.Value)}");
        meta.Add($"{kind}流 #{streamIndex}");
        if (streamCount > 1) meta.Add($"共 {streamCount} 条，用第 1 条");

        return " · " + string.Join(" · ", meta);
    }

    /// <summary>码率格式化。用户看的是"这个文件多大流量"，用 Mbps/kbps 最直观。</summary>
    public static string FormatBitrate(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0) return "—";
        if (bitsPerSecond >= 1_000_000) return $"{bitsPerSecond / 1_000_000.0:0.##} Mbps";
        return $"{bitsPerSecond / 1000.0:0.#} kbps";
    }

    /// <summary>文件体积格式化，用二进制单位（与资源管理器一致）。</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }

        return $"{v:0.#} {units[i]}";
    }

    private static string DescribeChannels(int channels) => channels switch
    {
        1 => "单声道",
        2 => "立体声",
        _ => $"{channels}声道",
    };

    // ────────────────────────── 派生状态 ──────────────────────────

    public void Recompute()
    {
        var comparison = SlotEvaluator.CompareDuration(_videoInfo, _audioInfo);

        DurationSymbol = comparison.Kind switch
        {
            DurationMatch.Match => "✓",
            DurationMatch.Close => "≈",
            DurationMatch.Mismatch => "⚠",
            _ => "",
        };

        DurationText = comparison.Kind switch
        {
            DurationMatch.Match => $"时长一致 · {MediaFileInfo.FormatDuration(_videoInfo?.DurationSeconds)}",
            DurationMatch.Close => $"基本一致 · 差 {Math.Abs(comparison.DeltaSeconds ?? 0):0.00} 秒",
            // 不一致时这里只给结论值，具体后果放在下面的提醒行，避免同一句话说两遍
            DurationMatch.Mismatch => comparison.DeltaSeconds is { } d
                ? (d < 0 ? $"音频短 {MediaFileInfo.FormatDuration(-d)}" : $"音频长 {MediaFileInfo.FormatDuration(d)}")
                : "时长不一致",
            _ => (Video.IsEmpty && Audio.IsEmpty) ? "选好两个文件后检查时长"
                : (Video.IsEmpty || Audio.IsEmpty) ? "还差一个文件"
                : "时长未知",
        };

        IsDurationMatch = comparison.Kind == DurationMatch.Match;
        IsDurationClose = comparison.Kind == DurationMatch.Close;
        IsDurationMismatch = comparison.Kind == DurationMatch.Mismatch;

        DurationBrush = comparison.Kind switch
        {
            DurationMatch.Match => Palette.Success,
            DurationMatch.Close => Palette.Muted,
            DurationMatch.Mismatch => Palette.Warn,
            _ => Palette.Muted,
        };

        RebuildWarnings(comparison);
        RebuildOutputSummary();

        // 时长不一致时，把用户引到偏移功能上
        SetDurationHint(comparison.Kind == DurationMatch.Mismatch
            ? "整体差一点？用右侧「音频偏移」校正"
            : "");

        Raise(nameof(CanStart));
        Raise(nameof(HasOffset));
        Raise(nameof(OffsetLabel));
        Raise(nameof(IsDurationMatch));
        Raise(nameof(IsDurationClose));
        Raise(nameof(IsDurationMismatch));
        Raise(nameof(TimelineAudioBrush));
        Raise(nameof(HasDurationData));
        Raise(nameof(VideoTimelineSeconds));
        Raise(nameof(AudioTimelineSeconds));
        Raise(nameof(TimelineOffsetSeconds));
        RaiseHint();
    }

    /// <summary>
    /// 合成摘要：把"这次会得到什么"提前摆出来 —— 文件名、容器、预估体积、保存位置。
    /// 只读 Core 的纯函数，不创建文件，也不改变输出命名的实际行为。
    /// </summary>
    private void RebuildOutputSummary()
    {
        if (_videoInfo is null)
        {
            OutputFileName = "选好视频后显示输出文件名";
            OutputFilePath = "";
            OutputFolderText = "";
            EstimatedSizeText = "选好音频后估算体积";
            HasOutputName = false;
            HasEstimatedSize = false;
            HasOutputFolder = false;
            Raise(nameof(OutputFileTooltip));
            return;
        }

        var outputPath = OutputNaming.BuildOutputPath(_videoInfo.Path, OutputDirectory);
        OutputFileName = Path.GetFileName(outputPath);
        OutputFilePath = outputPath;
        OutputFolderText = "保存到 " + ShortenPath(Path.GetDirectoryName(outputPath) ?? "", 46);
        HasOutputName = true;
        HasOutputFolder = true;

        if (_audioInfo?.FirstAudio is { } audio)
        {
            var estimate = SizeEstimator.EstimateOutputBytes(_videoInfo, audio);
            EstimatedSizeText = estimate is { } bytes
                ? $"预计 {SizeEstimator.FormatBytes(bytes)}"
                : "体积待估算";
        }
        else
        {
            EstimatedSizeText = "选好音频后估算体积";
        }

        HasEstimatedSize = true;
        Raise(nameof(OutputFileTooltip));
    }

    /// <summary>
    /// 重建提醒。所有提醒（两个卡片各自的 + 跨文件的）统一收进一份清单，
    /// 主界面只显示一条摘要，完整内容在"提醒"弹窗里看。
    ///
    /// 之所以做成弹窗而不是行内展开：行内展开会把卡片和窗口撑高，
    /// 进而把整个布局挤变形（实测详情被推到窗口外，用户以为"点了没反应"）。
    /// 弹窗让主界面高度恒定，从根上不存在这个问题。
    /// </summary>
    private void RebuildWarnings(DurationComparison comparison)
    {
        var cross = SlotEvaluator.CollectCrossChecks(_videoInfo, _audioInfo, OutputDirectory);

        var items = new List<NoteViewModel>();
        // 槽位的 Notes 在探测时就已经带好来源标签，这里直接合并；
        // 跨文件的提醒补上"视频与音频之间"这个来源。
        items.AddRange(Video.Notes);
        items.AddRange(Audio.Notes);
        foreach (var n in cross) items.Add(new NoteViewModel(n, "视频与音频之间"));

        WarningNotices = items;

        var cautions = items.Where(i => i.IsCaution).ToList();

        if (cautions.Count == 0)
        {
            HasWarning = false;
            WarningSummary = "";
        }
        else
        {
            // 摘要只说第一条 + 还有几条，完整内容在弹窗里，避免同一句话占两行
            WarningSummary = cautions.Count == 1
                ? cautions[0].Text
                : $"{cautions[0].Text}（另有 {cautions.Count - 1} 条）";
            HasWarning = true;
        }

        // 卡片底部的"提醒条数"入口
        Video.RefreshNoticeSummary();
        Audio.RefreshNoticeSummary();

        RaiseWarningDerived();
    }

    /// <summary>所有提醒的完整清单，按警告在前、说明在后排序。</summary>
    public IReadOnlyList<NoteViewModel> WarningNotices { get; private set; } = Array.Empty<NoteViewModel>();


    /// <summary>提醒总数，用于弹窗标题。</summary>
    public int NoticeCount => WarningNotices.Count;

    public int CautionCount => WarningNotices.Count(n => n.IsCaution);

    private void RaiseWarningDerived()
    {
        Raise(nameof(WarningLinkText));
        Raise(nameof(WarningTag));
        Raise(nameof(WarningCountLabel));
        Raise(nameof(HasWarningCount));
        Raise(nameof(NoticeCount));
        Raise(nameof(CautionCount));
        RaiseHint();
    }

    // ────────────────────────── 输出目录 ──────────────────────────

    public void RefreshOutputDirectory()
    {
        if (_manualOutputDirectory is not null)
            OutputDirectory = _manualOutputDirectory;
        else if (_videoInfo is not null)
            OutputDirectory = Path.GetDirectoryName(Path.GetFullPath(_videoInfo.Path)) ?? "";
        else
            OutputDirectory = "(选择视频后自动设为视频所在目录)";

        OutputDirectoryShort = ShortenPath(OutputDirectory);
        Raise(nameof(OutputDirectoryIsDefault));
    }

    /// <summary>路径太长时中间省略，避免把弹窗撑开。</summary>
    public static string ShortenPath(string path, int max = 52)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= max) return path;

        var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 2) return path;

        var tail = string.Join(Path.DirectorySeparatorChar, parts[^2..]);
        var head = parts[0];
        var result = head + Path.DirectorySeparatorChar + "…" + Path.DirectorySeparatorChar + tail;

        return result.Length <= max ? result : "…" + Path.DirectorySeparatorChar + tail;
    }

    public void SetOutputDirectory(string path)
    {
        _manualOutputDirectory = path;
        RefreshOutputDirectory();
        Recompute();
    }

    public void ResetOutputDirectoryToDefault()
    {
        _manualOutputDirectory = null;
        RefreshOutputDirectory();
        Recompute();
    }

    // ────────────────────────── 开始 ──────────────────────────

    public double ParseOffsetSeconds()
    {
        var raw = (OffsetText ?? "").Trim().Replace("，", "").Replace(",", "").Replace("毫秒", "");
        if (raw.Length == 0) return 0;

        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ms))
            return 0;

        // 界面单位是毫秒，ffmpeg 要秒
        return ms / 1000.0;
    }

    /// <summary>
    /// 把偏移量写成一句人话。预览的状态提示里会带上它，
    /// 让用户能确认"程序这次用的偏移"到底是多少，而不是只看画面猜。
    /// </summary>
    public static string OffsetNote(double offsetSeconds) =>
        Math.Abs(offsetSeconds) < 0.0005
            ? "无偏移"
            : $"偏移 {offsetSeconds * 1000:+0;-0;0} 毫秒";

    public async Task RunSwapAsync()
    {
        if (!CanStart || _videoInfo is null || _audioInfo is null) return;

        var outputPath = OutputNaming.BuildOutputPath(_videoInfo.Path, OutputDirectory);
        var offset = ParseOffsetSeconds();

        Busy = true;
        HasResult = false;
        Progress = 0;
        ProgressText = "0%";
        SetStatus("正在封装，请稍等…", Palette.Text);

        try
        {
            var runner = _inspector.CreateRunner();
            var progress = new Progress<SwapProgress>(p =>
            {
                Progress = p.Fraction;
                ProgressText = $"{p.Percent}%";
            });

            var finalPath = await runner.SwapAsync(
                _videoInfo.Path, _audioInfo.Path, outputPath, offset,
                _videoInfo.DurationSeconds, progress, CancellationToken.None).ConfigureAwait(true);

            var resultInfo = new FileInfo(finalPath);
            ResultPath = finalPath;
            ResultDetail = $"{SizeEstimator.FormatBytes(resultInfo.Length)} · 全程未重编码" +
                           (Math.Abs(offset) > 0.0005 ? $" · 偏移 {offset * 1000:+0;-0;0} 毫秒" : "");
            HasResult = true;
            ProgressText = "";
            SetStatus("", Palette.Text);

            ResultReady?.Invoke(this, EventArgs.Empty);
        }
        catch (SwapFailedException ex)
        {
            ProgressText = "";
            SetStatus(ex.Title, Palette.Danger);
            LastFailure = (ex.Title, ex.Advice, ex.Details);
            FailureRaised?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ProgressText = "";
            SetStatus("处理失败", Palette.Danger);
            LastFailure = ("处理失败", ex.Message, ex.ToString());
            FailureRaised?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task RunPreviewAsync()
    {
        if (!CanStart || _videoInfo is null || _audioInfo is null) return;

        var previewPath = OutputNaming.BuildPreviewPath(_videoInfo.FileName);
        var offset = ParseOffsetSeconds();

        // 每次预览都用新文件名（见 BuildPreviewPath 的说明），所以先把上一次的删掉，
        // 保证临时目录里始终只有一个预览文件。删不掉（播放器占着）也不影响本次预览。
        OutputNaming.CleanupPreviewFiles();

        Busy = true;
        Progress = 0;
        ProgressText = "0%";
        SetStatus("正在生成预览（会重新编码，仅供检查对齐）…", Palette.Text);

        try
        {
            var runner = _inspector.CreateRunner();
            var progress = new Progress<SwapProgress>(p =>
            {
                Progress = p.Fraction;
                ProgressText = $"{p.Percent}%";
            });

            var result = await runner.PreviewAsync(
                _videoInfo.Path, _audioInfo.Path, previewPath, offset,
                CommandBuilder.EffectivePreviewSeconds(_videoInfo.DurationSeconds),
                progress, CancellationToken.None).ConfigureAwait(true);

            ProgressText = "";
            // 把本次实际用的偏移写进状态里：用户在弹窗里一眼就能看出
            // "程序以为的偏移"是多少，不必靠猜（预览对不上时也能立刻分清是参数没进去，
            // 还是播放器还在放上一次的预览）。
            SetStatus($"预览已生成（{OffsetNote(offset)}），已用系统播放器打开。", Palette.Success);
            LastPreviewPath = result;
            OpenWithShell(result);
            PreviewReady?.Invoke(this, result);
        }
        catch (SwapFailedException ex)
        {
            ProgressText = "";
            SetStatus(ex.Title, Palette.Danger);
            LastFailure = (ex.Title, ex.Advice, ex.Details);
            FailureRaised?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ProgressText = "";
            SetStatus("预览失败", Palette.Danger);
            LastFailure = ("预览失败", ex.Message, ex.ToString());
            FailureRaised?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Busy = false;
        }
    }

    public (string Title, string Advice, string Details)? LastFailure { get; private set; }
    public string? LastPreviewPath { get; private set; }

    public event EventHandler? FailureRaised;
    public event EventHandler? ResultReady;

    /// <summary>预览文件生成好了（参数是文件路径），供弹窗显示状态。</summary>
    public event EventHandler<string>? PreviewReady;

    // ────────────────────────── 收尾 ──────────────────────────

    /// <summary>清空重来。输出目录的手动选择保留（用户下次多半还想放那里）。</summary>
    public void Reset()
    {
        Video.Clear();
        Audio.Clear();
        _videoInfo = null;
        _audioInfo = null;
        HasResult = false;
        ResultPath = "";
        ResultDetail = "";
        Progress = 0;
        ProgressText = "";
        OffsetText = "0";
        AppliedOffsetSeconds = 0;
        SetStatus("", Palette.Text);
        RefreshOutputDirectory();
        Recompute();
    }

    public void SetStatus(string text, IBrush brush)
    {
        StatusText = text;
        StatusBrush = brush;
    }

    public static void OpenWithShell(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* 打开失败不该影响主流程 */ }
    }

    public static void RevealInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch { /* 忽略 */ }
    }
}
