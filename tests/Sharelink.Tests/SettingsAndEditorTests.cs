using Sharelink.Editor.Shapes;
using Sharelink.Settings;

namespace Sharelink.Tests;

public class SettingsAndEditorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sharelink-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        string path = Path.Combine(_dir, "settings.json");
        var original = new AppSettings
        {
            OutputFolder = @"D:\Prints",
            ImageFormat = ImageFormatOption.Jpeg,
            ClipboardAction = ClipboardAction.File,
            Hotkeys = { CaptureRegion = "Ctrl+Alt+R" },
        };

        SettingsStore.Save(original, path);
        AppSettings loaded = SettingsStore.Load(path, out bool firstRun);

        Assert.False(firstRun);
        Assert.Equal(@"D:\Prints", loaded.OutputFolder);
        Assert.Equal(ImageFormatOption.Jpeg, loaded.ImageFormat);
        Assert.Equal(ClipboardAction.File, loaded.ClipboardAction);
        Assert.Equal("Ctrl+Alt+R", loaded.Hotkeys.CaptureRegion);
    }

    [Fact]
    public void Settings_CorruptFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ isto não é json");

        AppSettings loaded = SettingsStore.Load(path, out _);

        Assert.Equal(new AppSettings().FileNamePattern, loaded.FileNamePattern);
        Assert.True(File.Exists(path + ".invalido"));
    }

    [Fact]
    public void Settings_OutOfRangeValuesAreClamped()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "JpegQuality": 5000, "VideoFps": 0, "OutputFolder": "" }""");

        AppSettings loaded = SettingsStore.Load(path, out _);

        Assert.Equal(100, loaded.JpegQuality);
        Assert.Equal(5, loaded.VideoFps);
        Assert.Equal(AppSettings.DefaultOutputFolder, loaded.OutputFolder);
    }

    [Fact]
    public void Pixelate_DestroysDetailInsideArea()
    {
        // Xadrez preto/branco de 1px: depois de pixelar, cada bloco vira um cinza uniforme.
        using var source = new Bitmap(32, 32);
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
            source.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : Color.White);

        var shape = new PixelateShape(new Point(0, 0)) { Thickness = 4 };
        shape.Drag(new Point(16, 16));

        using var output = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(output))
        {
            g.DrawImageUnscaled(source, 0, 0);
            shape.Draw(g, source);
        }

        Color first = output.GetPixel(0, 0);
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 12; x++)
            Assert.Equal(first.ToArgb(), output.GetPixel(x, y).ToArgb());
        Assert.InRange(first.R, 100, 155);
        // Fora da área, nada muda.
        Assert.Equal(source.GetPixel(20, 20).ToArgb(), output.GetPixel(20, 20).ToArgb());
    }
}
