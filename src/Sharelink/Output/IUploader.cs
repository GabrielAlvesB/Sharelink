using Sharelink.Settings;

namespace Sharelink.Output;

/// <param name="RemoteKey">Identificador do arquivo no destino, necessário para apagar depois.</param>
internal sealed record UploadResult(bool Success, string? Link, string? RemoteKey, string? Error = null);

/// <summary>Destino que transforma um arquivo local em link (R2, S3, Imgur, Nextcloud/WebDAV).</summary>
internal interface IUploader
{
    UploadDestination Destination { get; }

    /// <summary>Nome para mostrar ao usuário.</summary>
    string Name { get; }

    Task<UploadResult> UploadAsync(string filePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string remoteKey, CancellationToken cancellationToken = default);

    /// <summary>Envia um arquivo minúsculo, confere o link público e apaga. Devolve uma mensagem para o usuário.</summary>
    Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default);
}
