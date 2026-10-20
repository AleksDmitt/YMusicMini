using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace YMusicMini.Services;

/// <summary>
/// Keeps the floating capsule on the desktop when Explorer runs Show Desktop / Minimize All.
/// Respects explicit tray hiding and the application's automatic-visibility rules.
/// Does not activate the window or install global keyboard hooks.
/// </summary>
public sealed class DesktopVisibilityService : IDisposable
{
    private const int WmSysCommand = 0x0112;
    private const long ScMinimize = 0xF020;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = new(-1);
    private static readonly nint HwndNoTopmost = new(-2);

    private readonly Window _window;
    private readonly Func<bool> _mayBeVisible;
    private readonly Func<bool> _normalTopmost;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private HwndSource? _source;
    private nint _hwnd;
    private bool _temporarilyTopmost;
    private bool _disposed;
    private DateTime _lastRaiseUtc = DateTime.MinValue;

    public DesktopVisibilityService(Window window, Func<bool> mayBeVisible, Func<bool> normalTopmost)
    {
        _window = window;
        _mayBeVisible = mayBeVisible;
        _normalTopmost = normalTopmost;
        _watch.Tick += Watch_Tick;
        _window.StateChanged += Window_StateChanged;
    }

    public void Start()
    {
        if (_disposed || _hwnd != 0) return;
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == 0) return;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        _watch.Start();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Most normal minimize commands can be prevented. Show Desktop may use
        // other shell mechanisms; the periodic check handles those separately.
        if (msg == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScMinimize && CanRestore())
        {
            handled = true;
            Debug.WriteLine("YMusic Mini: blocked shell minimize request");
        }
        return 0;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized && CanRestore())
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Check));
    }

    private void Watch_Tick(object? sender, EventArgs e) => Check();

    private bool CanRestore() => !_disposed && _hwnd != 0 && _mayBeVisible();

    private void Check()
    {
        if (!CanRestore())
        {
            UndoTemporaryTopmost();
            return;
        }

        // Win+M can actually minimize the window. Restore without taking focus.
        // If the shell hides it directly, restore only while the widget should be visible.
        if (IsIconic(_hwnd) || !IsWindowVisible(_hwnd))
        {
            ShowWindow(_hwnd, SwShowNoActivate);
            if (!_window.IsVisible) _window.Show(); // ShowActivated=False in MainWindow.xaml.
            Debug.WriteLine("YMusic Mini: restored capsule after Minimize All");
        }

        // Win+D can leave a borderless tool window 'visible' yet behind the desktop.
        // Raise it above Explorer, with SWP_NOACTIVATE to preserve the active app.
        if (IsDesktopForeground())
        {
            if (!_normalTopmost()) _temporarilyTopmost = true;
            if ((DateTime.UtcNow - _lastRaiseUtc).TotalMilliseconds >= 850)
            {
                SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
                _lastRaiseUtc = DateTime.UtcNow;
            }
        }
        else
        {
            _lastRaiseUtc = DateTime.MinValue;
            UndoTemporaryTopmost();
        }
    }

    private void UndoTemporaryTopmost()
    {
        if (!_temporarilyTopmost) return;
        _temporarilyTopmost = false;
        if (_hwnd != 0 && !_normalTopmost())
            SetWindowPos(_hwnd, HwndNoTopmost, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private static bool IsDesktopForeground()
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0) return false;
        var buffer = new StringBuilder(96);
        if (GetClassName(foreground, buffer, buffer.Capacity) == 0) return false;
        // Do not include Shell_TrayWnd: opening Start or clicking the taskbar is
        // not equivalent to requesting Show Desktop.
        return buffer.ToString() is "Progman" or "WorkerW";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watch.Stop();
        _window.StateChanged -= Window_StateChanged;
        _source?.RemoveHook(WndProc);
        _source = null;
        UndoTemporaryTopmost();
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hwnd, int nCmdShow);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y,
        int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder name, int maxCount);
}
