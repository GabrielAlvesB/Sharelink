using Sharelink.Controls;
using Sharelink.Theme;

namespace Sharelink.Editor;

internal sealed class TextInputDialog : ThemedForm
{
    private readonly TextBox _textBox;

    public TextInputDialog()
    {
        Text = "Adicionar texto";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 190);
        Padding = new Padding(14);

        _textBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
        var hint = new Label { Text = "Ctrl+Enter para confirmar", Dock = DockStyle.Left, AutoSize = true, Padding = new Padding(0, 16, 0, 0) };
        ThemeManager.SetRole(hint, ThemeRole.Muted);
        var ok = new ModernButton { Text = "Adicionar", Variant = ButtonVariant.Primary, DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 110 };
        var cancel = new ModernButton { Text = "Cancelar", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 100 };
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange([hint, ok, new Panel { Dock = DockStyle.Right, Width = 8 }, cancel]);

        Controls.Add(_textBox);
        Controls.Add(buttons);
        CancelButton = cancel;
    }

    public string Value => _textBox.Text.TrimEnd();

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter))
        {
            DialogResult = DialogResult.OK;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
