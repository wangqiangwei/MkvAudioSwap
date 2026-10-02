using MkvAudioSwap.Core;

namespace MkvAudioSwap.Tests;

/// <summary>
/// 命令构造的回归测试。这些断言锁住的是"踩过的坑"，不是"当前实现的写法"。
/// 任何一条挂掉都意味着可能又出现了静默错误。
/// </summary>
public class CommandBuilderTests
{
    [Fact]
    public void 正式输出_不包含任何重编码参数()
    {
        var args = CommandBuilder.BuildSwap("v.mp4", "a.caf", "out.mkv");

        Assert.Contains("copy", args);
        Assert.DoesNotContain("libx264", args);
        Assert.DoesNotContain("aac", args);
        Assert.DoesNotContain("-crf", args);
        Assert.DoesNotContain("-b:a", args);
    }

    [Fact]
    public void 正式输出_只保留第一条视频流和第一条音频流()
    {
        var args = CommandBuilder.BuildSwap("v.mp4", "a.caf", "out.mkv");
        var joined = string.Join(" ", args);

        Assert.Contains("-map 0:v:0", joined);
        Assert.Contains("-map 1:a:0", joined);

        // 不允许出现任何通配 map（会把字幕/章节/封面一起带进来）
        Assert.DoesNotContain("-map 0 ", joined);
        Assert.DoesNotContain("-map 0?", joined);
        Assert.DoesNotContain("0:v?", joined);
    }

    /// <summary>
    /// 最关键的一条：负偏移时必须显式关闭 avoid_negative_ts。
    /// 用默认参数时 ffmpeg 会把【视频】整体往后推来消除负时间戳 ——
    /// 实测：-itsoffset -0.3 默认参数 → 视频 start=0.300 / 音频 start=0.000（静默错误）
    ///       -avoid_negative_ts disabled → 视频 start=0.000 / 音频 start=-0.300（正确）
    /// 删掉这个参数不会报错，只会让用户的画面偏移，所以必须有测试守着。
    /// </summary>
    [Fact]
    public void 负偏移_必须关闭avoid_negative_ts()
    {
        var args = CommandBuilder.BuildSwap("v.mp4", "a.caf", "out.mkv", offsetSeconds: -0.3);
        var joined = string.Join(" ", args);

        Assert.Contains("-avoid_negative_ts disabled", joined);
    }

    [Fact]
    public void 零偏移_不生成itsoffset参数()
    {
        var args = CommandBuilder.BuildSwap("v.mp4", "a.caf", "out.mkv", offsetSeconds: 0);
        Assert.DoesNotContain("-itsoffset", args);
    }

    /// <summary>
    /// -itsoffset 只作用于紧跟其后的那个输入。
    /// 如果它出现在音频的 -i 之后（或视频的 -i 之前），偏移会静默失效。
    /// </summary>
    [Fact]
    public void 偏移参数必须紧跟在音频输入之前()
    {
        var args = CommandBuilder.BuildSwap("v.mp4", "a.caf", "out.mkv", offsetSeconds: 0.5);

        var offsetIdx = args.IndexOf("-itsoffset");
        Assert.True(offsetIdx > 0, "应该存在 -itsoffset");

        // 断言结构关系：... -itsoffset, <值>, -i, <音频路径> ...
        // 不用 args.IndexOf 找别的位置，避免和参数值撞上。
        var audioInputIdx = args.IndexOf("a.caf");
        Assert.Equal("-i", args[audioInputIdx - 1]);
        Assert.Equal(audioInputIdx - 2, offsetIdx + 1);
        Assert.Equal("-itsoffset", args[offsetIdx]);

        var videoInputIdx = args.IndexOf("v.mp4");
        Assert.True(offsetIdx > videoInputIdx, "-itsoffset 必须排在视频输入之后，否则会作用到错误的输入上");
    }

    [Fact]
    public void 偏移值使用不变文化格式化()
    {
        // 中文/德文等区域设置下小数点可能是逗号，会让 ffmpeg 解析失败
        var args = CommandBuilder.BuildSwap("v.mp4", "a.caf", "out.mkv", offsetSeconds: 0.125);
        Assert.Contains("0.125", args);
        Assert.DoesNotContain("0,125", args);
    }

    [Fact]
    public void 不复制容器元数据()
    {
        var args = CommandBuilder.BuildSwap("v.mp4", "a.caf", "out.mkv");
        var joined = string.Join(" ", args);

        // 源容器元数据是按旧音轨写的，复制可能和新音轨时长冲突
        Assert.Contains("-map_metadata -1", joined);
    }

    [Fact]
    public void 不使用y_除非调用方保证文件名唯一()
    {
        // 程序自己生成唯一文件名，所以用 -n：
        // 万一撞名，ffmpeg 直接失败退出，而不是弹一个看不见的提问把进程挂死
        var args = CommandBuilder.BuildSwap("v.mp4", "a.caf", "out.mkv");
        Assert.Contains("-n", args);
        Assert.DoesNotContain("-y", args);
    }

    [Fact]
    public void 预览_重编码视频但音频不裁剪()
    {
        var args = CommandBuilder.BuildPreview("v.mp4", "a.caf", "p.mp4", 0);
        var joined = string.Join(" ", args);

        Assert.Contains("-c:v libx264", joined);
        Assert.Contains("-preset ultrafast", joined);
        Assert.Contains("scale=640:-2", joined);

        // -t 只能作用在视频上：音频保留完整时长，用户才能检查"结尾有没有被切断"
        var tIdx = args.IndexOf("-t");
        var mapAudioIdx = joined.IndexOf("-map 1:a:0", StringComparison.Ordinal);
        Assert.True(tIdx > 0);
        Assert.True(args.IndexOf("-t") < args.IndexOf("-c:a"), "-t 在音频编码参数之前，只约束视频");
        Assert.True(mapAudioIdx > 0);
    }

    [Fact]
    public void 预览时长取视频时长与十秒的较小值()
    {
        Assert.Equal(10, CommandBuilder.EffectivePreviewSeconds(300));
        Assert.Equal(4.5, CommandBuilder.EffectivePreviewSeconds(4.5));
        Assert.Equal(10, CommandBuilder.EffectivePreviewSeconds(null));
    }
}
