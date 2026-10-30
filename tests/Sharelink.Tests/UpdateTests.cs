using Sharelink.Update;

namespace Sharelink.Tests;

public class UpdateTests
{
    private const string Hash = "aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899";

    [Fact]
    public void Signature_ValidOnlyForSameVersionHashAndKey()
    {
        (string privateKey, string publicKey) = UpdateSignature.CreateKeyPair();
        string signature = UpdateSignature.Sign(privateKey, "1.2.0", Hash);

        Assert.True(UpdateSignature.Verify(publicKey, "1.2.0", Hash, signature));
        Assert.False(UpdateSignature.Verify(publicKey, "1.3.0", Hash, signature)); // versão trocada
        Assert.False(UpdateSignature.Verify(publicKey, "1.2.0", Hash.Replace('a', 'b'), signature)); // arquivo trocado

        (_, string otherPublicKey) = UpdateSignature.CreateKeyPair();
        Assert.False(UpdateSignature.Verify(otherPublicKey, "1.2.0", Hash, signature)); // assinado por outra chave
        Assert.False(UpdateSignature.Verify(publicKey, "1.2.0", Hash, "isto não é base64"));
    }

    [Fact]
    public void EmbeddedPublicKeyIsValid()
    {
        (string privateKey, _) = UpdateSignature.CreateKeyPair();
        string signature = UpdateSignature.Sign(privateKey, "1.0.0", Hash);
        // A chave embutida é uma chave pública ECDSA válida (e, claro, não aceita uma assinatura de outra chave).
        Assert.False(UpdateSignature.Verify(UpdatePublicKey.Value, "1.0.0", Hash, signature));
        Assert.StartsWith("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE", UpdatePublicKey.Value);
    }

    [Theory]
    [InlineData("v1.2.3", true)]
    [InlineData("1.2.3", true)]
    [InlineData("v1.2", false)]
    [InlineData("v1.2.3-beta", false)]
    [InlineData("latest", false)]
    public void Version_ParsesTags(string tag, bool valid) => Assert.Equal(valid, AppVersion.TryParse(tag, out _));

    private static string ReleaseJson(string tag, string installerUrl, string sigUrl, bool prerelease = false) => $$"""
        {
          "tag_name": "{{tag}}", "draft": false, "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "https://github.com/GabrielAlvesB/Sharelink/releases/tag/{{tag}}", "body": "Novidades",
          "assets": [
            { "name": "Sharelink-Setup-1.4.0.exe", "browser_download_url": "{{installerUrl}}" },
            { "name": "Sharelink-Setup-1.4.0.exe.sig", "browser_download_url": "{{sigUrl}}" }
          ]
        }
        """;

    private const string Base = "https://github.com/GabrielAlvesB/Sharelink/releases/download/v1.4.0/";

    [Fact]
    public void Release_ParsesInstallerAndSignature()
    {
        ReleaseInfo? release = GitHubUpdateClient.ParseRelease(ReleaseJson("v1.4.0", Base + "Sharelink-Setup-1.4.0.exe", Base + "Sharelink-Setup-1.4.0.exe.sig"));
        Assert.NotNull(release);
        Assert.Equal(new Version(1, 4, 0), release.Version);
        Assert.Equal(Base + "Sharelink-Setup-1.4.0.exe", release.InstallerUrl.AbsoluteUri);
        Assert.Equal("Novidades", release.Notes);
    }

    [Theory]
    [InlineData("https://evil.com/Sharelink-Setup-1.4.0.exe")] // outro site
    [InlineData("http://github.com/GabrielAlvesB/Sharelink/releases/download/v1.4.0/Sharelink-Setup-1.4.0.exe")] // sem https
    [InlineData("https://github.com/OutraPessoa/Sharelink/releases/download/v1.4.0/Sharelink-Setup-1.4.0.exe")] // outro repositório
    public void Release_IgnoresAssetsOutsideThisRepository(string installerUrl) =>
        Assert.Null(GitHubUpdateClient.ParseRelease(ReleaseJson("v1.4.0", installerUrl, Base + "Sharelink-Setup-1.4.0.exe.sig")));

    [Fact]
    public void Release_IgnoresPrereleaseAndBadTags()
    {
        Assert.Null(GitHubUpdateClient.ParseRelease(ReleaseJson("v1.4.0", Base + "Sharelink-Setup-1.4.0.exe", Base + "Sharelink-Setup-1.4.0.exe.sig", prerelease: true)));
        Assert.Null(GitHubUpdateClient.ParseRelease(ReleaseJson("nova", Base + "Sharelink-Setup-1.4.0.exe", Base + "Sharelink-Setup-1.4.0.exe.sig")));
    }
}
