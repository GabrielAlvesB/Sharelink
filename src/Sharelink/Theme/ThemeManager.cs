using System.Runtime.InteropServices;
using Microsoft.Win32;
using Sharelink.Controls;
using Sharelink.Settings;

namespace Sharelink.Theme;

/// <summary>Papel visual de um controle (define quais cores ele recebe).</summary>
internal enum ThemeRole
{
    Default,
    Surface,
    Muted,
    Warning,
    Heading,
    Canvas,
}

/// <summary>Tema atual (claro/escuro) e aplicação das cores em qualquer janela.</summary>
internal static class ThemeManager
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Roles = new();
    private static ThemeMode _mode = ThemeMode.System;
    private static bool _listening;

    public static Palette Current { get; private set; } = ResolvePalette(ThemeMode.System, IsWindowsDark());

    /// <summary>O tema mudou (pelo usuário ou porque o Windows trocou de modo).</summary>
    public static event Action? Changed;

    public static Palette ResolvePalette(ThemeMode mode, bool windowsIsDark) => mode switch
    {
        ThemeMode.Dark => Palette.Dark,
        ThemeMode.Light => Palette.Light,
        _ => windowsIsDark ? Palette.Dark : Palette.Light,
    };

    /// <summary>Lê a preferência "modo dos aplicativos" do Windows.</summary>
    public static bool IsWindowsDark()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    public static void Initialize(ThemeMode mode)
    {
        _mode = mode;
        Current = ResolvePalette(mode, IsWindowsDark());
        ApplyColorMode();
        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                    Refresh();
            };
        }
    }

    public static void SetMode(ThemeMode mode)
    {
        _mode = mode;
        Refresh();
    }

    private static void Refresh()
    {
        Palette next = ResolvePalette(_mode, IsWindowsDark());
        if (next == Current)
            return;
        Current = next;
        ApplyColorMode();
        Changed?.Invoke();
    }

    /// <summary>Modo escuro nativo do WinForms para os controles padrão (caixas de texto, rolagem...).</summary>
    private static void ApplyColorMode()
    {
#pragma warning disable WFO5001 // API de modo escuro do WinForms (ainda marcada como experimental)
        Application.SetColorMode(Current.IsDark ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
    }

    public static void SetRole(Control control, ThemeRole role)
    {
        Roles.AddOrUpdate(control, role);
        ApplyTo(control, Current);
    }

    private static ThemeRole RoleOf(Control control) => Roles.TryGetValue(control, out object? role) ? (ThemeRole)role : ThemeRole.Default;

    /// <summary>Aplica as cores a um controle e a todos os filhos.</summary>
    public static void Apply(Control root)
    {
        Palette p = Current;
        ApplyTo(root, p);
        foreach (Control child in root.Controls)
            Apply(child);
        if (root.ContextMenuStrip is not null)
            ApplyTo(root.ContextMenuStrip, p);
        if (root is Form form)
            ApplyTitleBar(form);
    }

    private static void ApplyTo(Control c, Palette p)
    {
        ThemeRole role = RoleOf(c);
        switch (c)
        {
            case ModernButton:
            case GalleryView:
                c.Invalidate();
                break;
            case ToolStrip strip:
                strip.Renderer = new ThemedRenderer(p);
                strip.BackColor = role == ThemeRole.Surface ? p.Surface : p.Background;
                strip.ForeColor = p.Text;
                foreach (ToolStripItem item in strip.Items)
                    ApplyToItem(item, p);
                break;
            case TextBox or NumericUpDown or ComboBox or ListBox:
                c.BackColor = p.Input;
                c.ForeColor = p.Text;
                if (c is TextBox tb) tb.BorderStyle = tb.Parent is SearchBox ? BorderStyle.None : BorderStyle.FixedSingle;
                if (c is ComboBox cb) cb.FlatStyle = FlatStyle.Flat;
                break;
            case LinkLabel link:
                link.LinkColor = link.ActiveLinkColor = p.Accent;
                link.VisitedLinkColor = p.Accent;
                link.BackColor = Color.Transparent;
                break;
            case Label or CheckBox or RadioButton:
                c.ForeColor = role switch
                {
                    ThemeRole.Muted => p.TextMuted,
                    ThemeRole.Warning => p.Warning,
                    _ => p.Text,
                };
                c.BackColor = Color.Transparent;
                break;
            case PictureBox:
                c.BackColor = c.Parent?.BackColor ?? p.Background;
                break;
            default:
                c.BackColor = role switch
                {
                    ThemeRole.Surface => p.Surface,
                    ThemeRole.Canvas => p.CanvasBackground,
                    // Contêineres sem papel definido acompanham o fundo de quem os contém.
                    _ => c is Form || c.Parent is null ? p.Background : c.Parent.BackColor,
                };
                c.ForeColor = p.Text;
                break;
        }
    }

    private static void ApplyToItem(ToolStripItem item, Palette p)
    {
        item.ForeColor = p.Text;
        if (item is ToolStripTextBox textBox)
        {
            textBox.BackColor = p.Input;
            textBox.ForeColor = p.Text;
        }
        else if (item is ToolStripComboBox combo)
        {
            combo.BackColor = p.Input;
            combo.ForeColor = p.Text;
            combo.FlatStyle = FlatStyle.Flat;
        }
        if (item is ToolStripDropDownItem dropDown)
        {
            dropDown.DropDown.Renderer = new ThemedRenderer(p);
            dropDown.DropDown.BackColor = p.Surface;
            foreach (ToolStripItem child in dropDown.DropDownItems)
                ApplyToItem(child, p);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Barra de título escura/clara combinando com a janela (Windows 10 20H1+ / 11).</summary>
    public static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated)
            return;
        Palette p = Current;
        int dark = p.IsDark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int caption = ColorTranslator.ToWin32(p.Background);
        DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
    }
}
