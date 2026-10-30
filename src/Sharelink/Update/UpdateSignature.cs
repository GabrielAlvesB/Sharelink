using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sharelink.Update;

/// <summary>Conteúdo do arquivo .sig publicado junto com cada instalador.</summary>
internal sealed record SignatureFile(string Version, string Sha256, string Signature);

/// <summary>
/// Assinatura das atualizações com ECDSA P-256. A chave privada fica só no PC de quem publica
/// (Gerenciador de Credenciais); o app traz apenas a chave pública. Assim, nem quem invadir
/// o GitHub consegue publicar uma atualização que o Sharelink aceite.
/// </summary>
internal static class UpdateSignature
{
    private const DSASignatureFormat Format = DSASignatureFormat.IeeeP1363FixedFieldConcatenation;

    /// <summary>A assinatura cobre a versão e o hash: não dá para reaproveitar um instalador antigo como "novo".</summary>
    public static byte[] Message(string version, string sha256Hex) =>
        Encoding.UTF8.GetBytes($"Sharelink-update|v1|{version}|{sha256Hex.ToLowerInvariant()}");

    public static async Task<string> FileSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    public static string Sign(string privateKeyPkcs8Base64, string version, string sha256Hex)
    {
        using ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPkcs8Base64), out _);
        return Convert.ToBase64String(ecdsa.SignData(Message(version, sha256Hex), HashAlgorithmName.SHA256, Format));
    }

    public static bool Verify(string publicKeySpkiBase64, string version, string sha256Hex, string signatureBase64)
    {
        try
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64), out _);
            return ecdsa.VerifyData(Message(version, sha256Hex), Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256, Format);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Gera um par de chaves novo: (privada PKCS#8, pública SPKI), ambas em base64.</summary>
    public static (string PrivateKey, string PublicKey) CreateKeyPair()
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    public static string Serialize(SignatureFile file) => JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });

    public static SignatureFile? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SignatureFile>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
