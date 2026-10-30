using System.Text.RegularExpressions;
using Sharelink.Output;
using Sharelink.Settings;
using Sharelink.Upload.Providers;

namespace Sharelink.Upload;

/// <summary>Monta o destino de upload escolhido a partir das configurações + Gerenciador de Credenciais.</summary>
internal static partial class UploaderFactory
{
    [GeneratedRegex(@"^[A-Za-z0-9 _.-]+(/[A-Za-z0-9 _.-]+)*$")]
    private static partial Regex FolderRegex();

    public static string DisplayName(UploadDestination destination) => destination switch
    {
        UploadDestination.CloudflareR2 => "Cloudflare R2",
        UploadDestination.S3Compatible => "S3 compatível",
        UploadDestination.Imgur => "Imgur",
        UploadDestination.WebDav => "Nextcloud / WebDAV",
        UploadDestination.Supabase => "Supabase Storage",
        _ => destination.ToString(),
    };

    /// <summary>Destinos onde o arquivo fica num serviço que você não controla.</summary>
    public static bool IsThirdParty(UploadDestination destination) => destination == UploadDestination.Imgur;

    public static bool TryCreate(UploadDestination destination, AppSettings settings, out IUploader? uploader, out string error)
    {
        uploader = null;
        switch (destination)
        {
            case UploadDestination.CloudflareR2:
                if (!R2Config.TryLoad(settings, out S3Target? r2, out error))
                    return false;
                uploader = new S3Client(r2!);
                return true;

            case UploadDestination.S3Compatible:
                if (!S3Config.TryLoad(settings, out S3Target? s3, out error))
                    return false;
                uploader = new S3Client(s3!);
                return true;

            case UploadDestination.Supabase:
                if (!SupabaseConfig.TryLoad(settings, out S3Target? supabase, out error))
                    return false;
                uploader = new S3Client(supabase!);
                return true;

            case UploadDestination.Imgur:
                var imgur = CredentialStore.Load(CredentialStore.ImgurTarget);
                if (!TryCreateImgur(imgur?.Secret, out ImgurClient? client, out error))
                    return false;
                uploader = client;
                return true;

            case UploadDestination.WebDav:
                var dav = CredentialStore.Load(CredentialStore.WebDavTarget);
                if (!TryCreateWebDav(settings.WebDavMode, settings.WebDavServer, dav?.UserName, dav?.Secret, settings.WebDavFolder,
                        settings.WebDavPublicBaseUrl, out WebDavTarget? target, out error))
                    return false;
                uploader = new WebDavClient(target!);
                return true;

            default:
                error = "Destino desconhecido.";
                return false;
        }
    }

    public static bool TryCreateImgur(string? clientId, out ImgurClient? client, out string error)
    {
        client = null;
        if (!ImgurClient.IsValidClientId(clientId))
        {
            error = "Client ID do Imgur inválido ou não informado.";
            return false;
        }
        client = new ImgurClient(clientId!.Trim());
        error = "";
        return true;
    }

    public static bool TryCreateWebDav(WebDavMode mode, string? server, string? user, string? password, string? folder, string? publicUrl,
        out WebDavTarget? target, out string error)
    {
        target = null;
        Uri? publicBase = null;
        folder = folder?.Trim().Trim('/');

        if (!UrlRules.TryParseHttps(server, allowPath: true, out Uri serverUrl))
            error = "Endereço do servidor inválido: precisa começar com https://.";
        else if (string.IsNullOrWhiteSpace(user) || user.Contains(':') || user.Any(char.IsControl) || user.Length > 128)
            error = "Usuário inválido.";
        else if (string.IsNullOrEmpty(password))
            error = "Senha (de aplicativo) não informada.";
        else if (string.IsNullOrEmpty(folder) || !FolderRegex().IsMatch(folder) || folder.Split('/').Any(s => s.Trim('.').Length == 0))
            error = "Pasta inválida (use letras, números, espaço, ponto, hífen; ex.: Sharelink ou Fotos/Prints).";
        else if (mode == WebDavMode.Generic && !UrlRules.TryParseHttps(publicUrl, allowPath: true, out publicBase!))
            error = "No WebDAV genérico, informe a URL pública https onde a pasta fica acessível.";
        else
        {
            target = new WebDavTarget(mode, serverUrl, user.Trim(), password, folder, publicBase);
            error = "";
            return true;
        }
        return false;
    }

    /// <summary>Só aceita apagar identificadores no formato que o próprio Sharelink gera para aquele destino.</summary>
    public static bool IsValidRemoteKey(UploadDestination destination, string? key) => destination switch
    {
        UploadDestination.CloudflareR2 or UploadDestination.S3Compatible or UploadDestination.Supabase => key is not null && ObjectKey.IsValid(key),
        UploadDestination.Imgur => ImgurClient.IsValidDeleteHash(key),
        UploadDestination.WebDav => WebDavClient.IsValidRemoteKey(key),
        _ => false,
    };
}
