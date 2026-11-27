using System.Windows.Interop;
using static SnipNation.Windows.NativeMethods;

namespace SnipNation.Windows;

internal enum CaptureShortcut
{
    Print = 1,
    Alternate = 2
}

internal sealed class GlobalHotkey : IDisposable
{
    private readonly HwndSource _messages = new(new HwndSourceParameters("SnipNation hotkey")
    {
        ParentWindow = new nint(-3) // HWND_MESSAGE
    });

    internal GlobalHotkey()
    {
        _messages.AddHook(HandleMessage);
    }

    internal event Action? Pressed;
    internal CaptureShortcut? Registered { get; private set; }

    internal bool TryRegister(CaptureShortcut shortcut)
    {
        if (Registered == shortcut)
            return true;

        var modifiers = NoRepeat | (shortcut == CaptureShortcut.Alternate ? ControlAndShift : 0);
        if (!RegisterHotKey(_messages.Handle, (int)shortcut, modifiers, PrintScreen))
            return false;

        if (Registered is { } previous)
            UnregisterHotKey(_messages.Handle, (int)previous);
        Registered = shortcut;
        return true;
    }

    public void Dispose()
    {
        if (Registered is { } shortcut)
            UnregisterHotKey(_messages.Handle, (int)shortcut);
        _messages.Dispose();
    }

    private nint HandleMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == HotkeyMessage && Registered is { } shortcut && wParam.ToInt32() == (int)shortcut)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return 0;
    }
}
