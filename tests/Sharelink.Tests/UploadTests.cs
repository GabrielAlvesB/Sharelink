using Sharelink.Settings;
using Sharelink.Upload;
using Sharelink.Upload.Providers;

namespace Sharelink.Tests;

public class UploadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sharelink-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Exemplo oficial da documentação da AWS ("GET Object" em Signature V4 para S3).</summary>
    [Fact]
    public void SigV4_MatchesAwsOfficialExample()
    {
        var headers = new Dictionary<string, string>
        {
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["range"] = "bytes=0-9",
            ["x-amz-content-sha256"] = SigV4Signer.EmptyPayloadHash,
            ["x-amz-date"] = "20130524T000000Z",
        };

        SigV4Signer.Signature signature = SigV4Signer.Sign("GET", "/test.txt", "", headers, SigV4Signer.EmptyPayloadHash,
            new DateTime(2013, 5, 24, 0, 0, 0, DateTimeKind.Utc), "us-east-1", "s3",
            "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");

        Assert.Equal("f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41", signature.Value);
        Assert.Equal("host;range;x-amz-content-sha256;x-amz-date", signature.SignedHeaders);
        Assert.Equal("20130524/us-east-1/s3/aws4_request", signature.CredentialScope);
    }

    [Fact]
    public void ObjectKey_IsRandomUrlSafeAndUnique()
    {
        var keys = new HashSet<string>();
        for (int i = 0; i < 10_000; i++)
        {
            string key = ObjectKey.Create("captura.png");
            Assert.True(ObjectKey.IsValid(key), key);
            Assert.Equal(26, key.Length);
            Assert.True(keys.Add(key));
        }
        Assert.EndsWith(".jpg", ObjectKey.Create("foto.JPEG"));
    }

    [Theory]
    [InlineData("pagina.html")]
    [InlineData("desenho.svg")]
    [InlineData("programa.exe")]
    [InlineData("script.js")]
    [InlineData("semextensao")]
    public void ObjectKey_RejectsUnsafeTypes(string file)
    {
        Assert.False(ObjectKey.IsAllowedFile(file));
        Assert.Throws<InvalidOperationException>(() => ObjectKey.Create(file));
    }

    [Theory]
    [InlineData("../outro-bucket/arquivo.png")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA.html")]
    [InlineData("curto.png")]
    public void ObjectKey_RejectsForeignKeys(string key) => Assert.False(ObjectKey.IsValid(key));

    [Fact]
    public void R2Config_AcceptsValidValuesAndBuildsCloudflareEndpoint()
    {
        Assert.True(R2Config.TryCreate("0123456789abcdef0123456789ABCDEF", "minhas-capturas", "https://pub-abc.r2.dev/",
            "0123456789abcdef0123456789abcdef", "segredo", out S3Target? target, out _));

        Assert.Equal("https://0123456789abcdef0123456789abcdef.r2.cloudflarestorage.com/minhas-capturas/abc.png", target!.ObjectUri("abc.png").AbsoluteUri);
        Assert.Equal("auto", target.Region);
        Assert.Equal("https://pub-abc.r2.dev/abc.png", target.PublicUrlFor("abc.png"));
        Assert.DoesNotContain("segredo", target.ToString());
    }

    [Theory]
    [InlineData("nao-e-hex", "bucket", "https://pub.r2.dev")]
    [InlineData("0123456789abcdef0123456789abcdef", "Bucket_Invalido", "https://pub.r2.dev")]
    [InlineData("0123456789abcdef0123456789abcdef", "bucket", "http://pub.r2.dev")]
    [InlineData("0123456789abcdef0123456789abcdef", "bucket", "https://user:senha@pub.r2.dev")]
    [InlineData("0123456789abcdef0123456789abcdef", "bucket", "https://pub.r2.dev/?x=1")]
    [InlineData("0123456789abcdef0123456789abcdef", "bucket", "javascript:alert(1)")]
    public void R2Config_RejectsInvalidValues(string account, string bucket, string url) =>
        Assert.False(R2Config.TryCreate(account, bucket, url, "0123456789abcdef0123456789abcdef", "s", out _, out _));

    [Fact]
    public void R2Config_RequiresSecret() =>
        Assert.False(R2Config.TryCreate("0123456789abcdef0123456789abcdef", "bucket", "https://pub.r2.dev",
            "0123456789abcdef0123456789abcdef", "", out _, out _));

    [Fact]
    public void CredentialStore_RoundTrip()
    {
        string target = $"Sharelink/Teste/{Guid.NewGuid():N}";
        try
        {
            CredentialStore.Save(target, "ID-DE-TESTE", "segredo com acentuação ✓");
            Assert.Equal(("ID-DE-TESTE", "segredo com acentuação ✓"), CredentialStore.Load(target));
        }
        finally
        {
            CredentialStore.Delete(target);
        }
        Assert.Null(CredentialStore.Load(target));
    }

    [Fact]
    public void LinkStore_RoundTripAndRemove()
    {
        string path = Path.Combine(_dir, "links.json");
        string file = Path.Combine(_dir, "captura.png");
        var info = new LinkInfo("https://pub.r2.dev/AAAAAAAAAAAAAAAAAAAAAA.png", "AAAAAAAAAAAAAAAAAAAAAA.png", new DateTime(2026, 10, 30),
            UploadDestination.S3Compatible);

        new LinkStore(path).Set(file, info);
        var reloaded = new LinkStore(path);
        Assert.Equal(info, reloaded.Get(file.ToUpperInvariant()));

        reloaded.Remove(file);
        Assert.Null(new LinkStore(path).Get(file));
    }

    [Fact]
    public void LinkStore_IgnoresTamperedEntries()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "links.json");
        File.WriteAllText(path, """
            {
              "C:\\a.png": { "Url": "http://inseguro/x.png", "Key": "AAAAAAAAAAAAAAAAAAAAAA.png", "UploadedAt": "2026-10-30T00:00:00" },
              "C:\\b.png": { "Url": "https://ok/x.png", "Key": "../../outro.png", "UploadedAt": "2026-10-30T00:00:00" }
            }
            """);

        var store = new LinkStore(path);
        Assert.Equal(0, store.Count);
    }
}
