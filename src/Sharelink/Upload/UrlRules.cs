namespace Sharelink.Upload;

/// <summary>Validação dos endereços digitados nas configurações.</summary>
internal static class UrlRules
{
    /// <summary>
    /// Endereço https sem usuário/senha embutidos, sem query e sem fragmento.
    /// </summary>
    /// <param name="allowPath">Se false, só aceita a raiz (ex.: endpoint S3).</param>
    public static bool TryParseHttps(string? value, bool allowPath, out Uri url)
    {
        url = null!;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out Uri? parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            (!allowPath && parsed.AbsolutePath != "/"))
            return false;
        url = parsed;
        return true;
    }

    /// <summary>Junta base + caminho sem barras duplicadas.</summary>
    public static string Combine(Uri baseUrl, string path) => $"{baseUrl.AbsoluteUri.TrimEnd('/')}/{path.TrimStart('/')}";
}
