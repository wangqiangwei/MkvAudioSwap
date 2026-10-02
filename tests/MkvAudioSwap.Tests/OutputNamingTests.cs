using MkvAudioSwap.Core;

namespace MkvAudioSwap.Tests;

public class OutputNamingTests : IDisposable
{
    private readonly string _dir;

    public OutputNamingTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "MkvAudioSwapNaming", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    private string MakeFile(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void 基本命名_源名加后缀并改扩展名为mkv()
    {
        var video = MakeFile("我的MV.mp4");
        var result = OutputNaming.BuildOutputPath(video);

        Assert.Equal("我的MV_替换音频.mkv", Path.GetFileName(result));
        Assert.Equal(_dir, Path.GetDirectoryName(result));
    }

    [Fact]
    public void 已存在时自动加序号()
    {
        var video = MakeFile("MV.mp4");
        MakeFile("MV_替换音频.mkv");

        var result = OutputNaming.BuildOutputPath(video);
        Assert.Equal("MV_替换音频(1).mkv", Path.GetFileName(result));
    }

    [Fact]
    public void 连续占用时序号递增()
    {
        var video = MakeFile("MV.mp4");
        MakeFile("MV_替换音频.mkv");
        MakeFile("MV_替换音频(1).mkv");
        MakeFile("MV_替换音频(2).mkv");

        var result = OutputNaming.BuildOutputPath(video);
        Assert.Equal("MV_替换音频(3).mkv", Path.GetFileName(result));
    }

    /// <summary>源文件本身就叫 xxx_替换音频.mkv 时，不能叠成 xxx_替换音频_替换音频.mkv。</summary>
    [Fact]
    public void 源名已含后缀时不叠加()
    {
        var video = MakeFile("MV_替换音频.mkv");
        var result = OutputNaming.BuildOutputPath(video);

        Assert.Equal("MV_替换音频_2.mkv", Path.GetFileName(result));
    }

    [Fact]
    public void 可以指定输出目录()
    {
        var video = MakeFile("MV.mp4");
        var other = Path.Combine(_dir, "out");
        Directory.CreateDirectory(other);

        var result = OutputNaming.BuildOutputPath(video, other);
        Assert.Equal(other, Path.GetDirectoryName(result));
    }

    [Fact]
    public void 临时文件保留原扩展名()
    {
        var result = OutputNaming.ToPartialPath(Path.Combine(_dir, "A_替换音频.mkv"));

        // ffmpeg 会靠扩展名推断容器，所以 .partial 必须插在 .mkv 前面
        Assert.EndsWith(".partial.mkv", result);
        Assert.Equal("A_替换音频.partial.mkv", Path.GetFileName(result));
    }

    [Fact]
    public void 预览文件放在系统临时目录且文件名合法()
    {
        var result = OutputNaming.BuildPreviewPath("含 非法:字符*的名字.mp4");

        Assert.StartsWith(OutputNaming.PreviewFolder, result);
        Assert.EndsWith(".mp4", result);
        Assert.DoesNotContain(":", Path.GetFileName(result));
        Assert.DoesNotContain("*", Path.GetFileName(result));
    }

    /// <summary>
    /// 预览路径必须是【固定】的。
    /// 曾经的做法是 preview_名字_HHmmss.mp4，每预览一次就永久多一个文件、
    /// 永远不会被覆盖，几分钟就能在临时目录堆出一堆几十 MB 的垃圾。
    /// 这条测试锁住"同一个路径"，防止时间戳被加回来。
    /// </summary>
    [Fact]
    public void 预览路径固定不变_不带时间戳()
    {
        var a = OutputNaming.BuildPreviewPath("第一次.mp4");
        var b = OutputNaming.BuildPreviewPath("完全不同的名字.mkv");

        Assert.Equal(a, b);
        Assert.Equal("preview.mp4", Path.GetFileName(a));
    }

    [Fact]
    public void 清理预览文件会删掉遗留文件()
    {
        var folder = Path.Combine(_dir, "preview_folder");
        Directory.CreateDirectory(folder);
        var f1 = Path.Combine(folder, "preview.mp4");
        var f2 = Path.Combine(folder, "preview_旧名字_120000.mp4");
        File.WriteAllText(f1, "x");
        File.WriteAllText(f2, "x");

        OutputNaming.PreviewFolderOverride = folder;
        try
        {
            var deleted = OutputNaming.CleanupPreviewFiles();

            Assert.Equal(2, deleted);
            Assert.False(File.Exists(f1));
            Assert.False(File.Exists(f2));
        }
        finally
        {
            OutputNaming.PreviewFolderOverride = null;
        }
    }

    [Fact]
    public void 清理预览文件在目录不存在时安静返回()
    {
        OutputNaming.PreviewFolderOverride = Path.Combine(_dir, "根本不存在");
        try
        {
            Assert.Equal(0, OutputNaming.CleanupPreviewFiles());
        }
        finally
        {
            OutputNaming.PreviewFolderOverride = null;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
