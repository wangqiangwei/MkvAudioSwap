using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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

        // 明确指出会落到哪个卡片，而不是让用户猜。
        // 视频和音频同时拖入时两张卡都亮 —— 之前只亮视频卡，看起来像音频被忽略了。
        var isVideo = files.Any(MainViewModel.IsVideoExtension);
        var isAudio = files.Any(MainViewModel.IsAudioExtension);

        SetDropping(VideoCard, isVideo);
        SetDropping(AudioCard, isAudio);
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
            "视频文件", MainViewModel.VideoPickerPatterns);

        if (path is not null) await _vm.LoadVideoAsync(path);
    }

    private async void OnPickAudio(object? sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(
            "选择音频（你在 DAW 里导出的音频）",
            "音频文件", MainViewModel.AudioPickerPatterns);

        if (path is not null) await _vm.LoadAudioAsync(path);
    }

    private async Task<string?> PickFileAsync(string title, string typeName, string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await PickerStartFolderAsync(),
            FileTypeFilter =
            [
                new FilePickerFileType(typeName) { Patterns = patterns },
                new FilePickerFileType("所有文件") { Patterns = ["*.*"] },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// 选择文件对话框的起始目录 = **程序自己所在的目录**（绿色目录，用户双击 exe 的地方）。
    ///
    /// 为什么必须显式给：不给的话 Windows 会沿用"上次访问过的目录"。
    /// 实测会停在上一回导入素材的位置（开发机上是仓库里的 测试素材\），
    /// 和用户这次要处理的文件毫无关系 —— 看起来就像程序记错了地方，
    /// 而用户没有任何办法把它掰回来（那是 shell 自己记的，不是本程序的设置）。
    ///
    /// 起点固定在程序目录至少是**可预期**的：用户双击的 exe 就在那里，
    /// 从那儿往旁边/往上找自己的素材是很自然的动作。
    /// </summary>
    private async Task<IStorageFolder?> PickerStartFolderAsync()
    {
        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(AppContext.BaseDirectory);
        }
        catch
        {
            // 取不到起点目录就让系统自己决定 —— 绝不能因为这点小事让"选择文件"整个点不动
            return null;
        }
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
        dialog.Configure(_vm.ParseOffsetSeconds(), _vm.VideoTimelineSeconds, _vm.AudioTimelineSeconds);

        // 用事件而不是轮询 LastPreviewPath：失败时 LastPreviewPath 可能还是上一次的旧值，
        // 那会让弹窗显示"预览已生成"这种假成功。
        var succeeded = false;
        void OnPreviewReady(object? _, string path)
        {
            succeeded = true;
            // 把本次的偏移写出来：预览对不上时，用户（和排查的人）第一眼就能分清
            // 是"偏移没进到参数里"还是"看到的还是上一次的预览"。
            dialog.ShowPreviewStatus(
                $"预览已生成（{MainViewModel.OffsetNote(_vm.AppliedOffsetSeconds)}），已用系统播放器打开。\n" +
                "这是临时文件，关闭本程序后会自动删除。");
        }

        _vm.PreviewReady += OnPreviewReady;

        dialog.PreviewRequested += async (_, seconds) =>
        {
            // 让预览用输入框里的当前值，而不是上次确定的值
            _vm.OffsetText = Math.Round(seconds * 1000).ToString("0");
            _vm.CommitOffset();

            succeeded = false;
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

        var dialog = new FailureDialog();
        dialog.Configure(f.Title, f.Advice, f.Details);
        await dialog.ShowDialog(this);
    }
}
