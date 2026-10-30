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

    /// <summary>
    /// Baixa um arquivo público para o disco. Segue redirecionamentos SOMENTE para hosts da lista
    /// (ex.: github.com → servidor de arquivos do GitHub), no máximo 5 vezes, e corta acima de <paramref name="maxBytes"/>.
    /// </summary>
    public static async Task DownloadToFileAsync(Uri url, IReadOnlyCollection<string> allowedHosts, string destination, long maxBytes,
        string userAgent, CancellationToken ct)
    {
        Uri current = url;
        for (int hop = 0; hop <= 5; hop++)
        {
            if (!allowedHosts.Contains(current.IdnHost, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Bloqueado: download a partir de host não permitido ({current.Host}).");
            EnsureAllowed(current, current.IdnHost);

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(userAgent);
            using HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is Uri location)
            {
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"o servidor respondeu {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > maxBytes)
                throw new InvalidOperationException("arquivo maior que o permitido.");

            string temp = destination + ".parcial";
            await using (Stream source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                        throw new InvalidOperationException("arquivo maior que o permitido.");
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            File.Move(temp, destination, overwrite: true);
            return;
        }
        throw new InvalidOperationException("redirecionamentos demais.");
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
