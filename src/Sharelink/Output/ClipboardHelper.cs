using System.Collections.Specialized;
using System.Drawing.Imaging;

namespace Sharelink.Output;

/// <summary>Área de transferência com novas tentativas (outro programa pode estar com ela aberta).</summary>
internal static class ClipboardHelper
{
    private const int RetryTimes = 10;
    private const int RetryDelayMs = 100;

    public static void SetImage(Image image)
    {
        var data = new DataObject();
        data.SetImage(image);
        // Formato "PNG" preserva qualidade em navegadores, Discord, Slack etc.
        var png = new MemoryStream();
        image.Save(png, ImageFormat.Png);
        data.SetData("PNG", false, png);
        Clipboard.SetDataObject(data, true, RetryTimes, RetryDelayMs);
    }

    public static void SetText(string text) =>
        Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true, RetryTimes, RetryDelayMs);

    public static void SetFile(string path) => SetFiles([path]);

    public static void SetFiles(IEnumerable<string> paths)
    {
        var list = new StringCollection();
        foreach (string path in paths)
            list.Add(path);
        var data = new DataObject();
        data.SetFileDropList(list);
        Clipboard.SetDataObject(data, true, RetryTimes, RetryDelayMs);
    }
}
