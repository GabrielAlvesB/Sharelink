using Sharelink.Output;
using Sharelink.Settings;

namespace Sharelink.Upload;

/// <summary>Gera e apaga links: verifica a configuração, pede confirmação e registra no <see cref="LinkStore"/>.</summary>
internal sealed class LinkService(Func<AppSettings> getSettings, Action saveSettings, LinkStore store, Action openLinkSettings)
{
    public LinkStore Store => store;

    /// <summary>Nome do destino ativo (para mostrar na interface).</summary>
    public string ActiveDestinationName => UploaderFactory.DisplayName(getSettings().Destination);

    /// <summary>Confere se o destino ativo está configurado e pede confirmação ao usuário.</summary>
    /// <returns>O destino pronto, ou null se faltar configuração ou o usuário cancelar.</returns>
    public IUploader? Prepare(IWin32Window? owner, int fileCount)
    {
        AppSettings settings = getSettings();
        string name = UploaderFactory.DisplayName(settings.Destination);
        if (!UploaderFactory.TryCreate(settings.Destination, settings, out IUploader? uploader, out string error))
        {
            if (MessageBox.Show(owner,
                    $"Para gerar links, configure antes o destino \"{name}\".\n\n{error}\n\nAbrir as configurações agora?",
                    "Sharelink", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                openLinkSettings();
            return null;
        }

        if (settings.ConfirmBeforeUpload)
        {
            var upload = new TaskDialogButton(fileCount == 1 ? "Gerar link" : $"Gerar {fileCount} links");
            string thirdParty = UploaderFactory.IsThirdParty(settings.Destination)
                ? "\n\nAtenção: o Imgur é um serviço de terceiros. Ele pode comprimir a imagem e você depende dele para apagar."
                : "";
            var page = new TaskDialogPage
            {
                Caption = "Sharelink",
                Heading = fileCount == 1 ? $"Enviar para {name} e gerar link público?" : $"Enviar {fileCount} arquivos para {name}?",
                Text = "Qualquer pessoa que tiver o link poderá ver o arquivo. " +
                       "O endereço é aleatório e você pode apagar o link a qualquer momento pela galeria." + thirdParty,
                Icon = TaskDialogIcon.Warning,
                Buttons = { upload, TaskDialogButton.Cancel },
                Verification = new TaskDialogVerificationCheckBox("Não perguntar novamente"),
            };
            TaskDialogButton result = owner is null ? TaskDialog.ShowDialog(page) : TaskDialog.ShowDialog(owner, page);
            if (result != upload)
                return null;
            if (page.Verification.Checked)
            {
                settings.ConfirmBeforeUpload = false;
                saveSettings();
            }
        }

        return uploader;
    }

    /// <summary>Envia o arquivo (se ainda não tiver link) e devolve o link.</summary>
    public async Task<LinkInfo> CreateAsync(IUploader uploader, string filePath)
    {
        if (store.Get(filePath) is LinkInfo existing)
            return existing;

        UploadResult result = await uploader.UploadAsync(filePath);
        if (!result.Success || result.Link is null || result.RemoteKey is null)
            throw new InvalidOperationException($"Não foi possível gerar o link ({uploader.Name}): {result.Error}");

        var info = new LinkInfo(result.Link, result.RemoteKey, DateTime.Now, uploader.Destination);
        store.Set(filePath, info);
        return info;
    }

    /// <summary>Apaga o arquivo do destino onde ele foi enviado; o link deixa de funcionar.</summary>
    public async Task DeleteAsync(string filePath)
    {
        if (store.Get(filePath) is not LinkInfo info)
            return;
        UploadDestination destination = info.EffectiveDestination;
        if (!UploaderFactory.TryCreate(destination, getSettings(), out IUploader? uploader, out string error))
            throw new InvalidOperationException(
                $"Este link está no {UploaderFactory.DisplayName(destination)}, que não está mais configurado: {error}");

        await uploader!.DeleteAsync(info.Key);
        store.Remove(filePath);
    }
}
