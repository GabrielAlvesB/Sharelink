using Sharelink.Common;

namespace Sharelink.Theme;

/// <summary>Janela que segue o tema do app e se atualiza sozinha quando ele muda.</summary>
internal class ThemedForm : Form
{
    public ThemedForm()
    {
        Icon = AppIcon.Get();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = new Font("Segoe UI", 9.5f);
        ThemeManager.Changed += OnThemeChanged;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeManager.ApplyTitleBar(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        ThemeManager.Apply(this);
        base.OnLoad(e);
    }

    private void OnThemeChanged()
    {
        if (IsDisposed)
            return;
        ThemeManager.Apply(this);
        OnThemeApplied();
        Invalidate(true);
    }

    /// <summary>Para janelas que desenham coisas próprias e precisam reagir ao tema.</summary>
    protected virtual void OnThemeApplied()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ThemeManager.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }
}
