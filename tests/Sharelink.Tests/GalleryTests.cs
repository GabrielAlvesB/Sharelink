using Sharelink.Gallery;
using Sharelink.Upload;

namespace Sharelink.Tests;

public class GalleryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sharelink-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string CreateFile(string relative, DateTime modified)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        File.SetLastWriteTime(path, modified);
        return path;
    }

    [Fact]
    public void Scan_FindsOldAndNewCapturesNewestFirst()
    {
        CreateFile(@"2025-01\antiga.png", new DateTime(2025, 1, 10));
        CreateFile(@"2026-10\nova.jpg", new DateTime(2026, 10, 30));
        CreateFile(@"2026-10\video.mp4", new DateTime(2026, 10, 29));
        CreateFile(@"2026-10\anim.gif", new DateTime(2026, 10, 28));
        CreateFile(@"2026-10\notas.txt", new DateTime(2026, 10, 30));
        CreateFile(@"2026-10\pagina.html", new DateTime(2026, 10, 30));

        List<GalleryItem> items = GalleryScanner.Scan(_dir);

        Assert.Equal(["nova.jpg", "video.mp4", "anim.gif", "antiga.png"], items.Select(i => i.Name));
        Assert.Equal(GalleryItemKind.Video, items[1].Kind);
        Assert.Equal(GalleryItemKind.Gif, items[2].Kind);
    }

    [Fact]
    public void Scan_MissingFolderReturnsEmpty() => Assert.Empty(GalleryScanner.Scan(Path.Combine(_dir, "nao-existe")));

    [Fact]
    public void GroupName_TodayYesterdayAndDates()
    {
        var today = new DateTime(2026, 10, 30, 15, 0, 0);
        Assert.Equal("Hoje", GalleryScanner.GroupName(new DateTime(2026, 10, 30, 1, 0, 0), today));
        Assert.Equal("Ontem", GalleryScanner.GroupName(new DateTime(2026, 10, 29, 23, 0, 0), today));
        Assert.Equal("Terça-feira, 27 de outubro", GalleryScanner.GroupName(new DateTime(2026, 10, 27), today));
        Assert.Equal("10 de janeiro de 2025", GalleryScanner.GroupName(new DateTime(2025, 1, 10), today));
    }

    [Fact]
    public void Matches_FiltersBySearchAndLink()
    {
        string linkedPath = CreateFile("com-link.png", DateTime.Now);
        string plainPath = CreateFile("sem-link.png", DateTime.Now);
        var links = new LinkStore(Path.Combine(_dir, "links.json"));
        links.Set(linkedPath, new LinkInfo("https://pub.r2.dev/AAAAAAAAAAAAAAAAAAAAAA.png", "AAAAAAAAAAAAAAAAAAAAAA.png", DateTime.Now));

        var linked = new GalleryItem(linkedPath, DateTime.Now, 1, GalleryItemKind.Image);
        var plain = new GalleryItem(plainPath, DateTime.Now, 1, GalleryItemKind.Image);

        Assert.True(GalleryScanner.Matches(linked, "", GalleryFilter.WithLink, links));
        Assert.False(GalleryScanner.Matches(plain, "", GalleryFilter.WithLink, links));
        Assert.True(GalleryScanner.Matches(plain, "SEM", GalleryFilter.All, links));
        Assert.False(GalleryScanner.Matches(plain, "xyz", GalleryFilter.All, links));
        Assert.False(GalleryScanner.Matches(plain, "", GalleryFilter.Videos, links));
    }
}
