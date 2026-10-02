using System.Collections.ObjectModel;
using MkvAudioSwap.Core;

namespace MkvAudioSwap.App;

/// <summary>卡片里的一条备注。颜色和符号一起给，色弱用户也能分辨。</summary>
public sealed class NoteViewModel
{
    /// <param name="source">
    /// 这条提醒来自哪个文件。汇总成一份清单后必须标出来源，
    /// 否则用户分不清"采样率不同"说的是视频音轨还是他导出的音频。
    /// 单张卡片内部显示时传空字符串即可（上下文已经说明了来源）。
    /// </param>
    public NoteViewModel(Note note, string source = "")
    {
        Text = note.Text;
        Level = note.Level;
        Source = source;
        Brush = Palette.ForLevel(note.Level);
        Symbol = note.Level switch
        {
            NoteLevel.Caution => "⚠",
            NoteLevel.Danger => "⛔",
            _ => "·",
        };
        Display = note.Level == NoteLevel.Info ? Text : Symbol + " " + Text;
    }

    public string Text { get; }
    public NoteLevel Level { get; }
    public string Source { get; }
    public bool HasSource => Source.Length > 0;
    public string Symbol { get; }
    public string Display { get; }
    public Avalonia.Media.IBrush Brush { get; }

    public bool IsCaution => Level is NoteLevel.Caution or NoteLevel.Danger;
    public bool IsInfo => Level == NoteLevel.Info;
}

/// <summary>
/// 一个文件槽位。只描述"这个文件是什么"，不判断用户该不该继续 ——
/// 唯一能让按钮变灰的是 Blocked（文件里没有视频流 / 没有 PCM 流）。
/// </summary>
public sealed class SlotViewModel : ObservableObject
{
    private string _fileName = "";
    private string _specLine = "";
    private string _secondaryLine = "";
    private bool _hasFile;
    private SlotStatus _status = SlotStatus.Empty;
    private string? _blockReason;

    public SlotViewModel(string title, string emptyTitle, string emptyHint, string pickerTitle)
    {
        Title = title;
        EmptyTitle = emptyTitle;
        EmptyHint = emptyHint;
        PickerTitle = pickerTitle;
    }

    public string Title { get; }

    /// <summary>空状态里的主提示，例如"把视频拖到这里"。</summary>
    public string EmptyTitle { get; }

    /// <summary>空状态里的副提示，列出支持的格式。</summary>
    public string EmptyHint { get; }

    public string PickerTitle { get; }

    /// <summary>卡片内的备注区。第一行规格，第二行补充信息，之后是提醒。</summary>
    public ObservableCollection<NoteViewModel> Notes { get; } = new();

    public string FileName { get => _fileName; set => Set(ref _fileName, value); }

    /// <summary>规格行：等宽字体显示，用户核对 DAW 导出设置就看这一行。</summary>
    public string SpecLine { get => _specLine; set => Set(ref _specLine, value); }

    /// <summary>补充行：源音轨信息（视频）或多音轨提示（音频）。</summary>
    public string SecondaryLine { get => _secondaryLine; set => Set(ref _secondaryLine, value); }



    /// <summary>
    /// 卡片底部那一行"说明/提醒"的入口文案。提醒内容本身在弹窗里显示，
    /// 不在卡片内展开 —— 展开会把卡片撑高，进而把整个窗口的布局挤变形。
    /// </summary>
    public string NoticeText { get; private set; } = "";
    public bool HasNotices => Notes.Count > 0;
    public bool HasCautionNotices { get; private set; }
    public Avalonia.Media.IBrush NoticeBrush =>
        HasCautionNotices ? Palette.Warn : Palette.Faint;

    /// <summary>备注列表变化后刷新上面的派生属性。</summary>
    public void RefreshNoticeSummary()
    {
        HasCautionNotices = Notes.Any(n => n.IsCaution);
        NoticeText = !HasNotices
            ? ""
            : HasCautionNotices
                ? $"⚠ {Notes.Count} 条需要注意 · 查看"
                : $"{Notes.Count} 条说明 · 查看";

        Raise(nameof(NoticeText));
        Raise(nameof(HasNotices));
        Raise(nameof(HasCautionNotices));
        Raise(nameof(NoticeBrush));
    }

    public bool HasFile
    {
        get => _hasFile;
        set { if (Set(ref _hasFile, value)) { Raise(nameof(IsEmpty)); Raise(nameof(PickButtonText)); } }
    }

    public bool IsEmpty => !HasFile;

    /// <summary>有文件之后按钮文案变成"更换"，避免用户以为要再选一次。</summary>
    public string PickButtonText => HasFile ? "更换" : (Title == "视频" ? "选择视频" : "选择音频");

    public SlotStatus Status
    {
        get => _status;
        set { if (Set(ref _status, value)) Raise(nameof(HasBlockReason)); }
    }

    public string? BlockReason
    {
        get => _blockReason;
        set { if (Set(ref _blockReason, value)) Raise(nameof(HasBlockReason)); }
    }

    public bool HasBlockReason => Status == SlotStatus.Blocked && !string.IsNullOrEmpty(BlockReason);

    public void Clear()
    {
        FileName = "";
        SpecLine = "";
        SecondaryLine = "";
        BlockReason = null;
        Status = SlotStatus.Empty;
        Notes.Clear();
        HasFile = false;
        RefreshNoticeSummary();
    }
}
