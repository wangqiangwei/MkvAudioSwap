using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MkvAudioSwap.App.Dialogs;
using MkvAudioSwap.App.ViewModels;

namespace MkvAudioSwap.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = _vm;

        _vm.FailureRaised += (_, _) => ShowFailureDialog();
        _vm.ResultReady += async (_, _) => await ShowCompletionDialog();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Opened += (_, _) => FadeIn();
        // 退出时删掉预览文件。预览是给用户"看一眼对齐"用的临时产物，
        // 不该跨会话留在磁盘上。启动时还会再清一次，兜住崩溃/强杀的情况。
        Closing += (_, _) =>
        {
            try { Core.OutputNaming.CleanupPreviewFiles(); } catch { /* 清理失败不影响退出 */ }
        };
    }

    /// <summary>启动时内容淡入。幅度很小，只用来让界面"活"一下。</summary>
    private void FadeIn()
    {
        var root = Content as Control;
        if (root is null) return;

        root.Opacity = 0;
        var anim = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(220),
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            FillMode = Avalonia.Animation.FillMode.Forward,
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1),
                    Setters = { new Avalonia.Styling.Setter(Visual.OpacityProperty, 1.0) },
                },
            },
        };

        _ = anim.RunAsync(root);
    }

    // ────────────────────────── 拖拽 ──────────────────────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = SafeGetFiles(e);
        e.DragEffects = files.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (files.Count == 0) return;

        // 明确指出会落到哪个卡片，而不是让用户猜
        var isVideo = files.Any(MainViewModel.IsVideoExtension);
        var isAudio = files.Any(MainViewModel.IsAudioExtension);

        SetDropping(VideoCard, isVideo);
        SetDropping(AudioCard, isVideo ? false : isAudio);
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        SetDropping(VideoCard, false);
        SetDropping(AudioCard, false);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropping(VideoCard, false);
        SetDropping(AudioCard, false);
        e.Handled = true;

        var files = SafeGetFiles(e);
        if (files.Count == 0) return;

        await _vm.LoadDroppedAsync(files);
    }

    private static List<string> SafeGetFiles(DragEventArgs e)
    {
        try
        {
            return (e.DataTransfer?.TryGetFiles() ?? [])
                .Select(i => i.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static void SetDropping(Border? card, bool on)
    {
        if (card is null) return;
        card.Classes.Set("dropping", on);
    }

    // ────────────────────────── 选择文件 ──────────────────────────

    private async void OnPickVideo(object? sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(
            "选择视频（源画面）",
            "视频文件", ["*.mp4", "*.mov", "*.mkv", "*.m4v", "*.webm", "*.avi", "*.ts", "*.flv", "*.wmv", "*.mpg", "*.mpeg", "*.m2ts"]);

        if (path is not null) await _vm.LoadVideoAsync(path);
    }

    private async void OnPickAudio(object? sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(
            "选择音频（你在 DAW 里导出的线性 PCM）",
            "线性 PCM 音频", ["*.caf", "*.wav", "*.aif", "*.aiff", "*.bwf", "*.rf64"]);

        if (path is not null) await _vm.LoadAudioAsync(path);
    }

    private async Task<string?> PickFileAsync(string title, string typeName, string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(typeName) { Patterns = patterns },
                new FilePickerFileType("所有文件") { Patterns = ["*.*"] },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    // ────────────────────────── 开始 ──────────────────────────

    private async void OnStart(object? sender, RoutedEventArgs e) => await _vm.RunSwapAsync();

    /// <summary>
    /// 打开提醒清单弹窗。刻意不做行内展开：展开会把卡片和窗口撑高、
    /// 把内容挤出窗口下边界（实测就是"点了详情什么都没出现"）。
    /// </summary>
    private async void OnOpenNotices(object? sender, RoutedEventArgs e)
    {
        var dialog = new NoticesDialog();
        dialog.Configure(_vm.WarningNotices);
        await dialog.ShowDialog(this);
    }

    private async Task ShowCompletionDialog()
    {
        if (!_vm.HasResult) return;

        var dialog = new CompletionDialog();
        dialog.Configure(_vm.ResultPath, _vm.ResultDetail);
        await dialog.ShowDialog(this);

        if (dialog.WantsReset) _vm.Reset();
    }

    // ────────────────────────── 设置 ──────────────────────────

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog();
        dialog.Configure(_vm.OutputDirectory, _vm.OutputDirectoryIsDefault);

        dialog.DirectoryChanged += (_, path) => _vm.SetOutputDirectory(path);
        dialog.DirectoryReset += (_, _) => _vm.ResetOutputDirectoryToDefault();

        await dialog.ShowDialog(this);
    }

    // ────────────────────────── 偏移 ──────────────────────────

    private async void OnOpenOffset(object? sender, RoutedEventArgs e)
    {
        var dialog = new OffsetDialog();
        dialog.Configure(_vm.ParseOffsetSeconds());

        // 用事件而不是轮询 LastPreviewPath：失败时 LastPreviewPath 可能还是上一次的旧值，
        // 那会让弹窗显示"预览已生成"这种假成功。
        var succeeded = false;
        void OnPreviewReady(object? _, string path)
        {
            succeeded = true;
            dialog.ShowPreviewStatus("预览已生成，已用系统播放器打开。\n这是临时文件，关闭本程序后会自动删除。");
        }

        _vm.PreviewReady += OnPreviewReady;

        dialog.PreviewRequested += async (_, seconds) =>
        {
            // 让预览用输入框里的当前值，而不是上次确定的值
            _vm.OffsetText = Math.Round(seconds * 1000).ToString("0");
            _vm.CommitOffset();

            succeeded = false;
            dialog.ShowPreviewStatus("正在生成预览…");
            await _vm.RunPreviewAsync();

            if (!succeeded)
                dialog.ShowPreviewStatus(
                    _vm.StatusText.Length > 0 ? _vm.StatusText : "预览失败", isError: true);
        };

        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            _vm.PreviewReady -= OnPreviewReady;
        }

        if (dialog.Confirmed)
        {
            _vm.OffsetText = Math.Round(dialog.ResultSeconds * 1000).ToString("0");
            _vm.CommitOffset();
            _vm.Recompute();
        }
    }

    // ────────────────────────── 失败对话框 ──────────────────────────

    private async void ShowFailureDialog()
    {
        if (_vm.LastFailure is not { } f) return;

        var panel = new StackPanel { Spacing = 11, Margin = new Avalonia.Thickness(24, 22, 24, 20) };

        panel.Children.Add(new TextBlock
        {
            Text = f.Title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 15,
            Foreground = Palette.Danger,
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = f.Advice,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Palette.Text,
            LineHeight = 20,
        });

        var details = new TextBox
        {
            Text = f.Details,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 150,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
            FontSize = 11,
            Background = Palette.Surface,
            BorderBrush = Palette.Border,
            CornerRadius = new Avalonia.CornerRadius(8),
        };

        var copyButton = new Button
        {
            Content = "复制详情",
            Padding = new Avalonia.Thickness(14, 8),
            CornerRadius = new Avalonia.CornerRadius(8),
            BorderThickness = new Avalonia.Thickness(1),
            BorderBrush = Palette.Border,
            Background = Brushes.Transparent,
            Foreground = Palette.Text,
        };

        var closeButton = new Button
        {
            Content = "关闭",
            Padding = new Avalonia.Thickness(18, 8),
            CornerRadius = new Avalonia.CornerRadius(8),
            Background = Palette.Accent,
            Foreground = Brushes.White,
        };

        Window? dialog = null;

        copyButton.Click += async (_, _) =>
        {
            try
            {
                var clipboard = GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                {
                    await clipboard.SetTextAsync(f.Details);
                    copyButton.Content = "已复制";
                }
            }
            catch
            {
                copyButton.Content = "复制失败";
            }
        };

        closeButton.Click += (_, _) => dialog?.Close();

        panel.Children.Add(details);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { copyButton, closeButton },
        });

        dialog = new Window
        {
            Title = "处理失败",
            Width = 600,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 520,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.Page,
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif"),
            Content = panel,
        };

        await dialog.ShowDialog(this);
    }
}
