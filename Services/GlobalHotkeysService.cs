using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Window = System.Windows.Window;
using YMusicMini.Models;

namespace YMusicMini.Services;

/// <summary>
/// Optional user-selectable global hotkeys. Failed registration rolls back
/// all bindings; no keyboard hooks or key injection.
/// </summary>
public sealed class GlobalHotkeysService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private readonly IntPtr _handle;
    private readonly HwndSource? _source;
    private readonly Action<int> _callback;

    public GlobalHotkeysService(Window window, Action<int> callback)
    {
        _callback = callback;
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
    }

    public bool Configure(AppSettings settings)
    {
        Unregister();
        if (!settings.EnableGlobalHotkeys) return true;
        if (_handle == IntPtr.Zero) return false;
        var list = new[] { settings.PlayPauseHotkey, settings.PreviousHotkey, settings.NextHotkey };
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == "Нет") continue;
            if (!ParseHotkey(list[i], out uint modifiers, out uint key) ||
                !RegisterHotKey(_handle, i + 1, modifiers | MOD_NOREPEAT, key))
            {
                Unregister();
                return false;
            }
        }
        return true;
    }

    private static bool ParseHotkey(string? shortcut, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        string[] parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (string part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                default: return false;
            }
        }
        key = parts[^1].ToLowerInvariant() switch
        {
            "left" => 0x25,
            "right" => 0x27,
            "space" => 0x20,
            "p" => 0x50,
            "j" => 0x4A,
            "k" => 0x4B,
            _ => 0
        };
        return modifiers != 0 && key != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() is >= 1 and <= 3)
        {
            _callback(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Unregister()
    {
        if (_handle == IntPtr.Zero) return;
        foreach (int id in new[] { 1, 2, 3 }) UnregisterHotKey(_handle, id);
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
