using System.Globalization;
using Sharelink.Upload;

namespace Sharelink.Gallery;

internal enum GalleryItemKind
{
    Image,
    Gif,
    Video,
}

internal sealed record GalleryItem(string FullPath, DateTime Modified, long Size, GalleryItemKind Kind)
{
    public string Name => Path.GetFileName(FullPath);
}

/// <summary>Lista as capturas da pasta (antigas e novas), da mais recente para a mais antiga.</summary>
internal static class GalleryScanner
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static GalleryItemKind? KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" => GalleryItemKind.Image,
        ".gif" => GalleryItemKind.Gif,
        ".mp4" => GalleryItemKind.Video,
        _ => null,
    };

    public static List<GalleryItem> Scan(string folder)
    {
        if (!Directory.Exists(folder))
            return [];

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MaxRecursionDepth = 4,
        };

        var items = new List<GalleryItem>();
        foreach (string path in Directory.EnumerateFiles(folder, "*", options))
        {
            if (KindOf(path) is not GalleryItemKind kind)
                continue;
            try
            {
                var info = new FileInfo(path);
                items.Add(new GalleryItem(info.FullName, info.LastWriteTime, info.Length, kind));
            }
            catch (IOException)
            {
                // Arquivo removido durante a varredura.
            }
        }
        items.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return items;
    }

    public static bool Matches(GalleryItem item, string search, GalleryFilter filter, LinkStore links)
    {
        if (search.Length > 0 && !item.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            return false;
        return filter switch
        {
            GalleryFilter.WithLink => links.Get(item.FullPath) is not null,
            GalleryFilter.Images => item.Kind == GalleryItemKind.Image,
            GalleryFilter.Videos => item.Kind is GalleryItemKind.Video or GalleryItemKind.Gif,
            _ => true,
        };
    }

    /// <summary>"Hoje", "Ontem" ou a data por extenso.</summary>
    public static string GroupName(DateTime date, DateTime today)
    {
        int days = (today.Date - date.Date).Days;
        if (days == 0) return "Hoje";
        if (days == 1) return "Ontem";
        string text = date.ToString(date.Year == today.Year ? "dddd, d 'de' MMMM" : "d 'de' MMMM 'de' yyyy", PtBr);
        return char.ToUpper(text[0], PtBr) + text[1..];
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };
}

internal enum GalleryFilter
{
    All,
    WithLink,
    Images,
    Videos,
}
