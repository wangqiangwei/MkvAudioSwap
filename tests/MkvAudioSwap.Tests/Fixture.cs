using System.Diagnostics;

namespace MkvAudioSwap.Tests;

/// <summary>
/// 测试素材全部用 ffmpeg 现场合成，仓库里不放任何二进制。
/// 找不到 ffmpeg 时测试不会失败，而是跳过 —— 保证在没装 ffmpeg 的机器上也能跑单元测试。
/// ffmpeg 的查找走 <see cref="TestTools"/>，不写死任何绝对路径。
/// </summary>
public sealed class Fixture : IDisposable
{
    public string Dir { get; }
    public string? Ffmpeg { get; }

    public Fixture()
    {
        Dir = Path.Combine(Path.GetTempPath(), "MkvAudioSwapTests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Dir);
        Ffmpeg = TestTools.FfmpegOrNull();
    }

    public bool HasFfmpeg => Ffmpeg is not null;

    public int Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Ffmpeg!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>合成一个 5 秒的测试视频（h264 + 无声）。</summary>
    public string MakeVideo(string name = "src.mp4", double seconds = 5, string audio = "")
    {
        var path = Path.Combine(Dir, name);

        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-loglevel", "error",
            "-f", "lavfi", "-i", $"testsrc=size=320x240:rate=30:duration={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        };

        if (audio.Length > 0)
        {
            args.AddRange(["-f", "lavfi", "-i", audio]);
            args.AddRange(["-map", "0:v:0", "-map", "1:a:0", "-c:a", "aac", "-b:a", "96k"]);
        }
        else
        {
            args.AddRange(["-map", "0:v:0", "-an"]);
        }

        args.AddRange(["-c:v", "libx264", "-crf", "30", "-pix_fmt", "yuv420p", "-n", path]);

        Assert.Equal(0, Run([.. args]));
        return path;
    }

    /// <summary>合成一个线性 PCM 音频文件。</summary>
    public string MakePcm(string name, string codec, int rate, int channels, double seconds, string container)
    {
        var path = Path.Combine(Dir, name);
        var layout = channels == 1 ? "mono" : "stereo";
        var dur = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var exit = Run(
            "-hide_banner", "-nostdin", "-loglevel", "error",
            "-f", "lavfi", "-i", $"sine=frequency=440:sample_rate={rate}:duration={dur}",
            "-af", $"aformat=channel_layouts={layout}",
            "-c:a", codec, "-n", path);

        Assert.Equal(0, exit);
        return path;
    }

    /// <summary>读某个流的 CRC，用来证明"字节完全没变"。</summary>
    public string StreamCrc(string file, string mapSpec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Ffmpeg!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in new[]
                 {
                     "-hide_banner", "-nostdin", "-loglevel", "error",
                     "-i", file, "-map", mapSpec, "-c", "copy", "-f", "crc", "-",
                 })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();

        // 输出形如：CRC=0x1a2b3c4d
        var idx = output.IndexOf("CRC=", StringComparison.Ordinal);
        return idx >= 0 ? output[idx..].Trim() : output.Trim();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); }
        catch { }
    }
}
