using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MkvAudioSwap.App.Controls;

/// <summary>
/// 双轨时长可视化：上轨是视频，下轨是音频。
///
/// 它只回答一个问题：“这两段素材在时间轴上是怎么对上的？”
///  - 两条等长 → 两条轨道右端对齐；
///  - 音频短 → 下轨提前结束；
///  - 设了偏移 → 下轨整体左/右移动。
///
/// 刻意不用 Canvas + 绑定宽度：那种写法要假定一个像素宽度，
/// 窗口一缩放就会错位。这里在 Render 里按实际 Bounds 计算比例。
/// </summary>
public sealed class DurationTimeline : Control
{
    public static readonly StyledProperty<double> VideoSecondsProperty =
        AvaloniaProperty.Register<DurationTimeline, double>(nameof(VideoSeconds));

    public static readonly StyledProperty<double> AudioSecondsProperty =
        AvaloniaProperty.Register<DurationTimeline, double>(nameof(AudioSeconds));

    public static readonly StyledProperty<double> OffsetSecondsProperty =
        AvaloniaProperty.Register<DurationTimeline, double>(nameof(OffsetSeconds));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<DurationTimeline, IBrush?>(
            nameof(TrackBrush), new SolidColorBrush(Color.Parse("#E4DFD2")));

    public static readonly StyledProperty<IBrush?> VideoBrushProperty =
        AvaloniaProperty.Register<DurationTimeline, IBrush?>(
            nameof(VideoBrush), Palette.Accent);

    public static readonly StyledProperty<IBrush?> AudioBrushProperty =
        AvaloniaProperty.Register<DurationTimeline, IBrush?>(
            nameof(AudioBrush), Palette.Warn);

    static DurationTimeline()
    {
        AffectsRender<DurationTimeline>(
            VideoSecondsProperty,
            AudioSecondsProperty,
            OffsetSecondsProperty,
            TrackBrushProperty,
            VideoBrushProperty,
            AudioBrushProperty);
    }

    public double VideoSeconds
    {
        get => GetValue(VideoSecondsProperty);
        set => SetValue(VideoSecondsProperty, value);
    }

    public double AudioSeconds
    {
        get => GetValue(AudioSecondsProperty);
        set => SetValue(AudioSecondsProperty, value);
    }

    public double OffsetSeconds
    {
        get => GetValue(OffsetSecondsProperty);
        set => SetValue(OffsetSecondsProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? VideoBrush
    {
        get => GetValue(VideoBrushProperty);
        set => SetValue(VideoBrushProperty, value);
    }

    public IBrush? AudioBrush
    {
        get => GetValue(AudioBrushProperty);
        set => SetValue(AudioBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var lane = Math.Clamp(height / 3.2, 4.0, 7.0);
        var gap = Math.Max(2.0, height - lane * 2);
        var top = new Rect(0, 0, width, lane);
        var bottom = new Rect(0, lane + gap, width, lane);
        var radius = lane / 2;

        // 两条空轨道：即使还没选文件，也先把“时间轴”这件事画出来。
        if (TrackBrush is { } track)
        {
            context.DrawRectangle(track, null, top, radius, radius);
            context.DrawRectangle(track, null, bottom, radius, radius);
        }

        // 只有两边时长都有效时才画“对齐关系”。
        // 缺一边时画一根孤零零的音频条，会让人误以为已经对上了。
        if (VideoSeconds <= 0 || AudioSeconds <= 0) return;

        var offset = OffsetSeconds;
        var min = Math.Min(0, offset);
        var max = Math.Max(VideoSeconds, offset + AudioSeconds);
        if (max - min < 0.0001) return;

        var scale = width / (max - min);
        double X(double t) => (t - min) * scale;

        // 视频轨
        if (VideoSeconds > 0 && VideoBrush is { } video)
        {
            var x = X(0);
            var w = Math.Max(2, VideoSeconds * scale);
            context.DrawRectangle(video, null, new Rect(x, 0, w, lane), radius, radius);
        }

        // 音频轨（带偏移）
        if (AudioSeconds > 0 && AudioBrush is { } audio)
        {
            var x = X(offset);
            var w = Math.Max(2, AudioSeconds * scale);
            context.DrawRectangle(audio, null, new Rect(x, lane + gap, w, lane), radius, radius);
        }

        // 零点的位置：用户能一眼看出“音频是提前还是延后”。
        var zeroX = X(0);
        if (zeroX >= 0 && zeroX <= width)
        {
            var pen = new Pen(new SolidColorBrush(Color.Parse("#332E2923")), 1);
            context.DrawLine(pen, new Point(zeroX, 0), new Point(zeroX, height));
        }
    }
}
