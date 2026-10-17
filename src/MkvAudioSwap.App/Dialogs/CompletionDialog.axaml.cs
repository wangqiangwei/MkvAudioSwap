using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace MkvAudioSwap.App.Dialogs;

/// <summary>替换完成后的弹窗。放在弹窗里是为了让主界面始终保持“两个框 + 一个开始”。</summary>
public partial class CompletionDialog : Window
{
    private string _outputPath = "";

    public CompletionDialog()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) => AnimateBadge();
    }

    /// <summary>用户是否选择了“再处理一个”。</summary>
    public bool WantsReset { get; private set; }

    public void Configure(string outputPath, string detail)
    {
        _outputPath = outputPath;
        this.FindControl<SelectableTextBlock>("PathText")!.Text = outputPath;
        this.FindControl<TextBlock>("DetailText")!.Text = detail;
    }

    /// <summary>成功徽章淡入。幅度很小，只给“完成”一个落点。</summary>
    private void AnimateBadge()
    {
        var badge = this.FindControl<Border>("SuccessBadge");
        if (badge is null) return;

        badge.Opacity = 0;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(260),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(Visual.OpacityProperty, 1.0) },
                },
            },
        };

        _ = animation.RunAsync(badge);
    }

    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (_outputPath.Length > 0) ViewModels.MainViewModel.RevealInExplorer(_outputPath);
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var button = this.FindControl<Button>("CopyPathButton")!;

        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;

            await clipboard.SetTextAsync(_outputPath);
            button.Content = "已复制";
        }
        catch
        {
            button.Content = "复制失败";
        }
    }

    private void OnPlay(object? sender, RoutedEventArgs e)
    {
        if (_outputPath.Length > 0) ViewModels.MainViewModel.OpenWithShell(_outputPath);
    }

    private void OnAgain(object? sender, RoutedEventArgs e)
    {
        WantsReset = true;
        Close();
    }
}
