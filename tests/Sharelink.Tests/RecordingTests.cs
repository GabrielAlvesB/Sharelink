using Sharelink.Common;
using Sharelink.Recording;

namespace Sharelink.Tests;

public class RecordingTests
{
    [Fact]
    public void FromPoints_NormalizesAnyDragDirection()
    {
        Assert.Equal(new Rectangle(10, 20, 30, 40), RectangleMath.FromPoints(new Point(40, 60), new Point(10, 20)));
        Assert.Equal(new Rectangle(10, 20, 30, 40), RectangleMath.FromPoints(new Point(10, 20), new Point(40, 60)));
    }

    [Fact]
    public void MakeEven_RoundsSizeDown() =>
        Assert.Equal(new Rectangle(-1921, 5, 100, 50), RectangleMath.MakeEven(new Rectangle(-1921, 5, 101, 51)));

    [Fact]
    public void CaptureArguments_IncludeRegionAndEndWithOutput()
    {
        List<string> args = FfmpegArguments.Capture(new Rectangle(-1920, 10, 640, 480), 30, true, @"C:\out\video.mp4", intermediate: false);

        Assert.Equal(@"C:\out\video.mp4", args[^1]);
        Assert.Equal("-1920", args[args.IndexOf("-offset_x") + 1]);
        Assert.Equal("10", args[args.IndexOf("-offset_y") + 1]);
        Assert.Equal("640x480", args[args.IndexOf("-video_size") + 1]);
        Assert.Equal("gdigrab", args[args.IndexOf("-f") + 1]);
        Assert.Contains("+faststart", args);
    }

    [Fact]
    public void OutputPathWithSpacesStaysOneArgument()
    {
        const string path = @"C:\Minha Pasta\-i evil.mp4";
        List<string> args = FfmpegArguments.ConvertToGif(path, @"C:\x.gif", 15);
        Assert.Equal(path, args[args.IndexOf("-i") + 1]);
    }

    [Fact]
    public void Locator_RejectsFilesNotNamedFfmpeg()
    {
        string fake = Path.Combine(Path.GetTempPath(), $"notffmpeg_{Guid.NewGuid():N}.exe");
        File.WriteAllText(fake, "");
        try
        {
            Assert.False(FfmpegLocator.IsValid(fake));
            Assert.Null(FfmpegLocator.Find(fake));
        }
        finally
        {
            File.Delete(fake);
        }
    }
}
