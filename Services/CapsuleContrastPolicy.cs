using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Media;

namespace YMusicMini.Services;

public static class CapsuleContrastPolicy
{
    // Retain the sampled hue, but keep light glass from disappearing on white pages.
    public static Color Background(Color candidate, bool light)
    {
        var target = light ? Color.FromRgb(118, 139, 190) : Color.FromRgb(184, 194, 232);
        return SurroundingThemeService.Mix(candidate, target, light ? 0.15 : 0.07);
    }

    public static Color Outline(Color background, bool light)
        => light ? SurroundingThemeService.Mix(background, Color.FromRgb(44, 65, 112), 0.66)
                 : SurroundingThemeService.Mix(background, Color.FromRgb(196, 200, 255), 0.52);
}
