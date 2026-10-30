using System.Text.Json;
using System.Text.Json.Serialization;
using Sharelink.Common;

namespace Sharelink.Settings;

/// <summary>Lê e grava as configurações em JSON local.</summary>
internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string AppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sharelink");

    public static string DefaultPath => Path.Combine(AppDataFolder, "settings.json");

    public static AppSettings Load(string path, out bool isFirstRun)
    {
        isFirstRun = !File.Exists(path);
        if (isFirstRun)
            return new AppSettings();

        try
        {
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                                   ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (JsonException ex)
        {
            // Arquivo corrompido: guarda uma cópia e volta ao padrão em vez de travar.
            Log.Error("configurações inválidas", ex);
            File.Copy(path, path + ".invalido", overwrite: true);
            return new AppSettings();
        }
    }

    /// <summary>Cópia independente, para editar sem afetar as configurações em uso.</summary>
    public static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)!;

    public static void Save(AppSettings settings, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
