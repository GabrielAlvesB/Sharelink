using Microsoft.Win32;

namespace Sharelink.Settings;

/// <summary>"Iniciar com o Windows" via HKCU\...\Run (só o usuário atual, sem precisar de administrador).</summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Sharelink";

    public const string MinimizedArgument = "--minimized";

    // Iniciado com o Windows, abre só na bandeja.
    private static string Command => $"\"{Environment.ProcessPath}\" {MinimizedArgument}";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value &&
               string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
