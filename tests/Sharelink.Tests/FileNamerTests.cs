using Sharelink.Output;
using Sharelink.Settings;

namespace Sharelink.Tests;

public class FileNamerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sharelink-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Expand_ReplacesDateTokens()
    {
        var now = new DateTime(2026, 10, 30, 14, 5, 9);
        Assert.Equal("Sharelink_2026-10-30_14-05-09", FileNamer.Expand("Sharelink_{yyyy-MM-dd_HH-mm-ss}", now));
        Assert.Equal("print sem data", FileNamer.Expand("print sem data", now));
    }

    [Theory]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("..", "Sharelink")]
    [InlineData("   ", "Sharelink")]
    [InlineData("nome.", "nome")]
    [InlineData("a:b*c?d\"e<f>g|h", "a_b_c_d_e_f_g_h")]
    public void Sanitize_RemovesDangerousCharacters(string input, string expected) =>
        Assert.Equal(expected, FileNamer.Sanitize(input));

    [Fact]
    public void Sanitize_LimitsLength() =>
        Assert.True(FileNamer.Sanitize(new string('x', 500)).Length <= 120);

    [Fact]
    public void GetUniquePath_DoesNotOverwrite()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "shot.png"), "");
        File.WriteAllText(Path.Combine(_dir, "shot (2).png"), "");

        Assert.Equal(Path.Combine(_dir, "shot (3).png"), FileNamer.GetUniquePath(_dir, "shot", ".png"));
    }

    [Fact]
    public void CreatePath_StaysInsideOutputFolder()
    {
        var settings = new AppSettings { OutputFolder = _dir, FileNamePattern = @"..\..\fuga_{yyyy}", UseMonthSubfolder = true };
        string path = FileNamer.CreatePath(settings, new DateTime(2026, 10, 30), ".png");

        Assert.StartsWith(Path.Combine(_dir, "2026-10") + Path.DirectorySeparatorChar, path);
        Assert.EndsWith(".png", path);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
    }
}
