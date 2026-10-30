using System.Drawing.Imaging;
using Sharelink.Settings;

namespace Sharelink.Output;

internal static class ImageSaver
{
    public static string GetExtension(ImageFormatOption format) => format == ImageFormatOption.Jpeg ? ".jpg" : ".png";

    public static ImageFormatOption FormatFromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" ? ImageFormatOption.Jpeg : ImageFormatOption.Png;

    public static void Save(Image image, string path, ImageFormatOption format, int jpegQuality)
    {
        if (format == ImageFormatOption.Png)
        {
            image.Save(path, ImageFormat.Png);
            return;
        }

        ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)jpegQuality);
        image.Save(path, codec, parameters);
    }
}
