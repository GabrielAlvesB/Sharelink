namespace Sharelink.Common;

/// <summary>Registro de erros em arquivo local (%AppData%\Sharelink\sharelink.log). Nunca enviado para lugar nenhum.</summary>
internal static class Log
{
    private const long MaxSizeBytes = 1024 * 1024;
    private static readonly object Sync = new();

    public static string FilePath => Path.Combine(Settings.SettingsStore.AppDataFolder, "sharelink.log");

    public static void Error(string context, Exception ex) => Write($"ERRO {context}: {ex}");

    public static void Info(string message) => Write($"INFO {message}");

    private static void Write(string line)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxSizeBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Registrar erro nunca deve derrubar o app.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
