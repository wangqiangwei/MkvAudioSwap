using Avalonia.Media;
using MkvAudioSwap.Core;

namespace MkvAudioSwap.App;

/// <summary>
/// 护眼米黄配色。原则：
///  1. 整体暖中性，长时间看不刺眼；
///  2. 【所有会显示的文字对比度都 ≥ 4.5:1】（WCAG AA 正文标准）；
///  3. 只有“决定合成结果”的信息才允许上色，其余一律暖灰；
///  4. 颜色永远配符号（✓ / ⚠ / ⛔），色弱用户和截图传阅都能看懂。
///
/// 2026-10 复核修正：旧注释写“第三层 #7A7164 对页面底 4.9:1”是算错了，
/// 实测只有 4.26:1。现在第三层改为 #746B5E（对 #F5F1E8 为 4.65:1，
/// 对卡片面 #FFFCF6 为 5.12:1）。完整对照表见 docs/UI规范.md。
///
/// 对比度是相对页面底色 #F5F1E8 计算的，改色值请重新核对。
/// </summary>
public static class Palette
{
    // ── 背景层 ──
    /// <summary>页面底色：米黄。</summary>
    public static readonly Color PageColor = Color.Parse("#F5F1E8");
    /// <summary>卡片面：比页面底略亮一档，形成柔和的层次。</summary>
    public static readonly Color SurfaceColor = Color.Parse("#FBF8F2");
    /// <summary>抬起面：卡片、输入框等高光面。</summary>
    public static readonly Color SurfaceRaisedColor = Color.Parse("#FFFCF6");
    /// <summary>拖拽悬停高亮：比卡片面深一档的米色。</summary>
    public static readonly Color HoverColor = Color.Parse("#EFE9DA");
    /// <summary>下沉区域（偏移说明、设置分组）。</summary>
    public static readonly Color SunkenColor = Color.Parse("#F0EBE0");
    /// <summary>规格 chip 的底色。</summary>
    public static readonly Color ChipColor = Color.Parse("#F1ECE2");

    // ── 线与边 ──
    public static readonly Color BorderColor = Color.Parse("#E2DCCD");
    public static readonly Color BorderStrongColor = Color.Parse("#CFC6B2");
    public static readonly Color BorderSoftColor = Color.Parse("#E6DFD0");
    public static readonly Color DividerColor = Color.Parse("#ECE5D6");

    // ── 文字（这三档是唯一的正文色，都满足 AA）──
    /// <summary>第一层：正文与标题。对页面底约 12.8:1。</summary>
    public static readonly Color TextColor = Color.Parse("#2E2923");
    /// <summary>第二层：说明文字、标签。对页面底约 5.0:1。</summary>
    public static readonly Color MutedColor = Color.Parse("#6F6659");
    /// <summary>第三层：规格行、最次要的元信息。对页面底 4.65:1，对卡片面 5.12:1。</summary>
    public static readonly Color FaintColor = Color.Parse("#746B5E");
    /// <summary>等宽规格文字。对卡片面约 7.3:1。</summary>
    public static readonly Color SpecColor = Color.Parse("#5C5449");

    // ── 强调 ──
    /// <summary>主色：柔和墨绿。白字在其上对比度约 5.2:1。</summary>
    public static readonly Color AccentColor = Color.Parse("#4A7560");
    public static readonly Color AccentHoverColor = Color.Parse("#3C6250");
    public static readonly Color AccentPressColor = Color.Parse("#2F4A3D");
    public static readonly Color AccentSoftColor = Color.Parse("#E4EDE6");
    public static readonly Color AccentSoftBorderColor = Color.Parse("#CFE0D5");

    /// <summary>成功/一致。对页面底约 5.2:1，对强调浅底约 5.1:1。</summary>
    public static readonly Color SuccessColor = Color.Parse("#3F6B52");
    /// <summary>浅绿底上的深绿文字，对 #E4EDE6 约 6.6:1。</summary>
    public static readonly Color SuccessTextColor = Color.Parse("#2F5A43");
    /// <summary>提醒。对页面底约 5.0:1，对浅琥珀底约 5.0:1。</summary>
    public static readonly Color WarnColor = Color.Parse("#8A5F14");
    /// <summary>浅琥珀底上的深琥珀文字，对 #F7F1E4 约 6.0:1。</summary>
    public static readonly Color WarnTextColor = Color.Parse("#7A5512");
    public static readonly Color WarnSoftColor = Color.Parse("#F7F1E4");
    public static readonly Color WarnSoftBorderColor = Color.Parse("#E8DCC0");
    /// <summary>错误。对页面底约 5.4:1。</summary>
    public static readonly Color DangerColor = Color.Parse("#A8402C");
    /// <summary>浅红底上的深红文字，对 #F7E7E2 约 6.2:1。</summary>
    public static readonly Color DangerTextColor = Color.Parse("#943725");
    public static readonly Color DangerSoftColor = Color.Parse("#F7E7E2");
    public static readonly Color DangerSoftBorderColor = Color.Parse("#E0BDB3");

