using Sharelink.Settings;
using Sharelink.Theme;
using Sharelink.Upload;
using Sharelink.Upload.Providers;

namespace Sharelink.Tests;

public class ProviderTests : IDisposable
{
    private const string Key = "AAAAAAAAAAAAAAAAAAAAAA.png";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sharelink-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // ---------- S3 compatível ----------

    [Fact]
    public void S3_VirtualHostedAndPathStyleUrls()
    {
        Assert.True(S3Config.TryCreate("https://s3.us-west-004.backblazeb2.com", "us-west-004", "meu-bucket", "https://f004.backblazeb2.com/file/meu-bucket",
            pathStyle: false, publicReadAcl: false, "0041234567890ab0000000001", "segredo", out S3Target? virtualHosted, out string error), error);
        Assert.Equal($"https://meu-bucket.s3.us-west-004.backblazeb2.com/{Key}", virtualHosted!.ObjectUri(Key).AbsoluteUri);
        Assert.Equal($"https://f004.backblazeb2.com/file/meu-bucket/{Key}", virtualHosted.PublicUrlFor(Key));

        Assert.True(S3Config.TryCreate("https://minio.exemplo.com:9000", "us-east-1", "capturas", "https://minio.exemplo.com:9000/capturas",
            pathStyle: true, publicReadAcl: false, "minioadmin", "segredo", out S3Target? pathStyle, out _));
        Assert.Equal($"https://minio.exemplo.com:9000/capturas/{Key}", pathStyle!.ObjectUri(Key).AbsoluteUri);
    }

    [Theory]
    [InlineData("http://s3.amazonaws.com", "us-east-1", "bucket", false)] // sem https
    [InlineData("https://s3.amazonaws.com/caminho", "us-east-1", "bucket", false)] // endpoint com caminho
    [InlineData("https://s3.amazonaws.com", "Região Inválida", "bucket", false)]
    [InlineData("https://s3.amazonaws.com", "us-east-1", "com.ponto", false)] // ponto exige path-style
    [InlineData("https://s3.amazonaws.com", "us-east-1", "BUCKET", true)]
    public void S3_RejectsInvalidValues(string endpoint, string region, string bucket, bool pathStyle) =>
        Assert.False(S3Config.TryCreate(endpoint, region, bucket, "https://cdn.exemplo.com", pathStyle, false, "AKIAEXEMPLO", "s", out _, out _));

    [Fact]
    public void S3_AccessKeyCannotInjectHeaderSyntax() =>
        Assert.False(S3Config.IsValidAccessKeyId("AKIA, SignedHeaders=host"));

    // ---------- Supabase ----------

    [Fact]
    public void Supabase_BuildsStorageS3AndPublicUrls()
    {
        Assert.True(SupabaseConfig.TryCreate("abcdefghijklmnopqrst", "sa-east-1", "capturas", "0123456789abcdef0123456789abcdef", "segredo",
            out S3Target? target, out string error), error);

        Assert.Equal($"https://abcdefghijklmnopqrst.storage.supabase.co/storage/v1/s3/capturas/{Key}", target!.ObjectUri(Key).AbsoluteUri);
        Assert.Equal("/storage/v1/s3/capturas/" + Key, target.ObjectUri(Key).AbsolutePath);
        Assert.Equal($"https://abcdefghijklmnopqrst.supabase.co/storage/v1/object/public/capturas/{Key}", target.PublicUrlFor(Key));
        Assert.Equal("sa-east-1", target.Region);
        Assert.Equal(UploadDestination.Supabase, target.Destination);
    }

    [Theory]
    [InlineData("evil.com/x", "sa-east-1", "capturas")] // project ref não pode virar outro host
    [InlineData("abcdefghijklmnopqrst", "Brasil", "capturas")]
    [InlineData("abcdefghijklmnopqrst", "sa-east-1", "pasta/../fora")]
    public void Supabase_RejectsInvalidValues(string project, string region, string bucket) =>
        Assert.False(SupabaseConfig.TryCreate(project, region, bucket, "0123456789abcdef", "s", out _, out _));

    [Fact]
    public void Supabase_RemoteKeysUseSharelinkFormat()
    {
        Assert.True(UploaderFactory.IsValidRemoteKey(UploadDestination.Supabase, Key));
        Assert.False(UploaderFactory.IsValidRemoteKey(UploadDestination.Supabase, "../" + Key));
    }

    // ---------- Imgur ----------

    [Fact]
    public void Imgur_ParsesUploadResponse()
    {
        (string link, string hash) = ImgurClient.ParseUploadResponse(
            """{ "data": { "id": "abc123", "link": "https://i.imgur.com/abc123.png", "deletehash": "XyZ987abcDEF" }, "success": true, "status": 200 }""");
        Assert.Equal("https://i.imgur.com/abc123.png", link);
        Assert.Equal("XyZ987abcDEF", hash);
    }

    [Theory]
    [InlineData("""{ "data": { "link": "http://i.imgur.com/a.png", "deletehash": "abcdef12" } }""")]
    [InlineData("""{ "data": { "link": "https://imgur.com.malicioso.com/a.png", "deletehash": "abcdef12" } }""")]
    [InlineData("""{ "data": { "link": "https://i.imgur.com/a.png", "deletehash": "../../x" } }""")]
    public void Imgur_RejectsSuspiciousResponses(string json) =>
        Assert.Throws<InvalidOperationException>(() => ImgurClient.ParseUploadResponse(json));

