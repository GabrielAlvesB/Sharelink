using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Sharelink.Upload;

/// <summary>
/// Assinatura AWS Signature Version 4 (usada pela API S3 do Cloudflare R2).
/// Implementada só com o .NET para não depender de SDK de terceiros.
/// </summary>
internal static class SigV4Signer
{
    public const string Algorithm = "AWS4-HMAC-SHA256";
    public const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    internal sealed record Signature(string Value, string SignedHeaders, string CredentialScope, string AuthorizationHeader);

    public static string AmzDate(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <param name="headers">Cabeçalhos a assinar (precisa incluir host, x-amz-date e x-amz-content-sha256).</param>
    public static Signature Sign(
        string method, string canonicalUri, string canonicalQuery,
        IReadOnlyDictionary<string, string> headers, string payloadHash, DateTime utcNow,
        string region, string service, string accessKeyId, string secretAccessKey)
    {
        var sorted = headers
            .Select(h => (Name: h.Key.Trim().ToLowerInvariant(), Value: h.Value.Trim()))
            .OrderBy(h => h.Name, StringComparer.Ordinal)
            .ToList();
        string canonicalHeaders = string.Concat(sorted.Select(h => $"{h.Name}:{h.Value}\n"));
        string signedHeaders = string.Join(';', sorted.Select(h => h.Name));

        string canonicalRequest = string.Join('\n', method, canonicalUri, canonicalQuery, canonicalHeaders, signedHeaders, payloadHash);

        string date = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string scope = $"{date}/{region}/{service}/aws4_request";
        string stringToSign = string.Join('\n', Algorithm, AmzDate(utcNow), scope, Sha256Hex(Encoding.UTF8.GetBytes(canonicalRequest)));

        byte[] key = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), Encoding.UTF8.GetBytes(date));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(region));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(service));
        key = HMACSHA256.HashData(key, "aws4_request"u8.ToArray());
        string signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(stringToSign)));

        string authorization = $"{Algorithm} Credential={accessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
        return new Signature(signature, signedHeaders, scope, authorization);
    }

    public static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static async Task<string> Sha256HexAsync(Stream stream, CancellationToken ct) =>
        Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));

    /// <summary>Codificação de caminho exigida pelo SigV4 (cada segmento, preservando "/").</summary>
    public static string EncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
