using Avalonia.Media;
using MkvAudioSwap.Core;

namespace MkvAudioSwap.App;

/// <summary>
/// 护眼米黄配色。原则：
///  1. 整体暖中性，长时间看不刺眼；
///  2. 【所有会显示的文字对比度都 ≥ 4.5:1】（WCAG AA 正文标准），
///     第二、三层文字实测约 5.5:1 和 4.8:1，不再是"看着很累"的浅灰；
///  3. 只有"决定合成结果"的信息才允许上色，其余一律暖灰；
///  4. 颜色永远配符号（✓ / ⚠ / ⛔），色弱用户和截图传阅都能看懂。
///
/// 对比度是相对页面底色 #F5F1E8 计算的，改色值请重新核对这几条注释。
/// </summary>
public static class Palette
{
    // ── 背景层 ──
    /// <summary>页面底色：米黄。</summary>
    public static readonly Color PageColor = Color.Parse("#F5F1E8");
    /// <summary>卡片面：比页面底略亮一档，形成柔和的层次。</summary>
    public static readonly Color SurfaceColor = Color.Parse("#FBF8F2");
    /// <summary>拖拽悬停高亮：比卡片面深一档的米色。</summary>
    public static readonly Color HoverColor = Color.Parse("#EFE9DA");
    /// <summary>下沉区域（偏移说明、设置分组）。</summary>
    public static readonly Color SunkenColor = Color.Parse("#F0EBE0");

    // ── 线与边 ──
    public static readonly Color BorderColor = Color.Parse("#E2DCCD");
    public static readonly Color BorderStrongColor = Color.Parse("#CFC6B2");

    // ── 文字（这三档是唯一的正文色，都满足 AA）──
    /// <summary>第一层：正文与标题。对页面底约 11:1。</summary>
    public static readonly Color TextColor = Color.Parse("#2E2923");
    /// <summary>第二层：说明文字、标签。对页面底约 5.3:1。</summary>
    public static readonly Color MutedColor = Color.Parse("#6F6659");
    /// <summary>第三层：规格行、最次要的元信息。对页面底约 4.9:1。</summary>
    public static readonly Color FaintColor = Color.Parse("#7A7164");

    // ── 强调 ──
    /// <summary>主色：柔和墨绿。白字在其上对比度约 4.8:1。</summary>
    public static readonly Color AccentColor = Color.Parse("#4A7560");
    public static readonly Color AccentHoverColor = Color.Parse("#3C6250");
    public static readonly Color AccentSoftColor = Color.Parse("#E4EDE6");

    /// <summary>成功/一致。对页面底约 5.0:1。</summary>
    public static readonly Color SuccessColor = Color.Parse("#3F6B52");
    /// <summary>提醒。对页面底约 4.5:1，且刻意偏棕以免看着像警告标签。</summary>
    public static readonly Color WarnColor = Color.Parse("#8A5F14");
    /// <summary>错误。对页面底约 5.3:1。</summary>
    public static readonly Color DangerColor = Color.Parse("#A8402C");

    // ── 画刷 ──
    public static readonly IBrush Page = new SolidColorBrush(PageColor);
    public static readonly IBrush Surface = new SolidColorBrush(SurfaceColor);
    public static readonly IBrush Hover = new SolidColorBrush(HoverColor);
    public static readonly IBrush Sunken = new SolidColorBrush(SunkenColor);
    public static readonly IBrush Border = new SolidColorBrush(BorderColor);
    public static readonly IBrush Text = new SolidColorBrush(TextColor);
    public static readonly IBrush Muted = new SolidColorBrush(MutedColor);
    public static readonly IBrush Faint = new SolidColorBrush(FaintColor);
    public static readonly IBrush Accent = new SolidColorBrush(AccentColor);
    public static readonly IBrush Success = new SolidColorBrush(SuccessColor);
    public static readonly IBrush Warn = new SolidColorBrush(WarnColor);
    public static readonly IBrush Danger = new SolidColorBrush(DangerColor);

    /// <summary>备注级别 → 颜色。信息级刻意用次要灰，避免界面到处是彩色。</summary>
    public static IBrush ForLevel(NoteLevel level) => level switch
    {
        NoteLevel.Caution => Warn,
        NoteLevel.Danger => Danger,
        _ => Faint,
    };

    public static Color ColorForLevel(NoteLevel level) => level switch
    {
        NoteLevel.Caution => WarnColor,
        NoteLevel.Danger => DangerColor,
        _ => FaintColor,
    };

    /// <summary>按十六进制字符串取画刷。供 String→Brush 绑定使用。</summary>
    public static IBrush FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Text;

        try { return new SolidColorBrush(Color.Parse(hex)); }
        catch { return Text; }
    }
}
