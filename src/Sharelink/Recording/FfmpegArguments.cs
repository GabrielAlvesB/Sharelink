using System.Globalization;

namespace Sharelink.Recording;

public enum RecordingFormat
{
    Mp4,
    Gif,
}

/// <summary>
/// Monta os argumentos do FFmpeg como lista (cada item vira um argumento separado),
/// então nenhum valor consegue "escapar" e injetar opções extras.
/// </summary>
internal static class FfmpegArguments
{
    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Grava a área da tela (gdigrab) em H.264. Para GIF, este é o arquivo intermediário.</summary>
    public static List<string> Capture(Rectangle region, int fps, bool drawMouse, string outputPath, bool intermediate)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "gdigrab",
            "-framerate", Num(fps),
            "-draw_mouse", drawMouse ? "1" : "0",
            "-offset_x", Num(region.X),
            "-offset_y", Num(region.Y),
            "-video_size", $"{Num(region.Width)}x{Num(region.Height)}",
            "-i", "desktop",
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
        };

        if (intermediate)
            args.AddRange(["-preset", "ultrafast", "-crf", "15"]);
        else
            args.AddRange(["-preset", "veryfast", "-crf", "23", "-movflags", "+faststart"]);

        args.Add(outputPath);
        return args;
    }

    /// <summary>Converte o vídeo intermediário em GIF com paleta otimizada.</summary>
    public static List<string> ConvertToGif(string inputPath, string outputPath, int fps) =>
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-i", inputPath,
        "-vf", $"fps={Num(fps)},split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle",
        "-loop", "0",
        outputPath,
    ];
}
