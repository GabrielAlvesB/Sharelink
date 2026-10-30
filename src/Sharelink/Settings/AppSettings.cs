namespace Sharelink.Settings;

public enum ImageFormatOption
{
    Png,
    Jpeg,
}

public enum ThemeMode
{
    /// <summary>Segue o modo claro/escuro do Windows.</summary>
    System,
    Dark,
    Light,
}

/// <summary>Para onde vão os arquivos quando você pede "Gerar link".</summary>
public enum UploadDestination
{
    CloudflareR2,
    S3Compatible,
    Imgur,
    WebDav,
    Supabase,
}

public enum WebDavMode
{
    /// <summary>Nextcloud/ownCloud: envia e cria um link de compartilhamento público.</summary>
    Nextcloud,
    /// <summary>WebDAV genérico: o link é montado a partir de uma URL pública informada.</summary>
    Generic,
}

public enum ClipboardAction
{
    /// <summary>Não copia nada.</summary>
    None,
    /// <summary>Copia a imagem (colar direto no Discord, WhatsApp, Word...).</summary>
    Image,
    /// <summary>Copia o caminho do arquivo no disco. (O link público é copiado sempre que você pede "gerar link".)</summary>
    Link,
    /// <summary>Copia o arquivo (colar no Explorer ou anexar em e-mail).</summary>
    File,
}

public sealed class HotkeySettings
{
    public string CaptureRegion { get; set; } = "Ctrl+Shift+4";
    public string CaptureFullScreen { get; set; } = "Ctrl+Shift+3";
    public string CaptureActiveWindow { get; set; } = "Ctrl+Shift+2";
    public string RecordMp4 { get; set; } = "Ctrl+Shift+5";
    public string RecordGif { get; set; } = "Ctrl+Shift+6";
    public string CaptureAndLink { get; set; } = "Ctrl+Shift+7";
}

public sealed class AppSettings
{
    public static string DefaultOutputFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Sharelink");

    public string OutputFolder { get; set; } = DefaultOutputFolder;

    /// <summary>Texto entre chaves é um formato de data do .NET. Ex.: "Print_{yyyy-MM-dd_HH-mm-ss}".</summary>
    public string FileNamePattern { get; set; } = "Sharelink_{yyyy-MM-dd_HH-mm-ss}";

    public bool UseMonthSubfolder { get; set; } = true;
    public ImageFormatOption ImageFormat { get; set; } = ImageFormatOption.Png;
    public int JpegQuality { get; set; } = 90;

    public bool OpenEditorAfterCapture { get; set; } = true;
    public bool SaveToFile { get; set; } = true;
    public ClipboardAction ClipboardAction { get; set; } = ClipboardAction.Image;
    public bool ShowNotification { get; set; } = true;

    /// <summary>Caminho do ffmpeg.exe. Vazio = procurar no PATH.</summary>
    public string? FfmpegPath { get; set; }
    public int VideoFps { get; set; } = 30;
    public int GifFps { get; set; } = 15;
    public bool RecordCursor { get; set; } = true;

    public HotkeySettings Hotkeys { get; set; } = new();

    public bool ShowGalleryOnStartup { get; set; } = true;
    public bool TrayHintShown { get; set; }

    // Cloudflare R2. As chaves NÃO ficam aqui: vão para o Gerenciador de Credenciais do Windows.
    public string R2AccountId { get; set; } = "";
    public string R2Bucket { get; set; } = "";
    public string R2PublicBaseUrl { get; set; } = "";
    public bool ConfirmBeforeUpload { get; set; } = true;

    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public UploadDestination Destination { get; set; } = UploadDestination.CloudflareR2;

    // S3 compatível (Amazon S3, Backblaze B2, Wasabi, DigitalOcean Spaces, MinIO...). Chaves no Gerenciador de Credenciais.
    public string S3Endpoint { get; set; } = "";
    public string S3Region { get; set; } = "";
    public string S3Bucket { get; set; } = "";
    public string S3PublicBaseUrl { get; set; } = "";
    public bool S3PathStyle { get; set; }
    public bool S3PublicReadAcl { get; set; }

    // Supabase Storage (acesso S3 do Supabase). Chaves S3 do Storage no Gerenciador de Credenciais.
    public string SupabaseProjectRef { get; set; } = "";
    public string SupabaseRegion { get; set; } = "";
    public string SupabaseBucket { get; set; } = "";

    // Nextcloud / WebDAV. Senha (de aplicativo) no Gerenciador de Credenciais.
    public WebDavMode WebDavMode { get; set; } = WebDavMode.Nextcloud;
    public string WebDavServer { get; set; } = "";
    public string WebDavFolder { get; set; } = "Sharelink";
    public string WebDavPublicBaseUrl { get; set; } = "";

    /// <summary>Corrige valores fora da faixa (ex.: arquivo editado à mão).</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(OutputFolder))
            OutputFolder = DefaultOutputFolder;
        if (string.IsNullOrWhiteSpace(FileNamePattern))
            FileNamePattern = new AppSettings().FileNamePattern;
        JpegQuality = Math.Clamp(JpegQuality, 10, 100);
        VideoFps = Math.Clamp(VideoFps, 5, 60);
        GifFps = Math.Clamp(GifFps, 5, 30);
        Hotkeys ??= new HotkeySettings();
        Hotkeys.CaptureAndLink ??= "";
        R2AccountId = R2AccountId?.Trim() ?? "";
        R2Bucket = R2Bucket?.Trim() ?? "";
        R2PublicBaseUrl = R2PublicBaseUrl?.Trim() ?? "";
        S3Endpoint = S3Endpoint?.Trim() ?? "";
        S3Region = S3Region?.Trim() ?? "";
        S3Bucket = S3Bucket?.Trim() ?? "";
        S3PublicBaseUrl = S3PublicBaseUrl?.Trim() ?? "";
        SupabaseProjectRef = SupabaseProjectRef?.Trim().ToLowerInvariant() ?? "";
        SupabaseRegion = SupabaseRegion?.Trim() ?? "";
        SupabaseBucket = SupabaseBucket?.Trim() ?? "";
        WebDavServer = WebDavServer?.Trim() ?? "";
        WebDavFolder = string.IsNullOrWhiteSpace(WebDavFolder) ? "Sharelink" : WebDavFolder.Trim().Trim('/');
        WebDavPublicBaseUrl = WebDavPublicBaseUrl?.Trim() ?? "";
        if (!Enum.IsDefined(Theme)) Theme = ThemeMode.System;
        if (!Enum.IsDefined(Destination)) Destination = UploadDestination.CloudflareR2;
        if (!Enum.IsDefined(WebDavMode)) WebDavMode = WebDavMode.Nextcloud;
        if (!Enum.IsDefined(ImageFormat)) ImageFormat = ImageFormatOption.Png;
        if (!Enum.IsDefined(ClipboardAction)) ClipboardAction = ClipboardAction.Image;
    }
}
