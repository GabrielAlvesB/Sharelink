namespace Sharelink.Theme;

/// <summary>Cores do app. Toda a interface lê daqui, então trocar o tema é trocar a paleta.</summary>
internal sealed record Palette(
    bool IsDark,
    Color Background,
    Color Surface,
    Color SurfaceHover,
    Color Border,
    Color Input,
    Color Text,
    Color TextMuted,
    Color Accent,
    Color AccentHover,
    Color AccentText,
    Color Danger,
    Color Success,
    Color Warning,
    Color CanvasBackground)
{
    public static readonly Palette Dark = new(
        IsDark: true,
        Background: Color.FromArgb(24, 24, 28),
        Surface: Color.FromArgb(34, 34, 40),
        SurfaceHover: Color.FromArgb(46, 46, 54),
        Border: Color.FromArgb(56, 56, 66),
        Input: Color.FromArgb(28, 28, 33),
        Text: Color.FromArgb(236, 236, 241),
        TextMuted: Color.FromArgb(150, 150, 162),
        Accent: Color.FromArgb(59, 130, 246),
        AccentHover: Color.FromArgb(96, 165, 250),
        AccentText: Color.White,
        Danger: Color.FromArgb(239, 68, 68),
        Success: Color.FromArgb(34, 197, 94),
        Warning: Color.FromArgb(245, 158, 11),
        CanvasBackground: Color.FromArgb(16, 16, 19));

    public static readonly Palette Light = new(
        IsDark: false,
        Background: Color.FromArgb(244, 245, 248),
        Surface: Color.White,
        SurfaceHover: Color.FromArgb(234, 237, 242),
        Border: Color.FromArgb(214, 218, 226),
        Input: Color.White,
        Text: Color.FromArgb(24, 24, 27),
        TextMuted: Color.FromArgb(107, 114, 128),
        Accent: Color.FromArgb(37, 99, 235),
        AccentHover: Color.FromArgb(29, 78, 216),
        AccentText: Color.White,
        Danger: Color.FromArgb(220, 38, 38),
        Success: Color.FromArgb(22, 163, 74),
        Warning: Color.FromArgb(217, 119, 6),
        CanvasBackground: Color.FromArgb(225, 228, 234));

    /// <summary>Cor de seleção translúcida (fundo de item selecionado).</summary>
    public Color Selection => Color.FromArgb(IsDark ? 60 : 40, Accent);
}
