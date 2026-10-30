using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sharelink.Output;
using Sharelink.Settings;

namespace Sharelink.Upload.Providers;

/// <summary>Destino WebDAV já validado.</summary>
/// <param name="Server">Nextcloud: endereço do Nextcloud. Genérico: a pasta WebDAV raiz.</param>
internal sealed record WebDavTarget(WebDavMode Mode, Uri Server, string User, string Password, string Folder, Uri? PublicBaseUrl)
{
    public string Host => Server.IdnHost;

    /// <summary>URL WebDAV da pasta de destino (termina em "/").</summary>
    public string FolderUrl
    {
        get
        {
            string root = Mode == WebDavMode.Nextcloud
                ? UrlRules.Combine(Server, $"remote.php/dav/files/{Uri.EscapeDataString(User)}")
                : Server.AbsoluteUri.TrimEnd('/');
            return $"{root}/{EncodeFolder(Folder)}/";
        }
    }

    public static string EncodeFolder(string folder) => string.Join('/', folder.Split('/').Select(Uri.EscapeDataString));

    public override string ToString() => $"WebDavTarget {{ {Mode}, {Server}, {User} }}";
}

/// <summary>Envio para Nextcloud (com link de compartilhamento público) ou WebDAV genérico.</summary>
internal sealed partial class WebDavClient(WebDavTarget target) : IUploader
{
    [GeneratedRegex(@"^(\d{1,12}:)?[A-Za-z0-9_-]{22}\.(png|jpg|gif|mp4)$")]
    private static partial Regex RemoteKeyRegex();

    public UploadDestination Destination => UploadDestination.WebDav;
    public string Name => target.Mode == WebDavMode.Nextcloud ? "Nextcloud" : "WebDAV";

    public static bool IsValidRemoteKey(string? key) => key is not null && RemoteKeyRegex().IsMatch(key);

    private AuthenticationHeaderValue Auth =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{target.User}:{target.Password}")));

    public async Task<UploadResult> UploadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string contentType = ObjectKey.ContentTypeFor(filePath);
        string key = ObjectKey.Create(filePath);
        try
        {
            await EnsureFolderAsync(cancellationToken);

            await using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                var content = new StreamContent(file);
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                using HttpResponseMessage put = await SendAsync(HttpMethod.Put, target.FolderUrl + key, content, cancellationToken);
                if (!put.IsSuccessStatusCode)
                    return new UploadResult(false, null, null, Describe(put.StatusCode));
            }

            if (target.Mode == WebDavMode.Generic)
                return new UploadResult(true, UrlRules.Combine(target.PublicBaseUrl!, key), key);

            (string shareId, string url) = await CreateShareAsync(key, cancellationToken);
            return new UploadResult(true, url, $"{shareId}:{key}");
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new UploadResult(false, null, null, SecureHttp.DescribeNetworkError(ex));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException)
        {
            return new UploadResult(false, null, null, ex.Message);
        }
    }

    public async Task DeleteAsync(string remoteKey, CancellationToken cancellationToken = default)
    {
        if (!IsValidRemoteKey(remoteKey))
            throw new InvalidOperationException("Identificador de arquivo remoto inválido.");

        string[] parts = remoteKey.Split(':');
        string key = parts[^1];
        try
        {
            if (parts.Length == 2)
            {
                using HttpResponseMessage unshare = await SendAsync(HttpMethod.Delete,
                    UrlRules.Combine(target.Server, $"ocs/v2.php/apps/files_sharing/api/v1/shares/{parts[0]}?format=json"), null, cancellationToken, ocs: true);
                if (!unshare.IsSuccessStatusCode && unshare.StatusCode != HttpStatusCode.NotFound)
                    throw new InvalidOperationException($"Não foi possível remover o compartilhamento: {Describe(unshare.StatusCode)}");
            }

            using HttpResponseMessage delete = await SendAsync(HttpMethod.Delete, target.FolderUrl + key, null, cancellationToken);
            if (!delete.IsSuccessStatusCode && delete.StatusCode != HttpStatusCode.NotFound)
                throw new InvalidOperationException($"Não foi possível apagar o arquivo: {Describe(delete.StatusCode)}");
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new InvalidOperationException($"Não foi possível apagar: {SecureHttp.DescribeNetworkError(ex)}");
        }
    }

    public Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        ConnectionTest.RunAsync(this, cancellationToken);

    /// <summary>Cria a pasta (e as intermediárias) se não existir. 405 = já existe.</summary>
    private async Task EnsureFolderAsync(CancellationToken ct)
    {
        string root = target.Mode == WebDavMode.Nextcloud
            ? UrlRules.Combine(target.Server, $"remote.php/dav/files/{Uri.EscapeDataString(target.User)}")
            : target.Server.AbsoluteUri.TrimEnd('/');

        string path = root;
        foreach (string segment in target.Folder.Split('/'))
        {
            path += "/" + Uri.EscapeDataString(segment);
            using HttpResponseMessage response = await SendAsync(new HttpMethod("MKCOL"), path + "/", null, ct);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MethodNotAllowed)
                throw new InvalidOperationException($"não foi possível criar a pasta \"{target.Folder}\": {Describe(response.StatusCode)}");
        }
    }

    private async Task<(string Id, string Url)> CreateShareAsync(string key, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["path"] = $"/{target.Folder}/{key}",
            ["shareType"] = "3", // link público
            ["permissions"] = "1", // somente leitura
        });
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post,
            UrlRules.Combine(target.Server, "ocs/v2.php/apps/files_sharing/api/v1/shares?format=json"), form, ct, ocs: true);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"o arquivo foi enviado, mas o Nextcloud não criou o link: {Describe(response.StatusCode)}");
        return ParseShareResponse(body);
    }

    /// <summary>Lê o id e a URL do compartilhamento criado (a URL precisa ser https).</summary>
    public static (string Id, string Url) ParseShareResponse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement data = doc.RootElement.GetProperty("ocs").GetProperty("data");
        JsonElement idElement = data.GetProperty("id");
        string id = idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt64().ToString() : idElement.GetString() ?? "";
        string url = data.GetProperty("url").GetString() ?? "";

        if (!Regex.IsMatch(id, @"^\d{1,12}$"))
            throw new InvalidOperationException("resposta do Nextcloud com id inválido.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("o Nextcloud devolveu um link que não é https.");
        return (id, uri.AbsoluteUri);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct, bool ocs = false)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = Auth;
        if (ocs)
        {
            request.Headers.TryAddWithoutValidation("OCS-APIRequest", "true");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        return await SecureHttp.SendAsync(request, target.Host, ct);
    }

    private static string Describe(HttpStatusCode status) => (int)status switch
    {
        401 => "usuário ou senha de aplicativo incorretos.",
        403 => "sem permissão nessa pasta.",
        404 => "endereço não encontrado (confira a URL do servidor).",
        507 => "sem espaço no servidor.",
        _ => $"o servidor respondeu {(int)status}.",
    };
}
