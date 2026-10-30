using System.ComponentModel;
using Sharelink.Hotkeys;

namespace Sharelink.Settings;

/// <summary>Campo que "grava" a combinação de teclas pressionada. Backspace/Delete limpa.</summary>
internal sealed class HotkeyBox : TextBox
{
    private Keys _hotkey;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
        Hotkey = Keys.None;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Keys Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            Text = value == Keys.None ? "(nenhum)" : HotkeyParser.Format(value);
        }
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) != Keys.Tab || keyData.HasFlag(Keys.Control);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.Modifiers == Keys.None && e.KeyCode is Keys.Back or Keys.Delete or Keys.Escape)
            Hotkey = Keys.None;
        else if (!HotkeyParser.IsModifierKey(e.KeyCode))
            Hotkey = e.KeyData;
        else
            Text = HotkeyParser.Format(e.Modifiers | Keys.A).TrimEnd('A') + "…";
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Hotkey = _hotkey;
        base.OnKeyUp(e);
    }
}
