using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MkvAudioSwap.App.Services;

namespace MkvAudioSwap.App.Dialogs;

/// <summary>设置弹窗：保存位置、运行环境检查、关于。全部从主界面移到这里。</summary>
public partial class SettingsDialog : Window
{
    private readonly MediaInspector _inspector = new();

    public SettingsDialog() => AvaloniaXamlLoader.Load(this);

    /// <summary>用户选了新的保存目录（绝对路径）。</summary>
    public event EventHandler<string>? DirectoryChanged;

    /// <summary>用户要求恢复默认保存位置。</summary>
    public event EventHandler? DirectoryReset;

    public void Configure(string directory, bool isDefault)
    {
        this.FindControl<TextBlock>("DirText")!.Text = directory;
        this.FindControl<Button>("ResetDirButton")!.IsVisible = !isDefault;
    }

    private async void OnChangeDir(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择输出位置",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        this.FindControl<TextBlock>("DirText")!.Text = path;
        this.FindControl<Button>("ResetDirButton")!.IsVisible = true;
        DirectoryChanged?.Invoke(this, path);
    }

    private void OnResetDir(object? sender, RoutedEventArgs e)
    {
        this.FindControl<Button>("ResetDirButton")!.IsVisible = false;
        DirectoryReset?.Invoke(this, EventArgs.Empty);
    }

    private async void OnCheck(object? sender, RoutedEventArgs e)
    {
        var envText = this.FindControl<TextBlock>("EnvText")!;
        var button = this.FindControl<Button>("CheckButton")!;

        button.IsEnabled = false;
        envText.Foreground = Palette.Muted;
        envText.Text = "正在检查…";

        try
        {
            var results = await _inspector.SelfCheckAsync();
            var lines = results.Select(r =>
                (r.Ok ? "✓ " : "⛔ ") + r.Label + "：" + r.Detail);

            envText.Text = string.Join("\n", lines);

            if (results.All(r => r.Ok))
            {
                envText.Foreground = Palette.Muted;
            }
            else
            {
                envText.Foreground = Palette.Danger;
                envText.Text += "\n\n未找到：请重新解压，确保 bin 文件夹和 exe 在同一层。\n" +
                                "无法运行：多半被杀毒软件拦截，请把程序目录加入白名单后重试。";
            }
        }
        catch (Exception ex)
        {
            envText.Foreground = Palette.Danger;
            envText.Text = "检查失败：" + ex.Message;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
