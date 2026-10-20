namespace YMusicMini.Models;

public sealed class AppSettings
{
    public double? CompactLeft { get; set; }
    public double? CompactTop { get; set; }
    public bool Topmost { get; set; } = true;
    public bool PositionLocked { get; set; } = false;
    public int ExpandDelayMs { get; set; } = 300;
    public int CollapseDelayMs { get; set; } = 300;
    public bool AutoCollapse { get; set; } = true;
    public bool AnimateWindow { get; set; } = true;
    public int WindowAnimationMs { get; set; } = 260;
    public int ExpandedCoverSize { get; set; } = 120;
    public string ThemeMode { get; set; } = "Adaptive";
    public double SurroundingIntensity { get; set; } = 65;
    public bool HideWhenYandexClosed { get; set; } = true;
    public string AccentMode { get; set; } = "Wallpaper";
    // Appearance personalization. Old settings.json files keep working.
    public string ButtonShape { get; set; } = "Circle";
    public string ButtonVisual { get; set; } = "Gradient";
    public string SettingsTheme { get; set; } = "System";
    public string ButtonOrder { get; set; } = "Previous,PlayPause,Next";
    public string VolumeTarget { get; set; } = "Application";
    public string? AudioProcessName { get; set; }
    // Keep persisted field for compatibility with older settings.json files.
    public double Opacity { get; set; } = 0.92;
    public bool StartWithWindows { get; set; }
    public bool HideWhileYandexFocused { get; set; }
    public bool HideWhenNotPlaying { get; set; }
    public bool HideOverFullscreen { get; set; }
    public bool EnableGlobalHotkeys { get; set; }
    public string PlayPauseHotkey { get; set; } = "Ctrl+Alt+P";
    public string PreviousHotkey { get; set; } = "Ctrl+Alt+Left";
    public string NextHotkey { get; set; } = "Ctrl+Alt+Right";

    public string? SelectedSourceId { get; set; }
}
