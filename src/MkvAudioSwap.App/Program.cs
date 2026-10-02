using System.Text;
using Avalonia;

namespace MkvAudioSwap.App;

internal static class Program
{
    /// <summary>崩溃日志放在 exe 旁边，用户能直接找到并发给你。</summary>
    private static string LogPath => Path.Combine(AppContext.BaseDirectory, "错误日志.txt");

    [STAThread]
    public static void Main(string[] args)
    {
        // 启动时清掉上次运行遗留的预览文件。
        // 预览是临时文件，不该永久留在磁盘上；此时也不可能有播放器正读着它。
        try { MkvAudioSwap.Core.OutputNaming.CleanupPreviewFiles(); } catch { /* 不影响启动 */ }

        // 兜底：任何未捕获异常都要留下痕迹。
        // 桌面程序崩溃时窗口会直接消失，用户只会说"点了没反应"，
        // 没有日志就完全无从查起。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteLog("未处理的异常", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteLog("后台任务异常", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteLog("程序启动失败", ex);
            TryShowFatalMessage(ex);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteLog(string title, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("================ 替音工具 错误报告 ================");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("类型：" + title);
            sb.AppendLine("系统：" + Environment.OSVersion.VersionString);
            sb.AppendLine(".NET：" + Environment.Version);
            sb.AppendLine("程序目录：" + AppContext.BaseDirectory);
            sb.AppendLine();
            sb.AppendLine(ex?.ToString() ?? "(没有异常对象)");
            sb.AppendLine();

            File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // 连日志都写不了就只能放弃，至少不能让日志本身引发二次崩溃
        }
    }

    /// <summary>
    /// 崩溃时用最原始的方式弹一个消息框（不依赖 Avalonia，因为可能正是它挂了）。
    /// </summary>
    private static void TryShowFatalMessage(Exception ex)
    {
        var text = "替音工具启动失败。\n\n" +
                   ex.Message + "\n\n" +
                   "详细信息已写入：\n" + LogPath;

        try
        {
            MessageBoxW(IntPtr.Zero, text, "替音工具", 0x00000010 /* MB_ICONERROR */);
        }
        catch
        {
            // 连消息框都弹不出来就算了，日志已经留下
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
