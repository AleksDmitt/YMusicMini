using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using YMusicMini.Models;
using YMusicMini.Services;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Window = System.Windows.Window;
using ComboBox = System.Windows.Controls.ComboBox;

namespace YMusicMini;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly string[] _sources;
    private readonly string[] _audioProcesses;
    private readonly AccentPaletteService _paletteService = new();
    private readonly ImageSource? _cover;
    private readonly bool _previewPlaying;
    private readonly Color? _actualCapsuleBackground;
    private bool _resetPositionOnSave;
    private readonly Dictionary<Slider, DateTime> _sliderHoverStarts = new();
    private const int WheelHoverDelayMs = 220;

    // Native Windows 11 title bar follows the chosen light/dark menu theme.
    // DWM is optional: on older Windows versions the standard system caption remains.
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void UpdateNativeTitleBar(bool dark)
    {
        if (!OperatingSystem.IsWindows()) return;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int value = dark ? 1 : 0;
            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref value, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
    public bool ResetPositionRequested => _resetPositionOnSave;
    public bool Saved { get; private set; }
    public SettingsWindow(AppSettings settings, string[] sources, string[] audioProcesses, ImageSource? cover = null,
        string? trackTitle = null, string? artistName = null, bool isPlaying = false,
        Color? actualCapsuleBackground = null)
    {
        InitializeComponent();
        _settings = settings;
        _sources = sources;
        _audioProcesses = audioProcesses;
        _cover = cover;
        _previewPlaying = isPlaying;
        _actualCapsuleBackground = actualCapsuleBackground;
        PreviewTitle.Text = string.IsNullOrWhiteSpace(trackTitle) ? "Яндекс Музыка" : trackTitle;
        PreviewArtist.Text = string.IsNullOrWhiteSpace(artistName) ? "Нет исполнителя" : artistName;
        PreviewSmallCoverImage.Source = cover;

        ThemeCombo.ItemsSource = new[] {
            "Адаптивная (по активному окну)", "По окружению капсулы", "По обложке трека",
            "Системная", "Светлая", "Тёмная" };
        AccentCombo.ItemsSource = new[] { "Стандартный градиент", "Акцент Windows", "Градиент обоев", "Градиент обложки" };
        PlayHotkeyCombo.ItemsSource = new[] { "Нет", "Ctrl+Alt+P", "Ctrl+Shift+P", "Ctrl+Alt+Space", "Ctrl+Shift+Space" };
        PreviousHotkeyCombo.ItemsSource = new[] { "Нет", "Ctrl+Alt+Left", "Ctrl+Shift+Left", "Ctrl+Alt+J" };
        NextHotkeyCombo.ItemsSource = new[] { "Нет", "Ctrl+Alt+Right", "Ctrl+Shift+Right", "Ctrl+Alt+K" };
        PlayHotkeyCombo.SelectedItem = settings.PlayPauseHotkey;
        PreviousHotkeyCombo.SelectedItem = settings.PreviousHotkey;
        NextHotkeyCombo.SelectedItem = settings.NextHotkey;
        ButtonShapeCombo.ItemsSource = new[] { "Круглые", "Скруглённые", "Почти квадратные" };
        ButtonOrderCombo.ItemsSource = new[] {
            "⏮  ▶/⏸  ⏭", "▶/⏸  ⏮  ⏭", "⏮  ⏭  ▶/⏸" };
        ButtonVisualCombo.ItemsSource = ButtonStyleCatalog.Options;
        SettingsThemeCombo.ItemsSource = new[] { "Системная", "Светлая", "Тёмная" };
        VolumeModeCombo.ItemsSource = new[] { "Только Яндекс Музыка", "Общая громкость Windows" };
        SourceCombo.ItemsSource = (new[] { "Автоматически (Yandex)" }).Concat(_sources).ToArray();
        AudioProcessCombo.ItemsSource = (new[] { "Автоматически (Yandex Music)" }).Concat(_audioProcesses).ToArray();

        ThemeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(
            new[] { "Adaptive", "Surroundings", "Album", "System", "Light", "Dark" }, settings.ThemeMode));
        SurroundingIntensitySlider.Value = Math.Clamp(settings.SurroundingIntensity, 0, 100);
        int accentIndex = Array.IndexOf(new[] { "Fixed", "Windows", "Wallpaper", "Album", "WallpaperGradient" }, settings.AccentMode);
        AccentCombo.SelectedIndex = accentIndex < 0 || accentIndex == 4 ? 2 : accentIndex;
        ButtonShapeCombo.SelectedIndex = settings.ButtonShape switch
        {
            "Rounded" => 1, "Square" => 2, _ => 0
        };
        ButtonOrderCombo.SelectedIndex = settings.ButtonOrder switch
        {
            "PlayPause,Previous,Next" => 1,
            "Previous,Next,PlayPause" => 2,
            _ => 0
        };
        ButtonVisualCombo.SelectedIndex = ButtonStyleCatalog.IndexOf(settings.ButtonVisual);
        SettingsThemeCombo.SelectedIndex = settings.SettingsTheme switch
        {
            "Light" => 1, "Dark" => 2, _ => 0
        };
        VolumeModeCombo.SelectedIndex = settings.VolumeTarget == "System" ? 1 : 0;
        SourceCombo.SelectedIndex = settings.SelectedSourceId is null ? 0 : Math.Max(0, Array.FindIndex(_sources, s => string.Equals(s, settings.SelectedSourceId, StringComparison.OrdinalIgnoreCase)) + 1);
        AudioProcessCombo.SelectedIndex = settings.AudioProcessName is null ? 0 : Math.Max(0, Array.FindIndex(_audioProcesses, s => string.Equals(s, settings.AudioProcessName, StringComparison.OrdinalIgnoreCase)) + 1);
        LockCheck.IsChecked = settings.PositionLocked;
        TopmostCheck.IsChecked = settings.Topmost;
        CollapseCheck.IsChecked = settings.AutoCollapse;
        AnimateWindowCheck.IsChecked = settings.AnimateWindow;
        WindowAnimationSlider.Value = Math.Clamp(settings.WindowAnimationMs, 140, 500);

        AnimateWindowCheck.Checked += (_, _) => WindowAnimationSlider.IsEnabled = true;
        AnimateWindowCheck.Unchecked += (_, _) => WindowAnimationSlider.IsEnabled = false;
        WindowAnimationSlider.IsEnabled = settings.AnimateWindow;
        TransparencySlider.Value = Math.Clamp((1 - settings.Opacity) * 100, 0, 60);
        ExpandDelaySlider.Value = settings.ExpandDelayMs;
        CollapseDelaySlider.Value = settings.CollapseDelayMs;
        StartupCheck.IsChecked = settings.StartWithWindows;
        HideYandexCheck.IsChecked = settings.HideWhileYandexFocused;
        HideClosedCheck.IsChecked = settings.HideWhenYandexClosed;
        HideInactiveCheck.IsChecked = settings.HideWhenNotPlaying;
        HideFullscreenCheck.IsChecked = settings.HideOverFullscreen;
        HotkeyCheck.IsChecked = settings.EnableGlobalHotkeys;

        // The preview is local to this window: Cancel never changes the running widget.
        PreviewLightCheck.IsChecked = _actualCapsuleBackground is { } current
            ? GetDarkInkPreference(current) : ForegroundThemeService.SystemIsLight();
        PreviewCoverImage.Source = _cover;
        PreviewCoverPlaceholder.Visibility = _cover is null ? Visibility.Visible : Visibility.Collapsed;
        ButtonVisualCombo.SelectionChanged += (_, _) =>
        {
            UpdateButtonCustomizationAvailability();
            UpdatePreview();
        };
        SettingsThemeCombo.SelectionChanged += (_, _) => ApplySettingsWindowTheme();
        ButtonShapeCombo.SelectionChanged += (_, _) => UpdatePreview();
        ButtonOrderCombo.SelectionChanged += (_, _) => UpdatePreview();
        ThemeCombo.SelectionChanged += (_, _) => UpdatePreview();
        SurroundingIntensitySlider.ValueChanged += (_, _) => UpdatePreview();
        AccentCombo.SelectionChanged += (_, _) => UpdatePreview();
        TransparencySlider.ValueChanged += (_, _) => UpdatePreview();
        PreviewLightCheck.Checked += (_, _) => UpdatePreview();
        PreviewLightCheck.Unchecked += (_, _) => UpdatePreview();
        PreviewExpandedCheck.Checked += (_, _) => UpdatePreview();
        PreviewExpandedCheck.Unchecked += (_, _) => UpdatePreview();
        foreach (var slider in new[] { TransparencySlider, SurroundingIntensitySlider,
                     WindowAnimationSlider, ExpandDelaySlider, CollapseDelaySlider })
            slider.ValueChanged += (_, _) => UpdateValueLabels();
        RegisterWheelSliders();
        SourceInitialized += (_, _) => UpdateNativeTitleBar(
            SettingsThemeCombo.SelectedIndex switch
            {
                1 => false,
                2 => true,
                _ => !ForegroundThemeService.SystemIsLight()
            });
        ApplySettingsWindowTheme();
        UpdateValueLabels();
        UpdatePreview();
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Fixed, non-maximizable dialog. On compact displays only its scroll area shrinks.
        var area = SystemParameters.WorkArea;
        Height = Math.Min(760, Math.Max(400, area.Height - 40));
        Width = Math.Min(585, Math.Max(380, area.Width - 40));
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    // Wheel changes selection ONLY while the list is explicitly opened.
    // Otherwise a closed combobox passes the action to the page scroller.
    private void Combo_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: false })
        {
            ScrollSettingsPage(e.Delta);
            e.Handled = true;
        }
    }

    private void ScrollSettingsPage(int delta) =>
        SettingsScroll.ScrollToVerticalOffset(
            Math.Clamp(SettingsScroll.VerticalOffset - Math.Sign(delta) * 52,
                0, SettingsScroll.ScrollableHeight));

    private void ApplySettingsWindowTheme()
    {
        bool light = SettingsThemeCombo.SelectedIndex switch
        {
            1 => true,
            2 => false,
            _ => ForegroundThemeService.SystemIsLight()
        };
        void Set(string key, byte r, byte g, byte b) =>
            Resources[key] = new SolidColorBrush(Color.FromRgb(r, g, b));
        if (light)
        {
            Set("SettingsWindowBackground", 243, 245, 249);
            Set("SettingsCardBackground", 255, 255, 255);
            Set("SettingsPreviewPanel", 232, 237, 246);
            Set("SettingsText", 28, 38, 53);
            Set("SettingsSubtext", 89, 101, 121);
            Set("SettingsBorder", 214, 222, 235);
            Set("SettingsControlBackground", 249, 251, 254);
            Set("SettingsButtonBackground", 235, 239, 247);
            Set("SettingsAccent", 98, 80, 177);
            Set("SettingsHoverBackground", 233, 229, 251);
            Set("SettingsSelectedBackground", 221, 215, 246);
            Set("SettingsRailBackground", 211, 218, 232);
        }
        else
        {
            Set("SettingsWindowBackground", 23, 27, 34);
            Set("SettingsCardBackground", 34, 39, 49);
            Set("SettingsPreviewPanel", 42, 48, 61);
            Set("SettingsText", 237, 241, 248);
            Set("SettingsSubtext", 168, 179, 197);
            Set("SettingsBorder", 65, 73, 89);
            Set("SettingsControlBackground", 46, 53, 66);
            Set("SettingsButtonBackground", 54, 62, 77);
            Set("SettingsAccent", 156, 132, 241);
            Set("SettingsHoverBackground", 61, 54, 91);
            Set("SettingsSelectedBackground", 70, 61, 110);
            Set("SettingsRailBackground", 69, 77, 98);
        }
        UpdateNativeTitleBar(!light);
    }

    // Случайная прокрутка страницы над ползунком не должна менять настройки.
    // Колесо ползунка активируется после 220 мс непрерывного наведения.
    private void RegisterWheelSliders()
    {
        Slider[] sliders = [TransparencySlider, SurroundingIntensitySlider,
            WindowAnimationSlider, ExpandDelaySlider, CollapseDelaySlider];
        foreach (var slider in sliders)
        {
            slider.MouseEnter += (_, _) => _sliderHoverStarts[slider] = DateTime.UtcNow;
            slider.MouseLeave += (_, _) => _sliderHoverStarts.Remove(slider);
            slider.PreviewMouseWheel += Slider_PreviewMouseWheel;
        }
    }

    private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsEnabled) return;
        if (!_sliderHoverStarts.TryGetValue(slider, out DateTime start) ||
            (DateTime.UtcNow - start).TotalMilliseconds < WheelHoverDelayMs)
        {
            ScrollSettingsPage(e.Delta);
            e.Handled = true;
            return;
        }
        slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta) * Math.Max(5, slider.TickFrequency),
            slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    private void UpdateValueLabels()
    {
        SurroundingValueLabel.Text = $"{SurroundingIntensitySlider.Value:0}%";
        TransparencyValueLabel.Text = $"{TransparencySlider.Value:0}%";
        AnimationDurationText.Text = $"{WindowAnimationSlider.Value:0} мс";
        ExpandDelayValueLabel.Text = $"{ExpandDelaySlider.Value:0} мс";
        CollapseDelayValueLabel.Text = $"{CollapseDelaySlider.Value:0} мс";
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        // Пока пользователь не нажал «Сохранить», реальное приложение не меняется.
        var defaults = new AppSettings();
        _resetPositionOnSave = true;
        ThemeCombo.SelectedIndex = 0;
        AccentCombo.SelectedIndex = 2;
        ButtonVisualCombo.SelectedIndex = ButtonStyleCatalog.IndexOf(defaults.ButtonVisual);
        ButtonShapeCombo.SelectedIndex = 0;
        ButtonOrderCombo.SelectedIndex = 0;
        SettingsThemeCombo.SelectedIndex = 0;
        SurroundingIntensitySlider.Value = defaults.SurroundingIntensity;
        TransparencySlider.Value = Math.Round((1 - defaults.Opacity) * 100);
        WindowAnimationSlider.Value = defaults.WindowAnimationMs;
        ExpandDelaySlider.Value = defaults.ExpandDelayMs;
        CollapseDelaySlider.Value = defaults.CollapseDelayMs;
        AnimateWindowCheck.IsChecked = defaults.AnimateWindow;
        LockCheck.IsChecked = defaults.PositionLocked;
        TopmostCheck.IsChecked = defaults.Topmost;
        CollapseCheck.IsChecked = defaults.AutoCollapse;
        StartupCheck.IsChecked = defaults.StartWithWindows;
        HideYandexCheck.IsChecked = defaults.HideWhileYandexFocused;
        HideClosedCheck.IsChecked = defaults.HideWhenYandexClosed;
        HideInactiveCheck.IsChecked = defaults.HideWhenNotPlaying;
        HideFullscreenCheck.IsChecked = defaults.HideOverFullscreen;
        HotkeyCheck.IsChecked = defaults.EnableGlobalHotkeys;
        PlayHotkeyCombo.SelectedItem = defaults.PlayPauseHotkey;
        PreviousHotkeyCombo.SelectedItem = defaults.PreviousHotkey;
        NextHotkeyCombo.SelectedItem = defaults.NextHotkey;
        VolumeModeCombo.SelectedIndex = 0;
        SourceCombo.SelectedIndex = 0;
        AudioProcessCombo.SelectedIndex = 0;
        UpdatePreview();
        UpdateValueLabels();
    }

    private void UpdateButtonCustomizationAvailability()
    {
        // PNG packs have a baked-in silhouette and color. Keep stored choices
        // intact so they are restored when the user returns to a vector style.
        string style = ButtonStyleCatalog.Values[Math.Clamp(ButtonVisualCombo.SelectedIndex, 0,
            ButtonStyleCatalog.Values.Length - 1)];
        ButtonColorOptions.Visibility = ButtonStyleCatalog.SupportsColorCustomization(style)
            ? Visibility.Visible : Visibility.Collapsed;
        ButtonShapeOptions.Visibility = ButtonStyleCatalog.SupportsShapeCustomization(style)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePreview()
    {
        if (!IsLoaded && PreviewCapsule is null) return;
        var mode = new[] { "Adaptive", "Surroundings", "Album", "System", "Light", "Dark" }[
            Math.Clamp(ThemeCombo.SelectedIndex, 0, 5)];
        bool sceneLight = PreviewLightCheck.IsChecked == true;
        bool light = mode switch
        {
            "Light" => true,
            "Dark" => false,
            "System" => ForegroundThemeService.SystemIsLight(),
            _ => sceneLight
        };
        var accentMode = new[] { "Fixed", "Windows", "Wallpaper", "Album" }[
            Math.Clamp(AccentCombo.SelectedIndex, 0, 3)];
        var colors = _paletteService.Read(accentMode, _cover);
        var gradient = new LinearGradientBrush(
            new GradientStopCollection {
                new GradientStop(colors[0], 0),
                new GradientStop(colors[1], 0.5),
                new GradientStop(colors[2], 1)
            }, new Point(0, 0), new Point(1, 1));
        PreviewProgressFill.Background = gradient;
        var styleKey = ButtonStyleCatalog.Values[Math.Clamp(ButtonVisualCombo.SelectedIndex, 0,
            ButtonStyleCatalog.Values.Length - 1)];
        bool useArtwork = ButtonStyleCatalog.IsArtwork(styleKey);
        void SetArtwork(System.Windows.Controls.Image image, FrameworkElement vector, string action)
        {
            image.Source = useArtwork ? ButtonStyleCatalog.GetImage(styleKey, action) : null;
            image.Visibility = useArtwork ? Visibility.Visible : Visibility.Collapsed;
            vector.Visibility = useArtwork ? Visibility.Collapsed : Visibility.Visible;
        }
        SetArtwork(PreviewPrevArtwork, PreviewPrevVector, "Previous");
        SetArtwork(PreviewPlayArtwork, PreviewPlayVector, _previewPlaying ? "Pause" : "Play");
        SetArtwork(PreviewNextArtwork, PreviewNextVector, "Next");
        PreviewPlayIcon.Data = (System.Windows.Media.Geometry)FindResource(
            _previewPlaying ? "Midnight.Icon.Pause" : "Midnight.Icon.Play");

        Color scene = sceneLight ? Color.FromRgb(244, 247, 251) : Color.FromRgb(38, 45, 60);
        Color background = light ? Color.FromRgb(232, 238, 249) : Color.FromRgb(29, 33, 43);
        if (mode == "Surroundings")
        {
            scene = sceneLight ? Color.FromRgb(226, 237, 247) : Color.FromRgb(40, 49, 69);
            background = SurroundingThemeService.GetCapsuleBackground(scene,
                SurroundingIntensitySlider.Value, sceneLight);
            background = CapsuleContrastPolicy.Background(background, sceneLight);
            light = GetDarkInkPreference(background);
        }
        else if (mode == "Album")
        {
            var album = _paletteService.Read("Album", _cover);
            var sample = Color.FromRgb(
                (byte)((album[0].R + album[1].R + album[2].R) / 3),
                (byte)((album[0].G + album[1].G + album[2].G) / 3),
                (byte)((album[0].B + album[1].B + album[2].B) / 3));
            bool sampleLight = SurroundingThemeService.Brightness(sample) >= 139;
            background = CapsuleContrastPolicy.Background(
                SurroundingThemeService.GetCapsuleBackground(sample, 78, sampleLight), sampleLight);
            light = GetDarkInkPreference(background);
        }
        // When the dialog first opens, show the same background as the actual widget,
        // including its surroundings-derived tint rather than the OS theme.
        bool untouched = mode == _settings.ThemeMode &&
            (mode != "Surroundings" || Math.Abs(SurroundingIntensitySlider.Value - _settings.SurroundingIntensity) < 0.01);
        if (untouched && _actualCapsuleBackground is { } realBackground)
        {
            background = realBackground;
            light = GetDarkInkPreference(background);
        }
        var inkColor = light ? Color.FromRgb(23, 30, 42) : Color.FromRgb(250, 251, 254);
        var ink = new SolidColorBrush(inkColor);
        var look = ButtonAppearanceFactory.Create(useArtwork ? "Flat" : styleKey, colors, light);
        var buttons = new[] { PreviewPrevButton, PreviewPlayButton, PreviewNextButton };
        for (int i = 0; i < buttons.Length; i++)
        {
            bool primary = i == 1;
            buttons[i].Background = primary ? look.PrimaryFill : look.SecondaryFill;
            buttons[i].BorderBrush = primary ? look.PrimaryBorder : look.SecondaryBorder;
            buttons[i].BorderThickness = primary ? look.PrimaryThickness : look.SecondaryThickness;
            buttons[i].CornerRadius = ButtonShapeCombo.SelectedIndex switch
            {
                1 => new CornerRadius(9),
                2 => new CornerRadius(4),
                _ => new CornerRadius(18)
            };
        }
        PreviewPlayButton.Effect = look.GlowOpacity > 0
            ? new System.Windows.Media.Effects.DropShadowEffect
              {
                  Color = colors[1], BlurRadius = 9, ShadowDepth = 0,
                  Opacity = look.GlowOpacity
              }
            : null;
        Brush buttonInk = look.Glyph;
        PreviewPrevIcon.Fill = buttonInk;
        PreviewPlayIcon.Fill = buttonInk;
        PreviewNextIcon.Fill = buttonInk;
        PreviewFrame.Background = new SolidColorBrush(scene);
        PreviewCapsule.Background = new SolidColorBrush(background);
        PreviewCapsule.BorderBrush = new SolidColorBrush(CapsuleContrastPolicy.Outline(background, light));
        PreviewTitle.Foreground = ink;
        PreviewArtist.Foreground = new SolidColorBrush(SurroundingThemeService.Mix(inkColor, background, .23));
        bool expanded = PreviewExpandedCheck.IsChecked == true;
        // Preview uses the same rule as the real player: compact mode can be
        // transparent, while expanded mode is fully opaque.
        PreviewCapsule.Opacity = expanded ? 1.0 : Math.Clamp(1 - TransparencySlider.Value / 100, .4, 1);
        // Mini remains at its natural size; full view gets only the space needed
        // to display the complete 480×220 mockup at a readable scale.
        PreviewFrame.Height = expanded ? 194 : 90;
        PreviewMiniRow.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        PreviewExpandedDetails.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        var currentParent = System.Windows.Media.VisualTreeHelper.GetParent(PreviewButtonsRow);
        var target = expanded ? PreviewExpandedButtonsRow : PreviewMiniRow;
        if (!ReferenceEquals(currentParent, target))
        {
            if (currentParent is System.Windows.Controls.Panel oldPanel) oldPanel.Children.Remove(PreviewButtonsRow);
            if (expanded) target.Children.Add(PreviewButtonsRow);
            else PreviewMiniRow.Children.Insert(1, PreviewButtonsRow);
        }
        PreviewPlayButton.Width = 36;
        PreviewPlayButton.Height = 36;
        PreviewPrevButton.Width = PreviewNextButton.Width = 36;
        PreviewPrevButton.Height = PreviewNextButton.Height = 36;
        PreviewPrevButton.Margin = expanded ? new Thickness(0,0,16,0) : new Thickness(0,0,7,0);
        PreviewNextButton.Margin = expanded ? new Thickness(0,0,0,0) : new Thickness(0,0,7,0);
        PreviewPlayButton.Margin = expanded ? new Thickness(0,0,16,0) : new Thickness(0,0,8,0);
        PreviewCapsule.Width = expanded ? 480 : 280;
        PreviewCapsule.Height = expanded ? 220 : 56;
        PreviewCapsule.CornerRadius = new CornerRadius(28);
        var buttonOrder = ButtonOrderCombo.SelectedIndex switch
        {
            1 => new[] { PreviewPlayButton, PreviewPrevButton, PreviewNextButton },
            2 => new[] { PreviewPrevButton, PreviewNextButton, PreviewPlayButton },
            _ => new[] { PreviewPrevButton, PreviewPlayButton, PreviewNextButton }
        };
        foreach (var b in buttonOrder) PreviewButtonsRow.Children.Remove(b);
        foreach (var b in buttonOrder) PreviewButtonsRow.Children.Add(b);
        UpdateButtonCustomizationAvailability();
        SurroundingOptions.Visibility = mode == "Surroundings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool GetDarkInkPreference(Color c)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B) > .179;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.ThemeMode = new[] { "Adaptive", "Surroundings", "Album", "System", "Light", "Dark" }[
            Math.Clamp(ThemeCombo.SelectedIndex, 0, 5)];
        _settings.SurroundingIntensity = SurroundingIntensitySlider.Value;
        _settings.AccentMode = new[] { "Fixed", "Windows", "Wallpaper", "Album" }[Math.Max(0, AccentCombo.SelectedIndex)];
        _settings.ButtonShape = new[] { "Circle", "Rounded", "Square" }[Math.Clamp(ButtonShapeCombo.SelectedIndex, 0, 2)];
        _settings.ButtonVisual = ButtonStyleCatalog.Values[
            Math.Clamp(ButtonVisualCombo.SelectedIndex, 0, ButtonStyleCatalog.Values.Length - 1)];
        _settings.SettingsTheme = new[] { "System", "Light", "Dark" }
            [Math.Clamp(SettingsThemeCombo.SelectedIndex, 0, 2)];
        if (_resetPositionOnSave)
        {
            _settings.CompactLeft = null;
            _settings.CompactTop = null;
        }
        _settings.ButtonOrder = new[] { "Previous,PlayPause,Next", "PlayPause,Previous,Next", "Previous,Next,PlayPause" }
            [Math.Clamp(ButtonOrderCombo.SelectedIndex, 0, 2)];
        _settings.VolumeTarget = VolumeModeCombo.SelectedIndex == 1 ? "System" : "Application";
        _settings.SelectedSourceId = SourceCombo.SelectedIndex <= 0 ? null : _sources[SourceCombo.SelectedIndex - 1];
        _settings.AudioProcessName = AudioProcessCombo.SelectedIndex <= 0 ? null : _audioProcesses[AudioProcessCombo.SelectedIndex - 1];
        _settings.PositionLocked = LockCheck.IsChecked == true;
        _settings.Topmost = TopmostCheck.IsChecked == true;
        _settings.AutoCollapse = CollapseCheck.IsChecked == true;
        _settings.AnimateWindow = AnimateWindowCheck.IsChecked == true;
        _settings.WindowAnimationMs = (int)WindowAnimationSlider.Value;
        _settings.Opacity = 1 - TransparencySlider.Value / 100;
        _settings.ExpandDelayMs = (int)ExpandDelaySlider.Value;
        _settings.CollapseDelayMs = (int)CollapseDelaySlider.Value;
        _settings.StartWithWindows = StartupCheck.IsChecked == true;
        _settings.HideWhileYandexFocused = HideYandexCheck.IsChecked == true;
        _settings.HideWhenYandexClosed = HideClosedCheck.IsChecked == true;
        _settings.HideWhenNotPlaying = HideInactiveCheck.IsChecked == true;
        _settings.HideOverFullscreen = HideFullscreenCheck.IsChecked == true;
        _settings.EnableGlobalHotkeys = HotkeyCheck.IsChecked == true;
        _settings.PlayPauseHotkey = PlayHotkeyCombo.SelectedItem?.ToString() ?? "Нет";
        _settings.PreviousHotkey = PreviousHotkeyCombo.SelectedItem?.ToString() ?? "Нет";
        _settings.NextHotkey = NextHotkeyCombo.SelectedItem?.ToString() ?? "Нет";
        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
