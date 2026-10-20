using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using System.Windows;
using System.Windows.Media;

namespace YMusicMini.Services;

/// <summary>
/// Single appearance calculation shared by the actual WPF transport controls and
/// their settings preview. Images from PNG packs remain untouched.
/// </summary>
public sealed record ButtonAppearance(
    Brush SecondaryFill, Brush PrimaryFill, Brush SecondaryBorder, Brush PrimaryBorder,
    Thickness SecondaryThickness, Thickness PrimaryThickness, Brush Glyph, double GlowOpacity);

public static class ButtonAppearanceFactory
{
    public static ButtonAppearance Create(string style, Color[] palette, bool light)
    {
        if (palette.Length < 3) throw new ArgumentException("Three accent colors required.", nameof(palette));
        // Mixed wallpapers can contain light and dark colors. Keep glyphs legible.
        bool darkGlyph = RelativeLuminance(palette[1]) > 0.38;
        Brush onAccent = new SolidColorBrush(darkGlyph ? Color.FromRgb(23, 31, 49) : Colors.White);
        Brush defaultGlyph = new SolidColorBrush(light ? Color.FromRgb(35, 43, 57) : Color.FromRgb(246, 248, 253));
        Brush clear = Brushes.Transparent;
        var gradient = Gradient(palette);
        // Keep the outline separated from the edge by using the same radius for
        // the content rectangle and the border; no layered circles or stacked rims.
        var borderGradient = Gradient(palette);
        switch (style)
        {
            case "Gradient":
                // An actual gradient FACE, not a narrow outline masquerading as a fill.
                return new(gradient, Gradient(palette), clear, clear,
                    new Thickness(0), new Thickness(0), onAccent, 0.13);
            case "Outline":
                return new(clear, clear, borderGradient, Gradient(palette),
                    new Thickness(1.25), new Thickness(1.25), defaultGlyph, 0.0);
            case "Frosted":
                Brush frost = new SolidColorBrush(light
                    ? Color.FromArgb(242, 231, 236, 247)
                    : Color.FromArgb(237, 54, 65, 88));
                Brush frostBorder = new SolidColorBrush(light
                    ? Color.FromRgb(126, 139, 163)
                    : Color.FromRgb(122, 138, 169));
                return new(frost, frost, frostBorder, frostBorder,
                    new Thickness(0.9), new Thickness(0.9), defaultGlyph, 0.0);
            default: // Flat and PNG sprite styles use transparent faces.
                return new(clear, clear, clear, clear,
                    new Thickness(0), new Thickness(0), defaultGlyph, 0);
        }
    }

    private static LinearGradientBrush Gradient(Color[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(colors[0], 0));
        brush.GradientStops.Add(new GradientStop(colors[1], 0.5));
        brush.GradientStops.Add(new GradientStop(colors[2], 1));
        return brush;
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte value)
        {
            double v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
