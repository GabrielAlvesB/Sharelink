using Sharelink.Native;

namespace Sharelink.Hotkeys;

/// <summary>Registra atalhos globais do Windows e dispara a ação correspondente.</summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public HotkeyManager()
    {
        // Janela "só de mensagens": invisível, existe apenas para receber WM_HOTKEY.
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
    }

    /// <returns>false se o atalho for inválido ou já estiver em uso por outro programa.</returns>
    public bool Register(Keys keys, Action action)
    {
        uint vk = (uint)(keys & Keys.KeyCode);
        if (vk == 0)
            return false;

        uint modifiers = NativeMethods.MOD_NOREPEAT;
        if (keys.HasFlag(Keys.Control)) modifiers |= NativeMethods.MOD_CONTROL;
        if (keys.HasFlag(Keys.Shift)) modifiers |= NativeMethods.MOD_SHIFT;
        if (keys.HasFlag(Keys.Alt)) modifiers |= NativeMethods.MOD_ALT;

        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(Handle, id, modifiers, vk))
            return false;

        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _actions.Keys)
            NativeMethods.UnregisterHotKey(Handle, id);
        _actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out Action? action))
        {
            action();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
