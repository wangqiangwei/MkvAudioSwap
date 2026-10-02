using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MkvAudioSwap.App.ViewModels;

namespace MkvAudioSwap.App.Dialogs;

/// <summary>
/// 提醒清单。做成独立窗口而不是主界面的行内展开区，理由有两条：
///   1. 行内展开会把卡片和窗口撑高、把布局挤出窗口边界 —— 实测表现为
///      "点了详情什么都没出现"，也就是用户报的那个 bug；
///   2. 提醒来自四个地方（视频文件、音频文件、两者之间、体积），
///      集中成一份带来源标注的清单，比散在两个卡片里更容易看清全貌。
/// </summary>
public partial class NoticesDialog : Window
{
    public NoticesDialog() => AvaloniaXamlLoader.Load(this);

    public void Configure(IReadOnlyList<NoteViewModel> notices)
    {
        var cautions = notices.Where(n => n.IsCaution).ToList();
        var infos = notices.Where(n => n.IsInfo).ToList();

        // 警告在前、说明在后：用户打开这个窗口是为了看"有什么要注意的"
        var ordered = cautions.Concat(infos).ToList();

        this.FindControl<TextBlock>("TitleText")!.Text =
            cautions.Count > 0 ? $"需要注意 {cautions.Count} 处" : $"{infos.Count} 条说明";

        this.FindControl<TextBlock>("SubtitleText")!.Text = cautions.Count > 0
            ? "下面这些不影响操作，按钮照常可用。请自己判断是否要处理。"
            : "这些只是补充说明，没有需要处理的问题。";

        this.FindControl<TextBlock>("FooterText")!.Text =
            "程序只比对时长与规格，不判断内容对错 —— 最终以你的耳朵为准。";

        var list = this.FindControl<ItemsControl>("NoticeList")!;
        list.ItemsSource = ordered;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
