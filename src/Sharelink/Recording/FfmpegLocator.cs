namespace Sharelink.Recording;

/// <summary>Encontra o ffmpeg.exe instalado pelo usuário. O Sharelink nunca baixa executáveis.</summary>
internal static class FfmpegLocator
{
    public const string ExeName = "ffmpeg.exe";
    public const string InstallCommand = "winget install Gyan.FFmpeg";

    /// <returns>Caminho completo do ffmpeg.exe ou null se não encontrado.</returns>
    public static string? Find(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return IsValid(configuredPath) ? Path.GetFullPath(configuredPath) : null;

        foreach (string folder in SearchFolders())
        {
            string candidate = Path.Combine(folder, ExeName);
            if (IsValid(candidate))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    public static bool IsValid(string path) =>
        string.Equals(Path.GetFileName(path), ExeName, StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    private static IEnumerable<string> SearchFolders()
    {
        // Lê o PATH atualizado do registro: o FFmpeg pode ter sido instalado com o Sharelink já aberto.
        string?[] paths =
        [
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        ];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in paths.Where(p => p is not null).SelectMany(p => p!.Split(';')))
        {
            string trimmed = Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"'));
            // Ignora entradas relativas (ex.: ".") para não executar algo da pasta atual.
            if (trimmed.Length > 0 && Path.IsPathFullyQualified(trimmed) && seen.Add(trimmed))
                yield return trimmed;
        }

        string wingetLinks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links");
        if (seen.Add(wingetLinks))
            yield return wingetLinks;
    }
}
