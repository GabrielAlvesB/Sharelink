using System.Diagnostics;
using Sharelink.Common;
using Sharelink.Settings;

namespace Sharelink.Update;

/// <summary>
/// Atualização automática: verifica o GitHub, baixa a versão nova em segundo plano e confere a
/// assinatura digital. A instalação só acontece quando o usuário clica em "Atualizar".
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private const long MaxInstallerBytes = 400L * 1024 * 1024;
    private const long MaxSignatureBytes = 16 * 1024;
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly Func<AppSettings> _getSettings;
    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _checking;

    public UpdateService(Func<AppSettings> getSettings)
    {
        _getSettings = getSettings;
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = (int)CheckInterval.TotalMilliseconds;
            if (_getSettings().AutoUpdate)
                await CheckAsync(userInitiated: false);
        };
    }

    public static string UpdatesFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sharelink", "Updates");

    /// <summary>Versão nova já baixada e verificada, pronta para instalar.</summary>
    public ReleaseInfo? Ready { get; private set; }
    private string? _readyInstaller;

    public event Action<ReleaseInfo>? UpdateReady;

    public void Start()
    {
        _timer.Interval = (int)FirstCheckDelay.TotalMilliseconds;
        _timer.Start();
    }

    /// <returns>Mensagem para mostrar ao usuário.</returns>
    public async Task<string> CheckAsync(bool userInitiated)
    {
        if (_checking)
            return "Já estou verificando…";
        _checking = true;
        try
        {
            ReleaseInfo? latest = await GitHubUpdateClient.GetLatestAsync(CancellationToken.None);
            if (latest is null || latest.Version <= AppVersion.Current)
                return $"Você já está na versão mais recente ({AppVersion.CurrentText}).";
            if (Ready?.Version == latest.Version)
                return $"A versão {latest.VersionText} já foi baixada e está pronta para instalar.";

            string installer = await DownloadAndVerifyAsync(latest);
            Ready = latest;
            _readyInstaller = installer;
            UpdateReady?.Invoke(latest);
            return $"Versão {latest.VersionText} baixada e verificada. Pronta para instalar.";
        }
        catch (Exception ex)
        {
            Log.Error("atualização", ex);
            return userInitiated ? $"Não foi possível verificar atualizações: {ex.Message}" : "";
        }
        finally
        {
            _checking = false;
        }
    }

    private static async Task<string> DownloadAndVerifyAsync(ReleaseInfo release)
    {
        Directory.CreateDirectory(UpdatesFolder);
        foreach (string old in Directory.EnumerateFiles(UpdatesFolder))
            TryDelete(old);

        string version = release.VersionText;
        string installer = Path.Combine(UpdatesFolder, GitHubUpdateClient.InstallerName(version));
        string signaturePath = installer + ".sig";

        await GitHubUpdateClient.DownloadAsync(release.SignatureUrl, signaturePath, MaxSignatureBytes, CancellationToken.None);
        await GitHubUpdateClient.DownloadAsync(release.InstallerUrl, installer, MaxInstallerBytes, CancellationToken.None);

        if (!await IsAuthenticAsync(installer, signaturePath, version))
        {
            TryDelete(installer);
            TryDelete(signaturePath);
            throw new InvalidOperationException("a assinatura da atualização é inválida. O arquivo foi descartado por segurança.");
        }
        return installer;
    }

    /// <summary>Confere: versão do .sig = versão da release, hash do arquivo = hash assinado, e assinatura válida.</summary>
    private static async Task<bool> IsAuthenticAsync(string installer, string signaturePath, string version)
    {
        SignatureFile? signature = UpdateSignature.Parse(await File.ReadAllTextAsync(signaturePath));
        if (signature is null || signature.Version != version)
            return false;
        string hash = await UpdateSignature.FileSha256Async(installer);
        return string.Equals(hash, signature.Sha256, StringComparison.OrdinalIgnoreCase) &&
               UpdateSignature.Verify(UpdatePublicKey.Value, version, hash, signature.Signature);
    }

    /// <summary>
    /// Verifica de novo e executa o instalador em modo silencioso. O arquivo fica travado contra
    /// escrita entre a verificação e a execução, para não poder ser trocado no meio do caminho.
    /// </summary>
    /// <returns>true se o instalador foi iniciado (o app deve fechar em seguida).</returns>
    public async Task<bool> InstallAsync()
    {
        if (Ready is null || _readyInstaller is null || !File.Exists(_readyInstaller))
            return false;

        var lockStream = new FileStream(_readyInstaller, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!await IsAuthenticAsync(_readyInstaller, _readyInstaller + ".sig", Ready.VersionText))
        {
            lockStream.Dispose();
            Ready = null;
            throw new InvalidOperationException("a assinatura da atualização não confere mais. Ela foi descartada.");
        }

        var psi = new ProcessStartInfo(_readyInstaller) { UseShellExecute = false };
        foreach (string arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS" })
            psi.ArgumentList.Add(arg);
        Process.Start(psi);
        // Fica travado até o app fechar logo em seguida.
        _installLock = lockStream;
        return true;
    }

    private FileStream? _installLock;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _installLock?.Dispose();
    }
}
