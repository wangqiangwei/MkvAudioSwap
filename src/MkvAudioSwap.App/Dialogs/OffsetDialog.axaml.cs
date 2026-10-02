using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MkvAudioSwap.App.Dialogs;

/// <summary>
/// 音频偏移设置。做成独立弹窗而不是主界面的一行，是因为主界面要保持在
/// "两个框 + 一个开始"的极简形态，而偏移是低频、且需要解释说明的功能。
/// </summary>
public partial class OffsetDialog : Window
{
    private bool _ready;

    public OffsetDialog()
    {
        AvaloniaXamlLoader.Load(this);
        _ready = true;
    }

    /// <summary>用户确认后的偏移秒数。</summary>
    public double ResultSeconds { get; private set; }

    public bool Confirmed { get; private set; }

    public void Configure(double offsetSeconds)
    {
        ResultSeconds = offsetSeconds;

        var ms = Math.Round(offsetSeconds * 1000);
        this.FindControl<TextBox>("OffsetBox")!.Text = ms.ToString("0");
        UpdateSecondsDisplay();
    }

    private double ParseBox()
    {
        var raw = (this.FindControl<TextBox>("OffsetBox")!.Text ?? "").Trim()
            .Replace("，", "").Replace(",", "").Replace("毫秒", "");

        if (raw.Length == 0) return 0;

        return double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>
    /// 输入毫秒时实时换算成秒显示。
    /// 用户量到的偏差往往是"0.3 秒"这种量级，只给毫秒他要在脑子里除一次 1000。
    /// </summary>
    private void OnOffsetChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateSecondsDisplay();
    }

    private void UpdateSecondsDisplay()
    {
        var block = this.FindControl<TextBlock>("SecondsValue");
        if (block is null) return;

        var seconds = ParseBox() / 1000.0;
        block.Text = seconds.ToString("+0.###;-0.###;0");

        // 非零时用主色强调，0 时用次要灰，避免"0 秒"也显得很醒目
        block.Foreground = Math.Abs(seconds) < 0.0005 ? Palette.Faint : Palette.Accent;
    }

    private void OnZero(object? sender, RoutedEventArgs e)
    {
        this.FindControl<TextBox>("OffsetBox")!.Text = "0";
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
        // 这里把当前输入框的值交给它，保证"预览看到的就是确定的偏移"。
        ResultSeconds = ParseBox() / 1000.0;
        PreviewRequested?.Invoke(this, ResultSeconds);
    }

    public event EventHandler<double>? PreviewRequested;

    /// <summary>主窗口回调，用来在弹窗里显示预览进度/结果。</summary>
    public void ShowPreviewStatus(string text, bool isError = false)
    {
        var block = this.FindControl<TextBlock>("PreviewStatus")!;
        block.Text = text;
        block.IsVisible = true;
        block.Foreground = isError ? Palette.Danger : Palette.Muted;
    }
}