    [Fact]
    public void Imgur_ClientIdValidation()
    {
        Assert.True(UploaderFactory.TryCreateImgur("1a2b3c4d5e6f7a8", out _, out _));
        Assert.False(UploaderFactory.TryCreateImgur("", out _, out _));
        Assert.False(UploaderFactory.TryCreateImgur("id com espaço", out _, out _));
    }

    // ---------- Nextcloud / WebDAV ----------

    [Fact]
    public void WebDav_NextcloudFolderUrlEscapesUserAndFolder()
    {
        Assert.True(UploaderFactory.TryCreateWebDav(WebDavMode.Nextcloud, "https://nuvem.exemplo.com/nextcloud", "joão silva", "senha-app",
            "Fotos/Prints 2026", null, out WebDavTarget? target, out string error), error);
        Assert.Equal("https://nuvem.exemplo.com/nextcloud/remote.php/dav/files/jo%C3%A3o%20silva/Fotos/Prints%202026/", target!.FolderUrl);
        Assert.DoesNotContain("senha-app", target.ToString());
    }

    [Fact]
    public void WebDav_ParsesNextcloudShare()
    {
        (string id, string url) = WebDavClient.ParseShareResponse(
            """{ "ocs": { "meta": { "statuscode": 200 }, "data": { "id": 42, "url": "https://nuvem.exemplo.com/s/AbCdEf" } } }""");
        Assert.Equal("42", id);
        Assert.Equal("https://nuvem.exemplo.com/s/AbCdEf", url);
        Assert.Throws<InvalidOperationException>(() => WebDavClient.ParseShareResponse(
            """{ "ocs": { "data": { "id": "42", "url": "http://inseguro/s/x" } } }"""));
    }

    [Theory]
    [InlineData(WebDavMode.Nextcloud, "http://nuvem.exemplo.com", "user", "Sharelink", null)] // sem https
    [InlineData(WebDavMode.Nextcloud, "https://nuvem.exemplo.com", "user:x", "Sharelink", null)] // ":" quebra o login
    [InlineData(WebDavMode.Nextcloud, "https://nuvem.exemplo.com", "user", "../fora", null)] // sair da pasta
    [InlineData(WebDavMode.Generic, "https://dav.exemplo.com", "user", "Sharelink", null)] // genérico sem URL pública
    public void WebDav_RejectsInvalidValues(WebDavMode mode, string server, string user, string folder, string? publicUrl) =>
        Assert.False(UploaderFactory.TryCreateWebDav(mode, server, user, "senha", folder, publicUrl, out _, out _));

    [Theory]
    [InlineData(UploadDestination.CloudflareR2, Key, true)]
    [InlineData(UploadDestination.S3Compatible, "../" + Key, false)]
    [InlineData(UploadDestination.Imgur, "XyZ987abcDEF", true)]
    [InlineData(UploadDestination.Imgur, "a/b", false)]
    [InlineData(UploadDestination.WebDav, "42:" + Key, true)]
    [InlineData(UploadDestination.WebDav, Key, true)]
    [InlineData(UploadDestination.WebDav, "42:../../senhas.txt", false)]
    public void RemoteKeys_OnlyFormatsSharelinkCreates(UploadDestination destination, string key, bool valid) =>
        Assert.Equal(valid, UploaderFactory.IsValidRemoteKey(destination, key));

    // ---------- Conexões ----------

    [Theory]
    [InlineData("http://api.imgur.com/3/image", "api.imgur.com")]
    [InlineData("https://outro-site.com/3/image", "api.imgur.com")]
    [InlineData("https://usuario:senha@api.imgur.com/3/image", "api.imgur.com")]
    public void SecureHttp_BlocksAnythingButHttpsToExpectedHost(string url, string host) =>
        Assert.Throws<InvalidOperationException>(() => SecureHttp.EnsureAllowed(new Uri(url), host));

    [Fact]
    public void SecureHttp_AllowsExpectedHost() =>
        SecureHttp.EnsureAllowed(new Uri("https://API.imgur.com/3/image"), "api.imgur.com");

    // ---------- Links antigos e tema ----------

    [Fact]
    public void LinkStore_LegacyEntriesWithoutDestinationAreR2()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "links.json");
        File.WriteAllText(path, $$"""{ "C:\\a.png": { "Url": "https://pub.r2.dev/{{Key}}", "Key": "{{Key}}", "UploadedAt": "2026-10-30T00:00:00" } }""");

        LinkInfo? info = new LinkStore(path).Get(@"C:\a.png");
        Assert.NotNull(info);
        Assert.Equal(UploadDestination.CloudflareR2, info.EffectiveDestination);
    }

    [Theory]
    [InlineData(ThemeMode.System, true, true)]
    [InlineData(ThemeMode.System, false, false)]
    [InlineData(ThemeMode.Dark, false, true)]
    [InlineData(ThemeMode.Light, true, false)]
    public void Theme_ResolvesFromModeAndWindows(ThemeMode mode, bool windowsDark, bool expectDark) =>
        Assert.Equal(expectDark, ThemeManager.ResolvePalette(mode, windowsDark).IsDark);
}
