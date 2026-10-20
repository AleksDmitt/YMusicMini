using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.UI.ViewManagement;
using Color = System.Windows.Media.Color;
using DrawingColor = System.Drawing.Color;

namespace YMusicMini.Services;

/// <summary>Раздельный источник градиента кнопок. Снимки обоев не отправляются и не сохраняются.</summary>
public sealed class AccentPaletteService
{
    private string? _cachedWallpaperPath;
    private DateTime _cachedWallpaperWriteTime;
    private Color[]? _cachedWallpaperPalette;

    public Color[] Read(string mode, ImageSource? albumCover)
    {
        try
        {
            return mode switch
            {
                "Windows" => FromAccent(),
                "Wallpaper" => ReadWallpaperColors() ?? Fixed(),
                "Album" => ReadAlbumColors(albumCover) ?? Fixed(),
                _ => Fixed()
            };
        }
        catch { return Fixed(); }
    }

    private static Color[] Fixed() =>
        [Color.FromRgb(147,197,234), Color.FromRgb(175,167,235), Color.FromRgb(229,170,209)];

    private static Color[] FromAccent()
    {
        var accent = new UISettings().GetColorValue(UIColorType.Accent);
        var c = Pastel(DrawingColor.FromArgb(accent.R, accent.G, accent.B));
        return [c, c, c];
    }

    private Color[]? ReadWallpaperColors()
    {
        string? path = null;
        object? com = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"));
            if (type is not null)
            {
                com = Activator.CreateInstance(type);
                if (com is IDesktopWallpaper desktop)
                {
                    // При слайд-шоу GetWallpaper(null) возвращает пустое значение.
                    // Указываем монитор явно.
                    string monitor = desktop.GetMonitorDevicePathAt(0);
                    path = desktop.GetWallpaper(monitor);
                }
            }
        }
        catch { /* используем резервный путь ниже */ }
        finally { if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com); }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            // Для некоторых тем и слайд-шоу Windows пишет сюда перекодированное изображение.
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        }
        if (!File.Exists(path)) return null;
        var written = File.GetLastWriteTimeUtc(path);
        if (path == _cachedWallpaperPath && written == _cachedWallpaperWriteTime)
            return _cachedWallpaperPalette;
        using var image = new Bitmap(path);
        var colors = Extract(image);
        _cachedWallpaperPath = path;
        _cachedWallpaperWriteTime = written;
        _cachedWallpaperPalette = colors;
        return colors;
    }

    private static Color[]? ReadAlbumColors(ImageSource? cover)
    {
        if (cover is not BitmapSource bitmap) return null;
        using var memory = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(memory);
        memory.Position = 0;
        using var image = new Bitmap(memory);
        return Extract(image);
    }

    private static Color[] Extract(Bitmap image)
    {
        using var small = new Bitmap(36, 24);
        using (var g = Graphics.FromImage(small))
            g.DrawImage(image, 0, 0, 36, 24);

        var result = new Color[3];
        for (int band = 0; band < 3; band++)
        {
            long r = 0, gg = 0, b = 0, count = 0;
            for (int x = band * 12; x < (band + 1) * 12; x++)
            for (int y = 0; y < 24; y++)
            {
                var c = small.GetPixel(x, y);
                // Среднее, с небольшим весом более насыщенных пикселей.
                int weight = 1 + (int)(c.GetSaturation() * 3);
                r += c.R * weight;
                gg += c.G * weight;
                b += c.B * weight;
                count += weight;
            }
            result[band] = Pastel(DrawingColor.FromArgb((int)(r/count), (int)(gg/count), (int)(b/count)));
        }
        return result;
    }

    // Сохраняем оттенок, но поднимаем яркость: тёмная пиктограмма различима на всех кнопках.
    private static Color Pastel(DrawingColor c)
    {
        const double keep = 0.42;
        return Color.FromRgb(
            (byte)(c.R * keep + 221 * (1 - keep)),
            (byte)(c.G * keep + 225 * (1 - keep)),
            (byte)(c.B * keep + 234 * (1 - keep)));
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
                          [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
    }
}
