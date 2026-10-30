using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Sharelink.Upload;

/// <summary>Nomes dos arquivos na nuvem: aleatórios (128 bits) e só de tipos seguros.</summary>
internal static partial class ObjectKey
{
    /// <summary>
    /// Só imagens e vídeos. Nunca HTML/SVG/JS: um arquivo desses no seu domínio
    /// poderia executar código no navegador de quem abrir o link.
    /// </summary>
    private static readonly Dictionary<string, (string Extension, string ContentType)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = (".png", "image/png"),
        [".jpg"] = (".jpg", "image/jpeg"),
        [".jpeg"] = (".jpg", "image/jpeg"),
        [".gif"] = (".gif", "image/gif"),
        [".mp4"] = (".mp4", "video/mp4"),
    };

    [GeneratedRegex(@"^[A-Za-z0-9_-]{22}\.(png|jpg|gif|mp4)$")]
    private static partial Regex KeyRegex();

    public static bool IsAllowedFile(string path) => Allowed.ContainsKey(Path.GetExtension(path));

    public static string ContentTypeFor(string path) =>
        Allowed.TryGetValue(Path.GetExtension(path), out var info)
            ? info.ContentType
            : throw new InvalidOperationException($"Tipo de arquivo não permitido para link: {Path.GetExtension(path)}");

    /// <summary>Ex.: "Qm3xY7...k9w.png". 16 bytes aleatórios = impossível de adivinhar.</summary>
    public static string Create(string path)
    {
        if (!Allowed.TryGetValue(Path.GetExtension(path), out var info))
            throw new InvalidOperationException($"Tipo de arquivo não permitido para link: {Path.GetExtension(path)}");

        Span<byte> random = stackalloc byte[16];
        RandomNumberGenerator.Fill(random);
        return Base64Url.EncodeToString(random) + info.Extension;
    }

    /// <summary>Só aceita apagar chaves no formato que o próprio Sharelink gera.</summary>
    public static bool IsValid(string key) => KeyRegex().IsMatch(key);
}
