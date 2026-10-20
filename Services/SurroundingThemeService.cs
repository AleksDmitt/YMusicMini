using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace YMusicMini.Services;

/// <summary>
/// Samples tiny squares OUTSIDE the widget's window bounds. Samples are used in memory
/// only: no screenshot files, camera/microphone access, network traffic, or process hooks.
/// The median rejects bright icons and small unrelated objects near the capsule.
/// </summary>
public sealed class SurroundingThemeService
{
    private MediaColor? _smoothed;

    public MediaColor? Read(Window window)
    {
        try
        {
            nint hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == 0 || !GetWindowRect(hwnd, out var rect)) return _smoothed;
            var usable = Forms.Screen.FromHandle(hwnd).WorkingArea;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 20 || height < 20) return _smoothed;

            const int patch = 12;
            const int gap = 24;
            var colors = new List<System.Drawing.Color>();
            using var bitmap = new Bitmap(patch, patch);
            using var graphics = Graphics.FromImage(bitmap);
            void SampleAt(int centerX, int centerY)
            {
                int x = centerX - patch / 2;
                int y = centerY - patch / 2;
                if (x < usable.Left || y < usable.Top || x + patch > usable.Right || y + patch > usable.Bottom)
                    return;
                graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(patch, patch), CopyPixelOperation.SourceCopy);
                long r = 0, g = 0, b = 0, count = 0;
                for (int px = 1; px < patch; px += 3)
                for (int py = 1; py < patch; py += 3)
                {
                    var color = bitmap.GetPixel(px, py);
                    r += color.R; g += color.G; b += color.B; count++;
                }
                colors.Add(System.Drawing.Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count)));
            }

            // Leave a small margin for the shadow and border. Sampling outside the
            // bounds is essential: otherwise the widget would sample its own color.
            foreach (double fraction in new[] { 0.18, 0.5, 0.82 })
            {
                int x = rect.Left + (int)(width * fraction);
                SampleAt(x, rect.Top - gap);
                SampleAt(x, rect.Bottom + gap);
            }
            foreach (double fraction in new[] { 0.25, 0.5, 0.75 })
            {
                int y = rect.Top + (int)(height * fraction);
                SampleAt(rect.Left - gap, y);
                SampleAt(rect.Right + gap, y);
            }
            if (colors.Count < 2) return _smoothed;

            static byte Median(IEnumerable<byte> values)
            {
                var ordered = values.OrderBy(x => x).ToArray();
                return ordered[ordered.Length / 2];
            }
            var measured = MediaColor.FromRgb(
                Median(colors.Select(c => c.R)),
                Median(colors.Select(c => c.G)),
                Median(colors.Select(c => c.B)));

            // Low-pass filter: reduces flashes from videos, hover highlights, or
            // animated wallpaper. Theme-switching also has its own hysteresis.
            _smoothed = _smoothed is { } previous ? Mix(previous, measured, 0.32) : measured;
            return _smoothed;
        }
        catch (Exception)
        {
            // Screen capture can be restricted (secure desktop, protected content).
            return _smoothed; // Caller falls back to the system theme if still null.
        }
    }

    public static MediaColor Mix(MediaColor a, MediaColor b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        static byte Channel(byte x, byte y, double t) => (byte)Math.Clamp((int)Math.Round(x + (y - x) * t), 0, 255);
        return MediaColor.FromRgb(Channel(a.R, b.R, t), Channel(a.G, b.G, t), Channel(a.B, b.B, t));
    }

    public static MediaColor GetCapsuleBackground(MediaColor environment, double intensity, bool light)
    {
        MediaColor baseColor = light ? MediaColor.FromRgb(249, 250, 253) : MediaColor.FromRgb(29, 33, 43);
        // Keep some contrast with the surroundings even at 100% intensity.
        return Mix(baseColor, environment, Math.Clamp(intensity, 0, 100) / 100.0 * 0.82);
    }

    public static double Brightness(MediaColor c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out RECT bounds);
}
