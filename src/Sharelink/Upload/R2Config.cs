using System.Text.RegularExpressions;
using Sharelink.Settings;
using Sharelink.Upload.Providers;

namespace Sharelink.Upload;

/// <summary>Configuração do Cloudflare R2 (um S3 compatível com endpoint fixo da Cloudflare).</summary>
internal static partial class R2Config
{
    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.IgnoreCase)]
    private static partial Regex AccountIdRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")]
    private static partial Regex BucketRegex();

    public static bool IsValidAccountId(string? value) => value is not null && AccountIdRegex().IsMatch(value);

    /// <summary>
    /// O host é sempre da Cloudflare: as credenciais nunca vão para um endereço digitado pelo usuário.
    /// </summary>
    public static Uri EndpointFor(string accountId) => new($"https://{accountId.ToLowerInvariant()}.r2.cloudflarestorage.com");

    public static bool TryCreate(string? accountId, string? bucket, string? publicUrl, string? accessKeyId, string? secret,
        out S3Target? target, out string error)
    {
        target = null;
        if (!IsValidAccountId(accountId))
            error = "Account ID inválido (são 32 letras/números, aparece no painel do R2).";
        else if (bucket is null || !BucketRegex().IsMatch(bucket))
            error = "Nome do bucket inválido (letras minúsculas, números e hífen).";
        else if (!UrlRules.TryParseHttps(publicUrl, allowPath: true, out Uri url))
            error = "URL pública inválida: precisa começar com https:// (ex.: https://pub-xxxx.r2.dev).";
        else if (!S3Config.IsValidAccessKeyId(accessKeyId))
            error = "Access Key ID inválido.";
        else if (string.IsNullOrEmpty(secret))
            error = "Secret Access Key não informada.";
        else
        {
            target = new S3Target(UploadDestination.CloudflareR2, "Cloudflare R2", EndpointFor(accountId!), "auto",
                bucket, PathStyle: true, url, accessKeyId!.Trim(), secret, PublicReadAcl: false);
            error = "";
            return true;
        }
        return false;
    }

    public static bool TryLoad(AppSettings settings, out S3Target? target, out string error)
    {
        var credential = CredentialStore.Load(CredentialStore.R2Target);
        return TryCreate(settings.R2AccountId, settings.R2Bucket, settings.R2PublicBaseUrl,
            credential?.UserName, credential?.Secret, out target, out error);
    }
}

/// <summary>
/// Supabase Storage pelo acesso compatível com S3. Usa as "S3 access keys" do Storage
/// (só armazenamento), nunca a chave service_role, que daria acesso ao banco de dados inteiro.
/// </summary>
internal static partial class SupabaseConfig
{
    [GeneratedRegex("^[a-z0-9]{15,40}$")]
    private static partial Regex ProjectRefRegex();

    [GeneratedRegex("^[a-z]{2}-[a-z]+-[0-9]$")]
    private static partial Regex RegionRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$")]
    private static partial Regex BucketRegex();

    /// <summary>Host da API de armazenamento: sempre um subdomínio do Supabase.</summary>
    public static Uri EndpointFor(string projectRef) => new($"https://{projectRef}.storage.supabase.co");

    public static Uri PublicBaseFor(string projectRef, string bucket) =>
        new($"https://{projectRef}.supabase.co/storage/v1/object/public/{Uri.EscapeDataString(bucket)}");

    public static bool TryCreate(string? projectRef, string? region, string? bucket, string? accessKeyId, string? secret,
        out S3Target? target, out string error)
    {
        target = null;
        projectRef = projectRef?.Trim().ToLowerInvariant();
        if (projectRef is null || !ProjectRefRegex().IsMatch(projectRef))
            error = "Project ref inválido (é o código do projeto, aparece na URL: https://SEU-CODIGO.supabase.co).";
        else if (region is null || !RegionRegex().IsMatch(region.Trim()))
            error = "Região inválida (ex.: sa-east-1, us-east-1). Aparece em Storage › S3 Connection.";
        else if (bucket is null || !BucketRegex().IsMatch(bucket.Trim()))
            error = "Nome do bucket inválido.";
        else if (!S3Config.IsValidAccessKeyId(accessKeyId))
            error = "Access Key ID inválido.";
        else if (string.IsNullOrEmpty(secret))
            error = "Secret Access Key não informada.";
        else
        {
            bucket = bucket.Trim();
            target = new S3Target(UploadDestination.Supabase, "Supabase", EndpointFor(projectRef), region.Trim(), bucket,
                PathStyle: true, PublicBaseFor(projectRef, bucket), accessKeyId!.Trim(), secret, PublicReadAcl: false,
                BasePath: "/storage/v1/s3");
            error = "";
            return true;
        }
        return false;
    }

    public static bool TryLoad(AppSettings settings, out S3Target? target, out string error)
    {
        var credential = CredentialStore.Load(CredentialStore.SupabaseTarget);
        return TryCreate(settings.SupabaseProjectRef, settings.SupabaseRegion, settings.SupabaseBucket,
            credential?.UserName, credential?.Secret, out target, out error);
    }
}

/// <summary>Configuração de qualquer serviço compatível com S3 (AWS, Backblaze B2, Wasabi, Spaces, MinIO...).</summary>
internal static partial class S3Config
{
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$")]
    private static partial Regex BucketRegex();

    [GeneratedRegex("^[a-z0-9-]{2,32}$")]
    private static partial Regex RegionRegex();

    [GeneratedRegex("^[A-Za-z0-9._+=-]{3,128}$")]
    private static partial Regex AccessKeyRegex();

    public static bool IsValidAccessKeyId(string? value) => value is not null && AccessKeyRegex().IsMatch(value.Trim());

    public static bool TryCreate(string? endpoint, string? region, string? bucket, string? publicUrl, bool pathStyle, bool publicReadAcl,
        string? accessKeyId, string? secret, out S3Target? target, out string error)
    {
        target = null;
        if (!UrlRules.TryParseHttps(endpoint, allowPath: false, out Uri endpointUrl))
            error = "Endpoint inválido: use só o endereço https do serviço, sem caminho (ex.: https://s3.us-west-004.backblazeb2.com).";
        else if (region is null || !RegionRegex().IsMatch(region))
            error = "Região inválida (ex.: us-east-1, us-west-004).";
        else if (bucket is null || !BucketRegex().IsMatch(bucket))
            error = "Nome do bucket inválido.";
        else if (!pathStyle && bucket.Contains('.'))
            error = "Bucket com ponto no nome precisa da opção \"path-style\" ligada.";
        else if (!UrlRules.TryParseHttps(publicUrl, allowPath: true, out Uri url))
            error = "URL pública inválida: precisa começar com https://.";
        else if (!IsValidAccessKeyId(accessKeyId))
            error = "Access Key ID inválido.";
        else if (string.IsNullOrEmpty(secret))
            error = "Secret Access Key não informada.";
        else
        {
            target = new S3Target(UploadDestination.S3Compatible, "S3 compatível", endpointUrl, region, bucket, pathStyle, url,
                accessKeyId!.Trim(), secret, publicReadAcl);
            error = "";
            return true;
        }
        return false;
    }

    public static bool TryLoad(AppSettings settings, out S3Target? target, out string error)
    {
        var credential = CredentialStore.Load(CredentialStore.S3Target);
        return TryCreate(settings.S3Endpoint, settings.S3Region, settings.S3Bucket, settings.S3PublicBaseUrl, settings.S3PathStyle,
            settings.S3PublicReadAcl, credential?.UserName, credential?.Secret, out target, out error);
    }
}
