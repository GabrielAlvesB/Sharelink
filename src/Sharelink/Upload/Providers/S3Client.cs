using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Sharelink.Output;
using Sharelink.Settings;

namespace Sharelink.Upload.Providers;

/// <summary>Destino S3 já validado (Cloudflare R2 ou outro serviço compatível).</summary>
/// <param name="BasePath">Caminho da API S3 no servidor, quando não fica na raiz (ex.: "/storage/v1/s3" no Supabase).</param>
internal sealed record S3Target(
    UploadDestination Destination, string Name, Uri Endpoint, string Region, string Bucket, bool PathStyle,
    Uri PublicBaseUrl, string AccessKeyId, string SecretAccessKey, bool PublicReadAcl, string BasePath = "")
{
    /// <summary>Endereço do objeto na API (path-style: host/bucket/chave; virtual: bucket.host/chave).</summary>
    public Uri ObjectUri(string key) => PathStyle
        ? new Uri($"https://{Endpoint.Authority}{BasePath}/{SigV4Signer.EncodePath($"{Bucket}/{key}")}")
        : new Uri($"https://{Bucket}.{Endpoint.Authority}{BasePath}/{SigV4Signer.EncodePath(key)}");

    public string PublicUrlFor(string key) => UrlRules.Combine(PublicBaseUrl, key);

    // Não deixa o segredo aparecer em logs ou mensagens por acidente.
    public override string ToString() => $"S3Target {{ {Name}, Bucket = {Bucket}, Endpoint = {Endpoint} }}";
}

/// <summary>Envio e remoção via API S3 com assinatura AWS SigV4.</summary>
internal sealed partial class S3Client(S3Target target) : IUploader
{
    private const string Service = "s3";

    [GeneratedRegex("<Code>([A-Za-z]+)</Code>")]
    private static partial Regex ErrorCodeRegex();

    public UploadDestination Destination => target.Destination;
    public string Name => target.Name;

    public async Task<UploadResult> UploadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string contentType = ObjectKey.ContentTypeFor(filePath);
        string key = ObjectKey.Create(filePath);

        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        string payloadHash = await SigV4Signer.Sha256HexAsync(file, cancellationToken);
        file.Position = 0;

        var content = new StreamContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Headers.TryAddWithoutValidation("Cache-Control", "public, max-age=31536000, immutable");

        var signed = new Dictionary<string, string> { ["content-type"] = contentType };
        if (target.PublicReadAcl)
            signed["x-amz-acl"] = "public-read";

        string? error = await SendAsync(HttpMethod.Put, key, content, payloadHash, signed, cancellationToken);
        return error is null
            ? new UploadResult(true, target.PublicUrlFor(key), key)
            : new UploadResult(false, null, null, error);
    }

    public async Task DeleteAsync(string remoteKey, CancellationToken cancellationToken = default)
    {
        if (!ObjectKey.IsValid(remoteKey))
            throw new InvalidOperationException("Identificador de arquivo remoto inválido.");

        string? error = await SendAsync(HttpMethod.Delete, remoteKey, null, SigV4Signer.EmptyPayloadHash, [], cancellationToken);
        if (error is not null)
            throw new InvalidOperationException($"Não foi possível apagar o link: {error}");
    }

    public Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        ConnectionTest.RunAsync(this, cancellationToken);

    /// <returns>null se deu certo; senão, uma mensagem de erro (nunca contém as chaves).</returns>
    private async Task<string?> SendAsync(HttpMethod method, string key, HttpContent? content, string payloadHash,
        Dictionary<string, string> extraSignedHeaders, CancellationToken cancellationToken)
    {
        Uri uri = target.ObjectUri(key);
        DateTime now = DateTime.UtcNow;
        string amzDate = SigV4Signer.AmzDate(now);

        var headers = new Dictionary<string, string>(extraSignedHeaders)
        {
            ["host"] = uri.Authority,
            ["x-amz-date"] = amzDate,
            ["x-amz-content-sha256"] = payloadHash,
        };
        SigV4Signer.Signature signature = SigV4Signer.Sign(method.Method, uri.AbsolutePath, "", headers, payloadHash, now,
            target.Region, Service, target.AccessKeyId, target.SecretAccessKey);

        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        if (extraSignedHeaders.TryGetValue("x-amz-acl", out string? acl))
            request.Headers.TryAddWithoutValidation("x-amz-acl", acl);
        request.Headers.TryAddWithoutValidation("Authorization", signature.AuthorizationHeader);

        try
        {
            using HttpResponseMessage response = await SecureHttp.SendAsync(request, uri.IdnHost, cancellationToken);
            if (response.IsSuccessStatusCode || (method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound))
                return null;

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            Match code = ErrorCodeRegex().Match(body);
            return Describe(response.StatusCode, code.Success ? code.Groups[1].Value : null);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return SecureHttp.DescribeNetworkError(ex);
        }
    }

    private static string Describe(HttpStatusCode status, string? code) => code switch
    {
        "SignatureDoesNotMatch" or "InvalidAccessKeyId" or "Unauthorized" => "chaves de acesso inválidas (confira o Access Key ID e a Secret).",
        "AccessDenied" => "a chave não tem permissão de escrita neste bucket.",
        "NoSuchBucket" => "o bucket não existe (confira o nome).",
        "AuthorizationHeaderMalformed" => "região incorreta para este bucket.",
        "AccessControlListNotSupported" => "este bucket não aceita ACL: desligue \"tornar público via ACL\".",
        "RequestTimeTooSkewed" => "o relógio do computador está errado; ajuste a data/hora do Windows.",
        _ => $"o servidor respondeu {(int)status}{(code is null ? "" : $" ({code})")}.",
    };
}

/// <summary>Teste de conexão comum a todos os destinos.</summary>
internal static class ConnectionTest
{
    // GIF transparente de 1×1 pixel.
    private const string TinyGif = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    public static async Task<(bool Ok, string Message)> RunAsync(IUploader uploader, CancellationToken ct)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"sharelink_teste_{Guid.NewGuid():N}.gif");
        await File.WriteAllBytesAsync(temp, Convert.FromBase64String(TinyGif), ct);
        try
        {
            UploadResult upload = await uploader.UploadAsync(temp, ct);
            if (!upload.Success || upload.Link is null || upload.RemoteKey is null)
                return (false, $"Falha no envio: {upload.Error}");

            try
            {
                int status = await SecureHttp.ProbePublicUrlAsync(new Uri(upload.Link), ct);
                return status is >= 200 and < 300
                    ? (true, "Tudo certo: envio, link público e remoção funcionando.")
                    : (false, $"O envio funcionou, mas o link público respondeu {status}. Confira se o acesso público está ativado e se a URL pública está correta.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                return (false, $"O envio funcionou, mas o link público não abriu: {SecureHttp.DescribeNetworkError(ex)}");
            }
            finally
            {
                await uploader.DeleteAsync(upload.RemoteKey, ct);
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