    // ── 状态胶囊 ──
    public static readonly Color PillReadyBackgroundColor = Color.Parse("#E4EDE6");
    public static readonly Color PillReadyBorderColor = Color.Parse("#CFE0D5");
    public static readonly Color PillReadyForegroundColor = Color.Parse("#2F5A43");
    public static readonly Color PillWarnBackgroundColor = Color.Parse("#F7F1E4");
    public static readonly Color PillWarnBorderColor = Color.Parse("#E8DCC0");
    public static readonly Color PillWarnForegroundColor = Color.Parse("#7A5512");
    public static readonly Color PillBlockedBackgroundColor = Color.Parse("#F7E7E2");
    public static readonly Color PillBlockedBorderColor = Color.Parse("#E0BDB3");
    public static readonly Color PillBlockedForegroundColor = Color.Parse("#943725");

    /// <summary>时长可视化里的轨道底色。</summary>
    public static readonly Color TrackColor = Color.Parse("#E4DFD2");
    /// <summary>时长可视化里的音频条。比视频条浅一档，两条并排时不会糊成一块。</summary>
    public static readonly Color TimelineAudioColor = Color.Parse("#7FA58F");

    // ── 画刷 ──
    public static readonly IBrush Page = new SolidColorBrush(PageColor);
    public static readonly IBrush Surface = new SolidColorBrush(SurfaceColor);
    public static readonly IBrush SurfaceRaised = new SolidColorBrush(SurfaceRaisedColor);
    public static readonly IBrush Hover = new SolidColorBrush(HoverColor);
    public static readonly IBrush Sunken = new SolidColorBrush(SunkenColor);
    public static readonly IBrush Chip = new SolidColorBrush(ChipColor);
    public static readonly IBrush Border = new SolidColorBrush(BorderColor);
    public static readonly IBrush BorderStrong = new SolidColorBrush(BorderStrongColor);
    public static readonly IBrush BorderSoft = new SolidColorBrush(BorderSoftColor);
    public static readonly IBrush Divider = new SolidColorBrush(DividerColor);
    public static readonly IBrush Text = new SolidColorBrush(TextColor);
    public static readonly IBrush Muted = new SolidColorBrush(MutedColor);
    public static readonly IBrush Faint = new SolidColorBrush(FaintColor);
    public static readonly IBrush Spec = new SolidColorBrush(SpecColor);
    public static readonly IBrush Accent = new SolidColorBrush(AccentColor);
    public static readonly IBrush AccentSoft = new SolidColorBrush(AccentSoftColor);
    public static readonly IBrush AccentSoftBorder = new SolidColorBrush(AccentSoftBorderColor);
    public static readonly IBrush Success = new SolidColorBrush(SuccessColor);
    public static readonly IBrush SuccessText = new SolidColorBrush(SuccessTextColor);
    public static readonly IBrush Warn = new SolidColorBrush(WarnColor);
    public static readonly IBrush WarnText = new SolidColorBrush(WarnTextColor);
    public static readonly IBrush WarnSoft = new SolidColorBrush(WarnSoftColor);
    public static readonly IBrush WarnSoftBorder = new SolidColorBrush(WarnSoftBorderColor);
    public static readonly IBrush Danger = new SolidColorBrush(DangerColor);
    public static readonly IBrush DangerText = new SolidColorBrush(DangerTextColor);
    public static readonly IBrush DangerSoft = new SolidColorBrush(DangerSoftColor);
    public static readonly IBrush DangerSoftBorder = new SolidColorBrush(DangerSoftBorderColor);
    public static readonly IBrush Track = new SolidColorBrush(TrackColor);
    public static readonly IBrush TimelineAudio = new SolidColorBrush(TimelineAudioColor);

    // ── 状态胶囊画刷 ──
    public static readonly IBrush PillReadyBackground = new SolidColorBrush(PillReadyBackgroundColor);
    public static readonly IBrush PillReadyBorder = new SolidColorBrush(PillReadyBorderColor);
    public static readonly IBrush PillReadyForeground = new SolidColorBrush(PillReadyForegroundColor);
    public static readonly IBrush PillWarnBackground = new SolidColorBrush(PillWarnBackgroundColor);
    public static readonly IBrush PillWarnBorder = new SolidColorBrush(PillWarnBorderColor);
    public static readonly IBrush PillWarnForeground = new SolidColorBrush(PillWarnForegroundColor);
    public static readonly IBrush PillBlockedBackground = new SolidColorBrush(PillBlockedBackgroundColor);
    public static readonly IBrush PillBlockedBorder = new SolidColorBrush(PillBlockedBorderColor);
    public static readonly IBrush PillBlockedForeground = new SolidColorBrush(PillBlockedForegroundColor);

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
}
