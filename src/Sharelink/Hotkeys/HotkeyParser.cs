namespace Sharelink.Hotkeys;

/// <summary>Converte atalhos entre texto ("Ctrl+Shift+4") e <see cref="Keys"/>.</summary>
internal static class HotkeyParser
{
    public static bool TryParse(string? text, out Keys keys)
    {
        keys = Keys.None;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        Keys modifiers = Keys.None;
        Keys keyCode = Keys.None;

        foreach (string rawPart in text.Split('+'))
        {
            string part = rawPart.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= Keys.Control;
                    break;
                case "shift":
                    modifiers |= Keys.Shift;
                    break;
                case "alt":
                    modifiers |= Keys.Alt;
                    break;
                default:
                    if (keyCode != Keys.None)
                        return false;
                    if (part.Length == 1 && char.IsAsciiDigit(part[0]))
                        keyCode = Keys.D0 + (part[0] - '0');
                    else if (!Enum.TryParse(part, ignoreCase: true, out keyCode) || int.TryParse(part, out _))
                        return false;
                    break;
            }
        }

        if (keyCode == Keys.None || IsModifierKey(keyCode))
            return false;

        keys = modifiers | keyCode;
        return true;
    }

    public static string Format(Keys keys)
    {
        Keys keyCode = keys & Keys.KeyCode;
        if (keyCode == Keys.None)
            return string.Empty;

        var parts = new List<string>(4);
        if (keys.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (keys.HasFlag(Keys.Shift)) parts.Add("Shift");
        if (keys.HasFlag(Keys.Alt)) parts.Add("Alt");
        parts.Add(keyCode is >= Keys.D0 and <= Keys.D9
            ? ((char)('0' + (keyCode - Keys.D0))).ToString()
            : keyCode.ToString());
        return string.Join('+', parts);
    }

    public static bool IsModifierKey(Keys keyCode) => keyCode is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;
}
