using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace YMusicMini.Services;

/// <summary>Opens the existing desktop Yandex Music window without unofficial APIs.</summary>
public static class YandexWindowService
{
    private const int SwRestore = 9;

    /// <summary>Whether the independent desktop client is running. Does not treat a browser tab as the app.</summary>
    public static bool IsRunning(string? explicitlySelectedProcess = null)
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    string name;
                    try { name = process.ProcessName; }
                    catch { continue; }
                    if (!string.IsNullOrWhiteSpace(explicitlySelectedProcess) &&
                        name.Equals(explicitlySelectedProcess, StringComparison.OrdinalIgnoreCase)) return true;
                    string normalized = new string(name.Where(char.IsLetterOrDigit)
                        .Select(char.ToLowerInvariant).ToArray());
                    if (normalized is "ymusic" or "yandexmusic" or "яндексмузыка" ||
                        normalized.StartsWith("yandexmusic", StringComparison.Ordinal) ||
                        normalized.StartsWith("яндексмузыка", StringComparison.Ordinal)) return true;
                    // Some desktop app builds have a Yandex-prefixed executable with
                    // the main window specifically titled Yandex Music.
                    if (normalized.Contains("yandex"))
                    {
                        string title;
                        try { title = process.MainWindowTitle; }
                        catch { continue; }
                        if (title.Contains("Яндекс Музыка", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Yandex Music", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
        }
        catch { /* access to unrelated processes may be denied */ }
        return false;
    }

    public static bool TryActivate()
    {
        nint target = 0;
        int topScore = 0;
        // Do not assume the process' MainWindowHandle is available while
        // a desktop app is minimized to its notification-area icon.
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId) return true;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                string normalized = new string(process.ProcessName
                    .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                string title = GetTitle(hwnd);
                bool exactName = normalized is "ymusic" or "yandexmusic" or "яндексмузыка" ||
                    normalized.StartsWith("yandexmusic", StringComparison.Ordinal) ||
                    normalized.StartsWith("яндексмузыка", StringComparison.Ordinal);
                bool matchingTitle = title.Contains("Яндекс Музыка", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Yandex Music", StringComparison.OrdinalIgnoreCase);
                // Never open an arbitrary browser tab showing Yandex Music.
                if (!exactName && !(normalized.Contains("yandex") && matchingTitle)) return true;

                int score = 0;
                if (exactName) score += 2;
                if (matchingTitle) score += 3;
                if (IsWindowVisible(hwnd)) score += 1;
                if (GetWindowRect(hwnd, out var rect) && rect.Right - rect.Left >= 240 &&
                    rect.Bottom - rect.Top >= 160) score += 2;
                // Prefer a real main window over helpers or invisible controls.
                if (score > topScore && score >= 4)
                {
                    target = hwnd;
                    topScore = score;
                }
            }
            catch (Exception) { /* Access may be denied for unrelated processes. */ }
            return true;
        }, 0);

        if (target == 0) return false;
        ShowWindowAsync(target, SwRestore);
        return SetForegroundWindow(target) || IsWindowVisible(target);
    }

    private static string GetTitle(nint hwnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsCallback(nint hwnd, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint lParam);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder buffer, int maxCount);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);
}
