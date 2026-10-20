using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace YMusicMini.Services;

/// <summary>
/// Приближённо определяет яркость реального содержимого активного окна по локальным пикселям экрана.
/// Ничего не сохраняет и не отправляет по сети.
/// </summary>
public sealed class ForegroundThemeService
{
    private IntPtr _lastExternalWindow;

    public bool IsLight(string mode)
    {
        if (mode == "Light") return true;
        if (mode == "Dark") return false;
        if (mode == "System") return SystemIsLight();

        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && GetOwnerPid(foreground) != Environment.ProcessId)
            _lastExternalWindow = foreground;

        if (_lastExternalWindow == IntPtr.Zero || !IsWindow(_lastExternalWindow) ||
            !GetWindowRect(_lastExternalWindow, out var bounds)) return SystemIsLight();

        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        if (width < 100 || height < 100) return SystemIsLight();
        try
        {
            using var bitmap = new Bitmap(20, 20);
            using var graphic = Graphics.FromImage(bitmap);
            double sum = 0;
            int count = 0;
            foreach (var fx in new[] { 0.35, 0.65 })
            foreach (var fy in new[] { 0.35, 0.65 })
            {
                int x = bounds.Left + (int)(width * fx);
                int y = bounds.Top + (int)(height * fy);
                var screen = Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
                x = Math.Clamp(x - 10, screen.Bounds.Left, screen.Bounds.Right - 20);
                y = Math.Clamp(y - 10, screen.Bounds.Top, screen.Bounds.Bottom - 20);
                graphic.CopyFromScreen(x, y, 0, 0, new Size(20, 20), CopyPixelOperation.SourceCopy);
                for (int px = 2; px < 20; px += 4)
                for (int py = 2; py < 20; py += 4)
                {
                    var c = bitmap.GetPixel(px, py);
                    sum += 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                    count++;
                }
            }
            return count > 0 ? sum / count > 142 : SystemIsLight();
        }
        catch { return SystemIsLight(); }
    }

    public static bool SystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme") ?? 0) != 0;
        }
        catch { return false; }
    }

    private static uint GetOwnerPid(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr hwnd);
}
