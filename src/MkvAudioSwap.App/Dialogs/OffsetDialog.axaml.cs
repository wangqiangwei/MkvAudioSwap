using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MkvAudioSwap.App.Controls;
using MkvAudioSwap.Core;

namespace MkvAudioSwap.App.Dialogs;

/// <summary>
/// 音频偏移设置。做成独立弹窗而不是主界面的一行，是因为主界面要保持在
/// “两个框 + 一个开始”的极简形态，而偏移是低频、且需要解释说明的功能。
///
/// 预览必须使用输入框里的当前值（<see cref="ResultSeconds"/>），
/// 不能使用“上次确定的值”——顺序在 MainWindow.OnOpenOffset 里也有说明。
/// </summary>
public partial class OffsetDialog : Window
{
    private bool _ready;
    private double _videoSeconds;
    private double _audioSeconds;

    public OffsetDialog()
    {
        AvaloniaXamlLoader.Load(this);
        _ready = true;
    }

    /// <summary>用户确认后的偏移秒数。</summary>
    public double ResultSeconds { get; private set; }

    public bool Confirmed { get; private set; }

    public void Configure(double offsetSeconds, double videoSeconds = 0, double audioSeconds = 0)
    {
        _videoSeconds = videoSeconds;
        _audioSeconds = audioSeconds;
        ResultSeconds = offsetSeconds;

        var ms = Math.Round(offsetSeconds * 1000);
        this.FindControl<TextBox>("OffsetBox")!.Text = ms.ToString("0");

        var timeline = this.FindControl<DurationTimeline>("OffsetTimeline");
        if (timeline is not null)
        {
            timeline.VideoSeconds = videoSeconds;
            timeline.AudioSeconds = audioSeconds;
        }

        var hasDurations = videoSeconds > 0 && audioSeconds > 0;
        this.FindControl<Border>("TimelineCard")!.IsVisible = hasDurations;
        this.FindControl<TextBlock>("DurationContext")!.Text = hasDurations
            ? $"视频 {MediaFileInfo.FormatDuration(videoSeconds)} · 音频 {MediaFileInfo.FormatDuration(audioSeconds)}"
            : "";

        UpdateSecondsDisplay();
    }

    private double ParseBox()
    {
        var box = this.FindControl<TextBox>("OffsetBox");
        var raw = (box?.Text ?? "").Trim()
            .Replace("，", "").Replace(",", "").Replace("毫秒", "");

        if (raw.Length == 0) return 0;

        return double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>
    /// 输入毫秒时实时换算成秒显示，并同步移动方向示意里的音频条。
    /// 用户量到的偏差往往是“0.3 秒”这种量级，只给毫秒他要在脑子里除一次 1000。
    /// </summary>
    private void OnOffsetChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateSecondsDisplay();
    }

    private void UpdateSecondsDisplay()
    {
        var seconds = ParseBox() / 1000.0;

        var block = this.FindControl<TextBlock>("SecondsValue");
        if (block is not null)
        {
            block.Text = seconds.ToString("+0.###;-0.###;0");
            // 非零时用主色强调，0 时用次要灰，避免“0 秒”也显得很醒目
            block.Foreground = Math.Abs(seconds) < 0.0005 ? Palette.Faint : Palette.SuccessText;
        }

        var timeline = this.FindControl<DurationTimeline>("OffsetTimeline");
        if (timeline is not null) timeline.OffsetSeconds = seconds;
    }

    private void OnZero(object? sender, RoutedEventArgs e)
    {
        this.FindControl<TextBox>("OffsetBox")!.Text = "0";
        UpdateSecondsDisplay();
    }

    private void OnPreset(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: { } tag }) return;

        // 快捷按钮是“相对调整”，不是“设为”。
        // 例：当前 -300，点 +100 → -200；再点 +100 → -100。
        // 归零由独立的「归零」按钮负责，所以这里不再有 0 这个快捷值。
        if (!double.TryParse(tag.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var delta))
            return;

        var next = Math.Round(ParseBox() + delta);
        this.FindControl<TextBox>("OffsetBox")!.Text = next.ToString("0");
        UpdateSecondsDisplay();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        ResultSeconds = ParseBox() / 1000.0;
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnPreview(object? sender, RoutedEventArgs e)
    {
        // 预览由主窗口执行（它才有 ffmpeg 和文件信息）。
        // 这里把当前输入框的值交给它，保证“预览看到的就是确定的偏移”。
        ResultSeconds = ParseBox() / 1000.0;
        ShowPreviewStatus("正在生成预览…", isBusy: true);
        PreviewRequested?.Invoke(this, ResultSeconds);
    }

    public event EventHandler<double>? PreviewRequested;

    /// <summary>主窗口回调，用来在弹窗里显示预览进度/结果。</summary>
    public void ShowPreviewStatus(string text, bool isError = false, bool isBusy = false)
    {
        var block = this.FindControl<TextBlock>("PreviewStatus")!;
        block.Text = text;
        block.IsVisible = true;
        block.Foreground = isError ? Palette.Danger : Palette.Muted;

        var progress = this.FindControl<ProgressBar>("PreviewProgress");
        if (progress is not null) progress.IsVisible = isBusy;

        var button = this.FindControl<Button>("PreviewButton");
        if (button is not null) button.IsEnabled = !isBusy;
    }
}
