using Sharelink.Editor;
using Sharelink.Settings;
using Sharelink.Upload;

namespace Sharelink.Output;

internal delegate void NotifyHandler(string title, string text, ToolTipIcon icon, string? fileToReveal);

/// <summary>O que acontece depois de uma captura: editor → salvar → (link) → área de transferência → aviso.</summary>
internal sealed class CaptureOutput(Func<AppSettings> getSettings, LinkService links, NotifyHandler notify)
{
    /// <summary>Um arquivo novo foi salvo na pasta de capturas.</summary>
    public event Action<string>? FileSaved;

    /// <summary>Assume a posse de <paramref name="capture"/> e o descarta ao final.</summary>
    /// <param name="generateLink">Gerar link público depois de salvar (só quando o usuário pede).</param>
    /// <param name="forceEditor">Abre o editor mesmo se estiver desativado nas configurações.</param>
    public async Task HandleImageAsync(Bitmap capture, bool generateLink, bool forceEditor = false)
    {
        AppSettings settings = getSettings();
        Bitmap image = capture;
        try
        {
            if (settings.OpenEditorAfterCapture || forceEditor)
            {
                using var editor = new EditorForm(capture, settings);
                if (editor.ShowDialog() != DialogResult.OK || editor.Result is null)
                    return;
                image = editor.Result;
                generateLink |= editor.GenerateLink;
            }

            string? path = null;
            if (settings.SaveToFile || generateLink || settings.ClipboardAction is ClipboardAction.Link or ClipboardAction.File)
            {
                path = FileNamer.CreatePath(settings, DateTime.Now, ImageSaver.GetExtension(settings.ImageFormat));
                ImageSaver.Save(image, path, settings.ImageFormat, settings.JpegQuality);
                FileSaved?.Invoke(path);
            }

            if (generateLink && path is not null && await CreateLinkAsync(path))
                return;

            string copied = CopyLocal(settings.ClipboardAction, path, image);
            notify("Captura pronta", Describe(copied, path), ToolTipIcon.Info, path);
        }
        finally
        {
            if (!ReferenceEquals(image, capture))
                image.Dispose();
            capture.Dispose();
        }
    }

    public Task HandleVideoAsync(string path)
    {
        FileSaved?.Invoke(path);
        AppSettings settings = getSettings();
        // Vídeo não pode ir como "imagem": nesse caso copia o arquivo.
        ClipboardAction action = settings.ClipboardAction == ClipboardAction.Image ? ClipboardAction.File : settings.ClipboardAction;
        string copied = CopyLocal(action, path, null);
        notify("Gravação pronta", Describe(copied, path) + "\nPara compartilhar, use \"Gerar link\" na galeria.", ToolTipIcon.Info, path);
        return Task.CompletedTask;
    }

    /// <returns>true se o link foi gerado e copiado.</returns>
    private async Task<bool> CreateLinkAsync(string path)
    {
        IUploader? client = links.Prepare(null, 1);
        if (client is null)
            return false;

        LinkInfo info = await links.CreateAsync(client, path);
        ClipboardHelper.SetText(info.Url);
        notify("Link copiado", info.Url, ToolTipIcon.Info, path);
        return true;
    }

    private static string CopyLocal(ClipboardAction action, string? path, Image? image)
    {
        switch (action)
        {
            case ClipboardAction.Image when image is not null:
                ClipboardHelper.SetImage(image);
                return "Imagem copiada.";
            case ClipboardAction.File when path is not null:
                ClipboardHelper.SetFile(path);
                return "Arquivo copiado (cole no Explorer, e-mail ou chat).";
            case ClipboardAction.Link when path is not null:
                ClipboardHelper.SetText(path);
                return "Caminho do arquivo copiado.";
            default:
                return "";
        }
    }

    private static string Describe(string copied, string? path)
    {
        string saved = path is null ? "" : $"Salvo em {Path.GetFileName(path)}. Clique para abrir a pasta.";
        return string.Join("\n", new[] { copied, saved }.Where(s => s.Length > 0));
    }
}
