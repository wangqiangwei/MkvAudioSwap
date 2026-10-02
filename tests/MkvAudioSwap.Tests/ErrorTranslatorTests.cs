using MkvAudioSwap.Core;

namespace MkvAudioSwap.Tests;

public class ErrorTranslatorTests
{
    [Fact]
    public void 找不到音频流时给出可操作的建议()
    {
        var (title, advice) = ErrorTranslator.TranslateFfmpegError(
            "[out#0/matroska @ 0x1] Stream map '1:a:0' matches no streams.\nError opening output file out.mkv.",
            @"E:\out.mkv");

        Assert.Contains("音频流", title);
        Assert.Contains("播放器", advice);
    }

    [Fact]
    public void 权限问题提示换保存位置()
    {
        var (title, advice) = ErrorTranslator.TranslateFfmpegError(
            "Error opening output file E:\\out.mkv.\nPermission denied", @"E:\out.mkv");

        Assert.Contains("写入", title);
        Assert.Contains("更改", advice);
    }

    [Fact]
    public void 磁盘满时明确说空间不足()
    {
        var (title, _) = ErrorTranslator.TranslateFfmpegError("av_interleaved_write_frame(): No space left on device", "x");
        Assert.Contains("空间", title);
    }

    [Fact]
    public void 文件损坏时提示重新获取()
    {
        var (title, _) = ErrorTranslator.TranslateFfmpegError("Invalid data found when processing input", "x");
        Assert.Contains("无法读取", title);
    }

    [Fact]
    public void 容器不兼容时明确说明不会自动转码()
    {
        var (title, advice) = ErrorTranslator.TranslateFfmpegError(
            "Could not write header for output file #0 (incorrect codec parameters ?): not supported in container", "x");

        Assert.Contains("mkv", title);
        Assert.Contains("不重编码", advice);
    }

    /// <summary>无法识别的错误必须保留原文，否则用户和作者都无从查起。</summary>
    [Fact]
    public void 未知错误保留最后一行原文()
    {
        var (title, advice) = ErrorTranslator.TranslateFfmpegError(
            "something weird happened here\nvery strange error 42", "x");

        Assert.Equal("处理失败", title);
        Assert.Contains("very strange error 42", advice);
        Assert.Contains("复制详情", advice);
    }

    [Fact]
    public void 空stderr不会崩()
    {
        var (title, _) = ErrorTranslator.TranslateFfmpegError("", "x");
        Assert.Equal("处理失败", title);
    }

    [Fact]
    public void probe错误_未写完的mp4有专门提示()
    {
        var msg = ErrorTranslator.TranslateProbeError("moov atom not found");
        Assert.Contains("没有写完", msg);
    }
}
