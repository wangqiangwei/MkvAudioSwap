using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MkvAudioSwap.App.Services;

namespace MkvAudioSwap.App.Dialogs;

/// <summary>环境检查的一行。用 record 让 ItemsControl 直接绑定，避免在 code-behind 里拼控件树。</summary>
public sealed record EnvCheckRow(bool Ok, string Label, string Detail)
{
    public bool IsFail => !Ok;
}

/// <summary>设置弹窗：保存位置、运行环境检查、关于。全部从主界面移到这里。</summary>
public partial class SettingsDialog : Window
{
    private readonly MediaInspector _inspector = new();
    private string _currentDirectory = "";

    public SettingsDialog() => AvaloniaXamlLoader.Load(this);

    /// <summary>用户选了新的保存目录（绝对路径）。</summary>
    public event EventHandler<string>? DirectoryChanged;

    /// <summary>用户要求恢复默认保存位置。</summary>
    public event EventHandler? DirectoryReset;

    public void Configure(string directory, bool isDefault)
    {
        _currentDirectory = directory;
        this.FindControl<TextBlock>("DirText")!.Text = directory;
        this.FindControl<Button>("ResetDirButton")!.IsVisible = !isDefault;
    }

    private async void OnChangeDir(object? sender, RoutedEventArgs e)
    {
        // 输出位置的选择框从“当前保存目录”开始 —— 这是用户唯一会想接着上次继续选的目录。
        // 注意：视频/音频选择框仍然固定从程序目录开始（README §18），两者不要混。
        IStorageFolder? start = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(_currentDirectory) && Directory.Exists(_currentDirectory))
                start = await StorageProvider.TryGetFolderFromPathAsync(_currentDirectory);
        }
        catch
        {
            // 取不到起点就交给系统，绝不让“更改目录”点不动
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择输出位置",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        _currentDirectory = path;
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
        var button = this.FindControl<Button>("CheckButton")!;
        var progress = this.FindControl<ProgressBar>("CheckProgress")!;
        var list = this.FindControl<ItemsControl>("EnvList")!;
        var hint = this.FindControl<TextBlock>("EnvHintText")!;

        button.IsEnabled = false;
        progress.IsVisible = true;
        list.ItemsSource = null;
        hint.IsVisible = false;

        try
        {
            var results = await _inspector.SelfCheckAsync();
            var rows = results
                .Select(r => new EnvCheckRow(r.Ok, r.Label, r.Detail))
                .ToList();

            list.ItemsSource = rows;

            if (rows.All(r => r.Ok))
            {
                hint.IsVisible = false;
            }
            else
            {
                hint.Text = "未找到：请重新解压，确保 bin 文件夹和 exe 在同一层。\n" +
                            "无法运行：多半被杀毒软件拦截，请把程序目录加入白名单后重试。";
                hint.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            hint.Text = "检查失败：" + ex.Message;
            hint.IsVisible = true;
        }
        finally
        {
            progress.IsVisible = false;
            button.IsEnabled = true;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
