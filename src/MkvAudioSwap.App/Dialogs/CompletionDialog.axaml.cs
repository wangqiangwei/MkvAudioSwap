using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MkvAudioSwap.App.Dialogs;

/// <summary>替换完成后的弹窗。放在弹窗里是为了让主界面始终保持"两个框 + 一个开始"。</summary>
public partial class CompletionDialog : Window
{
    private string _outputPath = "";

    public CompletionDialog() => AvaloniaXamlLoader.Load(this);

    /// <summary>用户是否选择了"再处理一个"。</summary>
    public bool WantsReset { get; private set; }

    public void Configure(string outputPath, string detail)
    {
        _outputPath = outputPath;
        this.FindControl<TextBlock>("PathText")!.Text = outputPath;
        this.FindControl<TextBlock>("DetailText")!.Text = detail;
    }

    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (_outputPath.Length > 0) ViewModels.MainViewModel.RevealInExplorer(_outputPath);
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
