using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MkvAudioSwap.App.Dialogs;

/// <summary>
/// 失败弹窗。用户反馈问题时唯一可靠的信息源是“命令行 + ffmpeg 输出”，
/// 所以详情必须能一键复制，且不能被截断成一句抽象错误。
/// </summary>
public partial class FailureDialog : Window
{
    private string _details = "";

    public FailureDialog() => AvaloniaXamlLoader.Load(this);

    public void Configure(string title, string advice, string details)
    {
        _details = details;

        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("AdviceText")!.Text = advice;
        this.FindControl<TextBox>("DetailsBox")!.Text = details;
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var button = this.FindControl<Button>("CopyDetailsButton")!;

        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;

            await clipboard.SetTextAsync(_details);
            button.Content = "已复制";
        }
        catch
        {
            button.Content = "复制失败";
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
