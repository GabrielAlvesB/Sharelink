// Rede: consulta as releases públicas do Sharelink no GitHub (sem nenhuma credencial).
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Sharelink.Upload.Providers;

namespace Sharelink.Update;

internal sealed record ReleaseInfo(Version Version, string Notes, Uri Page, Uri InstallerUrl, Uri SignatureUrl)
{
    public string VersionText => AppVersion.Format(Version);
}

internal static class GitHubUpdateClient
{
    public const string Owner = "GabrielAlvesB";
    public const string Repository = "Sharelink";

    private const string ApiHost = "api.github.com";

    /// <summary>De onde os arquivos das releases podem vir (o GitHub redireciona para o servidor de arquivos).</summary>
    public static readonly string[] DownloadHosts = ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    public static string InstallerName(string version) => $"Sharelink-Setup-{version}.exe";

    private static string UserAgent => $"Sharelink/{AppVersion.CurrentText}";

    /// <returns>A release mais recente, ou null se ainda não houver nenhuma.</returns>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{ApiHost}/repos/{Owner}/{Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using HttpResponseMessage response = await SecureHttp.SendAsync(request, ApiHost, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"o GitHub respondeu {(int)response.StatusCode}.");
        return ParseRelease(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// Lê a resposta da API. Só aceita tag no formato vX.Y.Z e arquivos publicados dentro
    /// das releases deste repositório.
    /// </summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean())
            return null;
        if (root.TryGetProperty("prerelease", out JsonElement pre) && pre.GetBoolean())
            return null;
        if (!AppVersion.TryParse(root.GetProperty("tag_name").GetString(), out Version version))
            return null;

        string versionText = AppVersion.Format(version);
        Uri? installer = null, signature = null;
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out Uri? url) || !IsReleaseAssetUrl(url))
                continue;
            if (name == InstallerName(versionText))
                installer = url;
            else if (name == InstallerName(versionText) + ".sig")
                signature = url;
        }
        if (installer is null || signature is null)
            return null;

        string notes = root.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? "" : "";
        Uri page = Uri.TryCreate(root.GetProperty("html_url").GetString(), UriKind.Absolute, out Uri? html) && IsReleaseAssetUrl(html, pagesToo: true)
            ? html
            : new Uri($"https://github.com/{Owner}/{Repository}/releases");
        return new ReleaseInfo(version, notes, page, installer, signature);
    }

    private static bool IsReleaseAssetUrl(Uri url, bool pagesToo = false) =>
        url.Scheme == Uri.UriSchemeHttps &&
        url.IdnHost.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        url.AbsolutePath.StartsWith(pagesToo ? $"/{Owner}/{Repository}/releases/" : $"/{Owner}/{Repository}/releases/download/", StringComparison.Ordinal);

    public static Task DownloadAsync(Uri url, string destination, long maxBytes, CancellationToken ct) =>
        SecureHttp.DownloadToFileAsync(url, DownloadHosts, destination, maxBytes, UserAgent, ct);
}
