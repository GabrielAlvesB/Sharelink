using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sharelink.Settings;

namespace Sharelink.Output;

/// <summary>Gera nomes e caminhos de arquivo seguros dentro da pasta de capturas.</summary>
internal static partial class FileNamer
{
    private const int MaxNameLength = 120;

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex TokenRegex();

    /// <summary>Troca cada "{formato}" pela data formatada. Formatos inválidos ficam como texto.</summary>
    public static string Expand(string pattern, DateTime now) =>
        TokenRegex().Replace(pattern, match =>
        {
            try
            {
                return now.ToString(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return match.Groups[1].Value;
            }
        });

    /// <summary>Remove caracteres proibidos e qualquer tentativa de navegar entre pastas ("..", "\").</summary>
    public static string Sanitize(string name)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);

        string result = sb.ToString().Trim().TrimEnd('.', ' ');
        if (result.Length > MaxNameLength)
            result = result[..MaxNameLength].TrimEnd('.', ' ');
        return result.Length == 0 || result.All(c => c == '.') ? "Sharelink" : result;
    }

    /// <summary>Se o arquivo já existe, acrescenta " (2)", " (3)"... em vez de sobrescrever.</summary>
    public static string GetUniquePath(string folder, string baseName, string extension)
    {
        string path = Path.Combine(folder, baseName + extension);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{baseName} ({i}){extension}");
        return path;
    }

    /// <summary>Caminho final do novo arquivo; cria a pasta se necessário.</summary>
    public static string CreatePath(AppSettings settings, DateTime now, string extension)
    {
        string root = Path.GetFullPath(settings.OutputFolder);
        string folder = settings.UseMonthSubfolder ? Path.Combine(root, now.ToString("yyyy-MM", CultureInfo.InvariantCulture)) : root;
        Directory.CreateDirectory(folder);

        string path = Path.GetFullPath(GetUniquePath(folder, Sanitize(Expand(settings.FileNamePattern, now)), extension));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de saída fora da pasta de capturas.");
        return path;
    }
}
