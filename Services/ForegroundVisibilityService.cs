using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace YMusicMini.Services;

/// <summary>Conservative, opt-in window visibility detection. No process injection.</summary>
public sealed class ForegroundVisibilityService
{
    public bool ShouldHide(bool hideYandexForeground, bool hideFullscreen)
    {
        if (!hideYandexForeground && !hideFullscreen) return false;
        if (hideYandexForeground && IsYandexMusicForeground()) return true;
        if (!hideFullscreen) return false;
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsShellSurface(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid != Environment.ProcessId && IsFullScreenWindow(hwnd);
    }

    // Важно: НЕ перебираем все видимые окна. Яндекс Музыка может быть
    // открыта на заднем плане, но тогда капсула должна оставаться видимой.
    private static bool IsYandexMusicForeground()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid != 0 && pid != Environment.ProcessId && IsYandexMusicWindow(hwnd, pid);
    }

    private static bool IsYandexMusicWindow(IntPtr hwnd, uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            string normalized = new string(process.ProcessName.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant).ToArray());
            if (normalized is "ymusic" or "yandexmusic" or "яндексмузыка" ||
                normalized.StartsWith("yandexmusic", StringComparison.Ordinal)) return true;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            // Require both a Yandex-like process and a plausible music title to
            // avoid hiding due to an unrelated Yandex Browser page.
            bool titleMatches = title.ToString().Contains("Яндекс Музыка", StringComparison.OrdinalIgnoreCase) ||
                title.ToString().Contains("Yandex Music", StringComparison.OrdinalIgnoreCase);
            return titleMatches && normalized.Contains("yandex");
        }
        catch { return false; }
    }

    // Explorer's desktop and taskbar may fill the entire monitor, but are not
    // fullscreen games or applications. Otherwise "Show desktop" hides the widget.
    internal static bool IsShellSurface(IntPtr hwnd)
    {
        var className = new StringBuilder(128);
        if (GetClassName(hwnd, className, className.Capacity) == 0) return false;
        return className.ToString() is "Progman" or "WorkerW" or
            "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "SHELLDLL_DefView";
    }

    private static bool IsFullScreenWindow(IntPtr hwnd)
    {
        if (IsIconic(hwnd) || !GetWindowRect(hwnd, out var rect)) return false;
        var screen = Forms.Screen.FromHandle(hwnd).Bounds;
        return rect.Left <= screen.Left + 2 && rect.Top <= screen.Top + 2 &&
               rect.Right >= screen.Right - 2 && rect.Bottom >= screen.Bottom - 2;
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
