using System.Text.RegularExpressions;

namespace Sharelink.Update;

internal static partial class AppVersion
{
    [GeneratedRegex(@"^v?(\d{1,4})\.(\d{1,4})\.(\d{1,5})$")]
    private static partial Regex TagRegex();

    /// <summary>Versão deste executável (vem do &lt;Version&gt; do csproj).</summary>
    public static Version Current
    {
        get
        {
            Version v = typeof(AppVersion).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentText => Format(Current);

    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    /// <summary>Aceita "1.2.3" ou "v1.2.3".</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        Match m = TagRegex().Match(text ?? "");
        if (!m.Success)
            return false;
        version = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        return true;
    }
}
