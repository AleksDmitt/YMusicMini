using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace YMusicMini.Services;

public sealed record ButtonVisualOption(string Value, string Title, ImageSource Thumbnail);

/// <summary>Artwork taken from button-styles-5.zip. Only standard media buttons use these sprites.</summary>
public static class ButtonStyleCatalog
{
    public static readonly string[] Values =
        ["Gradient", "Frosted", "Outline", "Flat", "Nebula", "Chrome", "Jelly", "Paws", "Arcade"];
    private static readonly string[] Titles =
        ["Градиент", "Матовое стекло", "Контур", "Без подложки",
         "Неоновое стекло", "Жидкий хром", "Желейки", "Котолапки", "Ретро-аркада"];
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.Ordinal);

    private static ImageSource Open(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri("pack://application:,,,/Assets/ButtonStyles/" + path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return Cache[path] = bitmap;
    }

    public static IReadOnlyList<ButtonVisualOption> Options { get; } =
        Values.Select((key, i) => new ButtonVisualOption(key, Titles[i], Open("Preview/" + key + ".png"))).ToArray();

    public static bool IsArtwork(string style) => Array.IndexOf(Values, style) >= 4;

    public static bool SupportsColorCustomization(string style) => !IsArtwork(style);
    public static bool SupportsShapeCustomization(string style) => !IsArtwork(style);
    public static int IndexOf(string style) => Math.Max(0, Array.IndexOf(Values, style));

    public static ImageSource? GetImage(string style, string action)
        => IsArtwork(style) ? Open(style + "/" + action + ".png") : null;
}
