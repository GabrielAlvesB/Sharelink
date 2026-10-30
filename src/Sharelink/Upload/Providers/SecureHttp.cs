// A pasta Upload/Providers é a ÚNICA parte do Sharelink com acesso à internet.
using System.Net.Http;

namespace Sharelink.Upload.Providers;

/// <summary>
/// Cliente HTTP compartilhado e "travado": só HTTPS, sem redirecionamentos, sem cookies,
/// e cada requisição só sai se for exatamente para o host esperado.
/// </summary>
internal static class SecureHttp
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    /// <summary>Lança exceção se o endereço não for https://{expectedHost}.</summary>
    public static void EnsureAllowed(Uri uri, string expectedHost)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Bloqueado: só conexões HTTPS são permitidas.");
        if (!string.Equals(uri.IdnHost, expectedHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Bloqueado: destino inesperado ({uri.Host}).");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Bloqueado: endereço com usuário/senha embutidos.");
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string expectedHost, CancellationToken ct)
    {
        EnsureAllowed(request.RequestUri ?? throw new InvalidOperationException("Requisição sem endereço."), expectedHost);
        return await Client.SendAsync(request, ct);
    }

    /// <summary>Confere se um link público responde (GET sem nenhuma credencial).</summary>
    public static async Task<int> ProbePublicUrlAsync(Uri url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await SendAsync(request, url.IdnHost, ct);
        return (int)response.StatusCode;
    }

    /// <summary>Mensagem amigável para falhas de rede (nunca inclui credenciais).</summary>
    public static string DescribeNetworkError(Exception ex) => ex switch
    {
        HttpRequestException http => $"sem conexão com o servidor ({http.HttpRequestError}).",
        TaskCanceledException => "tempo esgotado.",
        _ => ex.Message,
    };
}
