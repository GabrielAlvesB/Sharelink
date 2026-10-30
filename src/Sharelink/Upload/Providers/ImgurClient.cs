using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sharelink.Output;
using Sharelink.Settings;

namespace Sharelink.Upload.Providers;

/// <summary>
/// Envio anônimo para o Imgur (com o Client ID do seu app no Imgur).
/// Atenção: o arquivo fica num serviço de terceiros; apagar usa o "deletehash" que o Imgur devolve.
/// </summary>
internal sealed partial class ImgurClient(string clientId) : IUploader
{
    private const string ApiHost = "api.imgur.com";

    [GeneratedRegex("^[A-Za-z0-9]{8,40}$")]
    private static partial Regex ClientIdRegex();

    [GeneratedRegex("^[A-Za-z0-9]{5,64}$")]
    private static partial Regex DeleteHashRegex();

    public UploadDestination Destination => UploadDestination.Imgur;
    public string Name => "Imgur";

    public static bool IsValidClientId(string? value) => value is not null && ClientIdRegex().IsMatch(value.Trim());

    public static bool IsValidDeleteHash(string? value) => value is not null && DeleteHashRegex().IsMatch(value);

    public async Task<UploadResult> UploadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string contentType = ObjectKey.ContentTypeFor(filePath);
        bool isVideo = contentType == "video/mp4";

        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var fileContent = new StreamContent(file);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var form = new MultipartFormDataContent
        {
            // Nome aleatório: o nome original do arquivo (data/hora) não vai para o Imgur.
            { fileContent, isVideo ? "video" : "image", ObjectKey.Create(filePath) },
            { new StringContent("file"), "type" },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{ApiHost}/3/image") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", clientId);

        try
        {
            using HttpResponseMessage response = await SecureHttp.SendAsync(request, ApiHost, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new UploadResult(false, null, null, Describe(response.StatusCode));

            (string link, string deleteHash) = ParseUploadResponse(body);
            return new UploadResult(true, link, deleteHash);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new UploadResult(false, null, null, SecureHttp.DescribeNetworkError(ex));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new UploadResult(false, null, null, $"resposta inesperada do Imgur ({ex.Message}).");
        }
    }

    public async Task DeleteAsync(string remoteKey, CancellationToken cancellationToken = default)
    {
        if (!IsValidDeleteHash(remoteKey))
            throw new InvalidOperationException("Identificador de arquivo remoto inválido.");

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://{ApiHost}/3/image/{remoteKey}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", clientId);
        try
        {
            using HttpResponseMessage response = await SecureHttp.SendAsync(request, ApiHost, cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                throw new InvalidOperationException($"Não foi possível apagar do Imgur: {Describe(response.StatusCode)}");
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new InvalidOperationException($"Não foi possível apagar do Imgur: {SecureHttp.DescribeNetworkError(ex)}");
        }
    }

    public Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        ConnectionTest.RunAsync(this, cancellationToken);

    /// <summary>Lê o link e o deletehash da resposta, validando que o link é mesmo do Imgur.</summary>
    public static (string Link, string DeleteHash) ParseUploadResponse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement data = doc.RootElement.GetProperty("data");
        string link = data.GetProperty("link").GetString() ?? "";
        string deleteHash = data.GetProperty("deletehash").GetString() ?? "";

        if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.IdnHost.Equals("imgur.com", StringComparison.OrdinalIgnoreCase) ||
              uri.IdnHost.EndsWith(".imgur.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("link fora do domínio imgur.com");
        if (!IsValidDeleteHash(deleteHash))
            throw new InvalidOperationException("deletehash inválido");
        return (uri.AbsoluteUri, deleteHash);
    }

    private static string Describe(HttpStatusCode status) => (int)status switch
    {
        400 => "o Imgur recusou o arquivo (formato ou tamanho).",
        401 or 403 => "Client ID inválido.",
        429 => "limite de envios do Imgur atingido; tente mais tarde.",
        _ => $"o Imgur respondeu {(int)status}.",
    };
}
