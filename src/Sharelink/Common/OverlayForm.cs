using Sharelink.Native;

namespace Sharelink.Common;

/// <summary>
/// Base para janelas sem borda posicionadas em coordenadas físicas da tela.
/// Ignora o redimensionamento automático por DPI, que deslocaria a janela
/// ao atravessar monitores com escalas diferentes.
/// </summary>
internal class OverlayForm : Form
{
    public OverlayForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_DPICHANGED)
            return;
        base.WndProc(ref m);
    }
}
