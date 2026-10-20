using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Media3D;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Slider = System.Windows.Controls.Slider;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Window = System.Windows.Window;
using YMusicMini.Models;
using YMusicMini.Services;

namespace YMusicMini;

public partial class MainWindow : Window
{
    private const double CompactWidth = 280;
    private const double CompactHeight = 56;
    private const double ExpandedWidth = 480;
    private const double ExpandedHeight = 220;

    private readonly SettingsService _settingsService = new();
    private readonly MediaSessionService _media = new();
    private readonly VolumeService _volume = new();
    private readonly ForegroundThemeService _foreground = new();
    private readonly SurroundingThemeService _surrounding = new();
    private readonly AccentPaletteService _palettes = new();
    private readonly DispatcherTimer _expandTimer = new();
    private readonly DispatcherTimer _collapseTimer = new();
    private readonly DispatcherTimer _pollTimer = new();
    private readonly DispatcherTimer _progressTimer = new();
    private readonly DispatcherTimer _themeTimer = new();
    private readonly DispatcherTimer _gradientTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly Stopwatch _gradientClock = new();
    private Color[] _gradientFrom = [];
    private Color[] _gradientTo = [];
    private readonly Stopwatch _progressClock = new();
    private readonly Stopwatch _windowAnimationClock = new();
    private bool _windowAnimating;
    private bool _windowAnimationExpanding;
    private Point _miniCoverPoint;
    private Point _fullCoverPoint;
    private bool _anchorRight;
    private bool _anchorBottom;
    private readonly ForegroundVisibilityService _visibility = new();
    // Prevent Win+D / Win+M from losing the floating window when it should be shown.
    private DesktopVisibilityService? _desktopVisibility;
    private GlobalHotkeysService? _hotkeys;
    private bool _manuallyHidden;
    private bool _automaticallyHidden;
    private bool _settingsDialogOpen;
    private SettingsWindow? _openSettings;
    private bool _started;
    private bool _draggingSeekThumb;
    private bool _pendingTrackClick;
    private double _pendingTrackValue;
    private bool? _requestedPlayState;
    private DateTime _playStateGuardUntilUtc;
    private string? _progressTrackKey;
    private bool _hasProgressSample;
    // Leave time for GSMTC/Yandex to publish the seek position after the command.
    private DateTime _seekGuardUntilUtc = DateTime.MinValue;
    private bool _awaitingSeekConfirmation;
    private DateTimeOffset _lastTimelineUpdate = DateTimeOffset.MinValue;
    private AppSettings _settings = new();
    private Forms.NotifyIcon? _tray;
    private bool _expanded;
    private bool _polling;
    private bool _closing;
    private bool _moving;
    private bool _seeking;
    private bool _syncVolume;
    private bool _volumeReady;
    private bool _playing;
    private double _duration;
    private double _basePosition;
    private double _compactLeft;
    private double _compactTop;
    private string[] _knownSources = [];
    private MediaSnapshot _lastMedia = MediaSnapshot.Empty;
    private bool? _lastThemeLight;
    private Color[]? _lastColors;
    private Color? _lastCapsuleBackground;
    private Color? _candidateCapsuleBackground;
    private int _candidateCapsuleCount;
    private ImageSource? _cachedAlbumThemeCover;
    private Color[]? _cachedAlbumThemePalette;

    public MainWindow()
    {
        InitializeComponent();
        _expandTimer.Interval = TimeSpan.FromMilliseconds(300);
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer.Stop();
            if (!_expanded && CompactExpand.IsMouseOver) SetExpanded(true);
        };
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(300);
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (_expanded && !IsMouseOver && !_moving) SetExpanded(false);
        };
        _pollTimer.Interval = TimeSpan.FromSeconds(1.5);
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(250);
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _themeTimer.Interval = TimeSpan.FromSeconds(1);
        _themeTimer.Tick += (_, _) =>
        {
            // Avoid screen sampling and color-brush animations during a
            // short, latency-sensitive window geometry transition.
            if (!_windowAnimating && !_settingsDialogOpen) ApplyAppearance();
            UpdateAutomaticVisibility();
        };
        _gradientTimer.Tick += (_, _) => UpdateGradientFrame();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;
        _settings = _settingsService.Load();
        if (_settings.AccentMode == "WallpaperGradient") _settings.AccentMode = "Wallpaper";
        _media.SelectedSourceId = _settings.SelectedSourceId;
        Topmost = _settings.Topmost;
        _expandTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.ExpandDelayMs, 0, 5000));
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.CollapseDelayMs, 0, 5000));
        SyncPinControls();
        ApplyButtonPersonalization();

        var area = WorkAreaFor(_settings.CompactLeft, _settings.CompactTop);
        _compactLeft = _settings.CompactLeft ?? (area.Right - CompactWidth - 16);
        _compactTop = _settings.CompactTop ?? (area.Bottom - CompactHeight - 16);
        _compactLeft = Math.Clamp(_compactLeft, area.Left, Math.Max(area.Left, area.Right - CompactWidth));
        _compactTop = Math.Clamp(_compactTop, area.Top, Math.Max(area.Top, area.Bottom - CompactHeight));
        // Reserve one unchanging native rectangle; unused pixels are fully
        // transparent, while the visible capsule is drawn at its saved anchor.
        UpdateFixedWindowPosition();
        SetWindowContentState(false);
        Opacity = Math.Clamp(_settings.Opacity, 0.4, 1.0);
        SetupTray();
        _desktopVisibility = new DesktopVisibilityService(this,
            () => _started && !_closing && !_manuallyHidden && !_automaticallyHidden && !_settingsDialogOpen,
            () => _settings.Topmost);
        _desktopVisibility.Start();
        _hotkeys = new GlobalHotkeysService(this, HotkeyInvoked);
        if (!_hotkeys.Configure(_settings))
            Debug.WriteLine("Глобальные сочетания заняты другой программой");
        ApplyAppearance();
        _progressTimer.Start();
        _pollTimer.Start();
        _themeTimer.Start();
        await PollAsync();
    }

    // Первичная привязка: размеры координат WPF в DIP; WorkArea API выдаёт физические пиксели.
    // Для необычной конфигурации с разным DPI на мониторах потребуется отдельная доработка.
    private Rect WorkAreaFor(double? windowLeft, double? windowTop)
    {
        double scaleX = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double scaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var screen = windowLeft.HasValue && windowTop.HasValue
            ? Forms.Screen.FromPoint(new System.Drawing.Point((int)(windowLeft.Value * scaleX), (int)(windowTop.Value * scaleY)))
            : Forms.Screen.PrimaryScreen;
        var r = screen?.WorkingArea ?? Forms.Screen.PrimaryScreen!.WorkingArea;
        return new Rect(r.Left / scaleX, r.Top / scaleY, r.Width / scaleX, r.Height / scaleY);
    }

    // Медиакнопки не раскрывают окно: стрелка открывает по клику либо по 300-мс наведению.
    private void ExpandButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_expanded) _expandTimer.Start();
    }
    private void ExpandButton_MouseLeave(object sender, MouseEventArgs e) => _expandTimer.Stop();
    private void Expand_Click(object sender, RoutedEventArgs e) => SetExpanded(true);
    private void Collapse_Click(object sender, RoutedEventArgs e) => SetExpanded(false);
    private void Window_MouseEnter(object sender, MouseEventArgs e) => _collapseTimer.Stop();
    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_expanded && _settings.AutoCollapse) _collapseTimer.Start();
    }

    // The compact rect is the only persisted geometry. The expanded rectangle
    // is computed from it, so dragging or resizing never changes the anchor.
    private Rect ExpandedRect()
    {
        var area = WorkAreaFor(_compactLeft, _compactTop);
        _anchorRight = _compactLeft + CompactWidth / 2 >= (area.Left + area.Right) / 2;
        _anchorBottom = _compactTop + CompactHeight / 2 >= (area.Top + area.Bottom) / 2;
        double x = _anchorRight ? _compactLeft + CompactWidth - ExpandedWidth : _compactLeft;
        double y = _anchorBottom ? _compactTop + CompactHeight - ExpandedHeight : _compactTop;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - ExpandedWidth));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - ExpandedHeight));
        return new Rect(x, y, ExpandedWidth, ExpandedHeight);
    }

    // The native window moves only when dragging or changing monitors; its
    // Width/Height remain 480 x 220 even in capsule mode. A layered transparent
    // window lets Explorer keep an unchanged HWND throughout the animation.
    private void UpdateFixedWindowPosition()
    {
        Rect bounds = ExpandedRect();
        Width = ExpandedWidth;
        Height = ExpandedHeight;
        Left = bounds.Left;
        Top = bounds.Top;
        double x = _compactLeft - bounds.Left;
        double y = _compactTop - bounds.Top;
        System.Windows.Controls.Canvas.SetLeft(CompactContainer, x);
        System.Windows.Controls.Canvas.SetTop(CompactContainer, y);
    }

    private static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private void UpdateShell(double expandedProgress)
    {
        double t = Math.Clamp(expandedProgress, 0, 1);
        double sx = CompactWidth / ExpandedWidth + (1 - CompactWidth / ExpandedWidth) * t;
        double sy = CompactHeight / ExpandedHeight + (1 - CompactHeight / ExpandedHeight) * t;
        double miniX = System.Windows.Controls.Canvas.GetLeft(CompactContainer);
        double miniY = System.Windows.Controls.Canvas.GetTop(CompactContainer);
        ShellScale.ScaleX = sx;
        ShellScale.ScaleY = sy;
        ShellOffset.X = miniX * (1 - t);
        ShellOffset.Y = miniY * (1 - t);
        // Subtle inner glass highlight follows the panel at every animation frame.
        GlassScale.ScaleX = sx;
        GlassScale.ScaleY = sy;
        GlassOffset.X = miniX * (1 - t);
        GlassOffset.Y = miniY * (1 - t);
        // Separate X/Y radii preserve a true pill in the 280x56 collapsed state.
        MainBorder.RadiusX = (28 / sx) * (1 - t) + 28 * t;
        MainBorder.RadiusY = (28 / sy) * (1 - t) + 28 * t;
    }

    // Opacity is a compact-mode preference. Expanded mode is fully opaque.
    private double CompactOpacity => Math.Clamp(_settings.Opacity, 0.4, 1.0);

    private void UpdatePlaybackTooltips()
    {
        string hint = _playing ? "Пауза" : "Воспроизвести";
        CompactToggle.ToolTip = hint;
        ExpandedToggle.ToolTip = hint;
    }

    private void SetExpanded(bool expand)
    {
        if (_expanded == expand) return;
        _expandTimer.Stop();
        _collapseTimer.Stop();
        if (_windowAnimating) FinishWindowAnimation();
        _expanded = expand;
        if (!_settings.AnimateWindow || !SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            SetWindowContentState(expand);
            return;
        }
        _windowAnimationExpanding = expand;
        _windowAnimating = true;
        CompactContainer.Visibility = Visibility.Visible;
        ExpandedContainer.Visibility = Visibility.Visible;
        CompactContainer.IsHitTestVisible = false;
        ExpandedContainer.IsHitTestVisible = false;
        // Determine artwork endpoints from the ACTUAL arranged controls.
        // No hardcoded cover coordinates that would jump on the last frame.
        WindowRoot.UpdateLayout();
        if (CompactView.Children[0] is FrameworkElement smallCover)
            _miniCoverPoint = smallCover.TransformToAncestor(WindowRoot).Transform(new Point(0, 0));
        _fullCoverPoint = ExpandedCoverImage.TransformToAncestor(WindowRoot).Transform(new Point(0, 0));
        MovingCover.Visibility = Visibility.Visible;
        MovingCover.Opacity = 1;
        MovingCoverImage.Source = _lastMedia.Cover;
        MovingCoverPlaceholder.Visibility = _lastMedia.Cover is null
            ? Visibility.Visible : Visibility.Collapsed;
        _windowAnimationClock.Restart();
        UpdateWindowAnimation();
        CompositionTarget.Rendering += WindowAnimation_Rendering;
    }

    private void SetWindowContentState(bool expanded)
    {
        CompactContainer.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedContainer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CompactContainer.Opacity = 1;
        ExpandedContainer.Opacity = 1;
        CompactContainer.IsHitTestVisible = true;
        ExpandedContainer.IsHitTestVisible = true;
        UpdateShell(expanded ? 1 : 0);
        MovingCover.Visibility = Visibility.Collapsed;
        MovingCover.Opacity = 1;
        _windowAnimating = false;
        Opacity = expanded ? 1.0 : CompactOpacity;
        SyncRepeatVisualState();
    }

    private void WindowAnimation_Rendering(object? sender, EventArgs e) => UpdateWindowAnimation();

    private void UpdateWindowAnimation()
    {
        if (!_windowAnimating) return;
        double duration = Math.Clamp(_settings.WindowAnimationMs, 140, 500);
        double linear = Math.Clamp(_windowAnimationClock.Elapsed.TotalMilliseconds / duration, 0, 1);
        double t = Ease(linear);
        double open = _windowAnimationExpanding ? t : 1 - t;
        // Render transforms only: NO HWND resizing and NO layout measurement
        // on every frame. It also keeps the anchor completely stationary.
        UpdateShell(open);
        // Same animation clock: the full player becomes opaque with no extra timer.
        Opacity = CompactOpacity + (1.0 - CompactOpacity) * open;

        double x = _miniCoverPoint.X + (_fullCoverPoint.X - _miniCoverPoint.X) * open;
        double y = _miniCoverPoint.Y + (_fullCoverPoint.Y - _miniCoverPoint.Y) * open;
        double coverScale = 40.0 / 136.0 + (1 - 40.0 / 136.0) * open;
        MovingCoverScale.ScaleX = coverScale;
        MovingCoverScale.ScaleY = coverScale;
        CoverOffset.X = x;
        CoverOffset.Y = y;
        // Avoid showing duplicate covers: the animated artwork is on top.
        CompactContainer.Opacity = Math.Clamp(1 - open / 0.4, 0, 1);
        ExpandedContainer.Opacity = Math.Clamp((open - 0.7) / 0.3, 0, 1);
        MovingCover.Opacity = Math.Clamp((1 - Math.Max(0, open - 0.9) / 0.1), 0, 1);
        if (linear >= 1) FinishWindowAnimation();
    }

    private void FinishWindowAnimation()
    {
        if (!_windowAnimating) return;
        CompositionTarget.Rendering -= WindowAnimation_Rendering;
        _windowAnimationClock.Stop();
        SetWindowContentState(_expanded);
    }

    private void SnapToExpandedPosition()
    {
        if (_windowAnimating) FinishWindowAnimation();
        UpdateFixedWindowPosition();
        SetWindowContentState(true);
    }

    private static bool OverInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or Slider) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_windowAnimating || _settings.PositionLocked || e.ClickCount != 1 ||
            OverInteractiveControl(e.OriginalSource as DependencyObject)) return;
        _collapseTimer.Stop();
        double previousX = Left, previousY = Top;
        _moving = true;
        try { DragMove(); }
        catch (InvalidOperationException) { /* не удерживается левая кнопка */ }
        finally { _moving = false; }
        _compactLeft += Left - previousX;
        _compactTop += Top - previousY;
        var area = WorkAreaFor(_compactLeft, _compactTop);
        _compactLeft = Math.Clamp(_compactLeft, area.Left, Math.Max(area.Left, area.Right - CompactWidth));
        _compactTop = Math.Clamp(_compactTop, area.Top, Math.Max(area.Top, area.Bottom - CompactHeight));
        _settings.CompactLeft = _compactLeft;
        _settings.CompactTop = _compactTop;
        UpdateFixedWindowPosition();
        SetWindowContentState(_expanded);
        SaveSettings();
    }

    private async Task PollAsync()
    {
        if (_polling || _closing) return;
        _polling = true;
        try
        {
            _knownSources = await _media.GetSourcesAsync();
            var data = await _media.ReadAsync();
            Render(data);
            UpdateVolume();
            UpdateAutomaticVisibility();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            TitleLabel.Text = "Ошибка получения медиасессии";
            ArtistLabel.Text = ex.Message.Length > 75 ? ex.Message[..75] : ex.Message;
            SetMediaControlsEnabled(false);
        }
        finally { _polling = false; }
    }

    private void Render(MediaSnapshot data)
    {
        // GSMTC can acknowledge play/pause with a delay. Display the requested state
        // immediately; fall back to the actual provider state after two seconds.
        if (_requestedPlayState.HasValue)
        {
            if (data.IsPlaying == _requestedPlayState.Value || DateTime.UtcNow >= _playStateGuardUntilUtc)
                _requestedPlayState = null;
            else
                data = data with { IsPlaying = _requestedPlayState.Value };
        }
        // If Yandex advanced to another track despite the local repeat request,
        // make one guarded attempt to return to the track that just ended.
        CheckRepeatTransition(data);
        bool changedCover = !ReferenceEquals(_lastMedia.Cover, data.Cover);
        double previousReportedPosition = _lastMedia.PositionSeconds;
        _lastMedia = data;
        TitleLabel.Text = data.Title;
        ArtistLabel.Text = data.Artist;
        var glyph = Geometry.Parse(data.IsPlaying ? "M6,5 H9.5 V19 H6 Z M14.5,5 H18 V19 H14.5 Z" : "M8,4.5 L20,12 8,19.5 Z");
        CompactPlayPath.Data = glyph;
        ExpandedPlayPath.Data = glyph;
        ApplyArtworkButtons(data.IsPlaying);
        CompactCoverBrush.ImageSource = data.Cover;
        ExpandedCoverImage.Source = data.Cover;
        if (_windowAnimating)
        {
            MovingCoverImage.Source = data.Cover;
            MovingCoverPlaceholder.Visibility = data.Cover is null
                ? Visibility.Visible : Visibility.Collapsed;
        }
        if (changedCover && data.Cover is not null)
        {
            ExpandedCoverImage.Opacity = 0;
            ExpandedCoverImage.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, SystemParameters.ClientAreaAnimation
                    ? TimeSpan.FromMilliseconds(220) : TimeSpan.Zero));
        }
        CompactCoverPlaceholder.Visibility = data.Cover is null ? Visibility.Visible : Visibility.Collapsed;
        ExpandedCoverPlaceholder.Visibility = data.Cover is null ? Visibility.Visible : Visibility.Collapsed;
        // Keep a local monotonic clock between GSMTC updates. Treat fresh, large jumps
        // as external seeks but do not reset on stale/lagging media timestamps.
        string trackKey = $"{data.SourceId}|{data.Title}|{data.Artist}|{data.DurationSeconds:F1}";
        bool sameTrack = data.HasSession && _hasProgressSample &&
            string.Equals(_progressTrackKey, trackKey, StringComparison.Ordinal);
        double predicted = _basePosition + (_playing ? _progressClock.Elapsed.TotalSeconds : 0);
        double reported = Math.Clamp(data.PositionSeconds, 0, Math.Max(0, data.DurationSeconds));
        bool freshTimeline = data.TimelineUpdatedAt > _lastTimelineUpdate;
        bool remotePositionMoved = Math.Abs(reported - previousReportedPosition) > 0.8;

        if (!sameTrack)
        {
            _basePosition = reported;
            _awaitingSeekConfirmation = false;
        }
        else if (_seeking)
        {
            // UI is owned by the user until the pointer is released and seek completes.
            _basePosition = predicted;
        }
        else if (_awaitingSeekConfirmation && DateTime.UtcNow < _seekGuardUntilUtc)
        {
            _basePosition = predicted;
            if (Math.Abs(reported - predicted) <= 1.5)
                _awaitingSeekConfirmation = false;
        }
        else if (_awaitingSeekConfirmation)
        {
            // If the provider never confirms the seek, resync rather than pretending it worked.
            _awaitingSeekConfirmation = false;
            _basePosition = reported;
        }
        else if (!data.IsPlaying || !_playing)
        {
            _basePosition = reported;
        }
        else if ((freshTimeline && Math.Abs(reported - predicted) > 2.5) ||
                 (remotePositionMoved && Math.Abs(reported - predicted) > 6))
        {
            _basePosition = reported; // Seek from another player or external timeline reset.
        }
        else
        {
            // Smooth small discrepancies; do not jump backwards on every polling cycle.
            double drift = reported - predicted;
            _basePosition = Math.Abs(drift) < 1.5 ? predicted + drift * 0.15 : predicted;
        }

        _lastTimelineUpdate = data.TimelineUpdatedAt;
        _progressTrackKey = data.HasSession ? trackKey : null;
        _hasProgressSample = data.HasSession;
        _duration = data.DurationSeconds;
        _playing = data.IsPlaying;
        UpdatePlaybackTooltips();
        _progressClock.Restart();
        SetMediaControlsEnabled(data.HasSession);
        UpdateProgress();
        if (changedCover && (_settings.AccentMode == "Album" || _settings.ThemeMode == "Album")) ApplyAppearance();
    }

    private void SetMediaControlsEnabled(bool value)
    {
        CompactPrev.IsEnabled = CompactToggle.IsEnabled = CompactNext.IsEnabled = value;
        ExpandedPrev.IsEnabled = ExpandedToggle.IsEnabled = ExpandedNext.IsEnabled = value;
        RepeatButton.IsEnabled = value;
        TrackProgress.IsEnabled = value && _duration > 0;
    }

    private void UpdateProgress()
    {
        if (_seeking) return;
        double now = _basePosition + (_playing ? _progressClock.Elapsed.TotalSeconds : 0);
        now = Math.Clamp(now, 0, Math.Max(0, _duration));
        // Leave room for GSMTC + the desktop client to process a seek before it
        // advances the playlist. This is a best-effort fallback, not gapless repeat.
        if (_localRepeatEnabled && !_repeatSeekInFlight && _playing && _lastMedia.HasSession &&
            !_awaitingSeekConfirmation && _duration > 8 &&
            now >= _duration - 2.0 && DateTime.UtcNow >= _repeatRetryAfterUtc)
            _ = RestartCurrentTrackAsync();
        TrackProgress.Maximum = Math.Max(1, _duration);
        TrackProgress.Value = now;
        PositionLabel.Text = FormatTime(now);
        DurationLabel.Text = FormatTime(_duration);
        TrackProgress.IsEnabled = _lastMedia.HasSession && _duration > 0;
    }

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "0:00";
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private static bool InVisualParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T) return true;
            if (element is not Visual && element is not Visual3D) break;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void Progress_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!TrackProgress.IsEnabled) return;
        if (InVisualParent<Thumb>(e.OriginalSource as DependencyObject))
        {
            // Let WPF drag the thumb normally, and seek when the drag ends.
            _seeking = true;
            _draggingSeekThumb = true;
            return;
        }

        // IsMoveToPointEnabled is deliberately false. A track click should not
        // fight WPF's internal Slider value updates or send duplicate seeks.
        TrackProgress.ApplyTemplate();
        var track = TrackProgress.Template.FindName("PART_Track", TrackProgress) as Track;
        double fraction;
        if (track is not null && track.ActualWidth > 0)
        {
            double halfThumb = (track.Thumb?.ActualWidth ?? 0) / 2;
            double usableWidth = Math.Max(1, track.ActualWidth - 2 * halfThumb);
            fraction = (e.GetPosition(track).X - halfThumb) / usableWidth;
        }
        else
        {
            fraction = e.GetPosition(TrackProgress).X / Math.Max(1, TrackProgress.ActualWidth);
        }

        _pendingTrackValue = TrackProgress.Minimum + Math.Clamp(fraction, 0, 1)
            * (TrackProgress.Maximum - TrackProgress.Minimum);
        _pendingTrackClick = true;
        _seeking = true;
        TrackProgress.Value = _pendingTrackValue;
        e.Handled = true;
        // Capture ensures the MouseUp arrives even if the pointer moves slightly.
        TrackProgress.CaptureMouse();
    }

    private async void Progress_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingTrackClick)
        {
            var requested = _pendingTrackValue;
            _pendingTrackClick = false;
            if (TrackProgress.IsMouseCaptured) TrackProgress.ReleaseMouseCapture();
            e.Handled = true;
            await CommitSeekAsync(requested);
            return;
        }
        if (!_draggingSeekThumb) return;
        _draggingSeekThumb = false;
        await CommitSeekAsync(TrackProgress.Value);
    }

    private void Progress_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Safety net when a window steals capture before the click is completed.
        if (_pendingTrackClick)
        {
            _pendingTrackClick = false;
            _seeking = false;
            UpdateProgress();
        }
    }

    private async Task CommitSeekAsync(double requested)
    {
        try
        {
            bool accepted = await _media.SeekAsync(requested);
            if (accepted)
            {
                _basePosition = requested;
                _progressClock.Restart();
                _awaitingSeekConfirmation = true;
                _seekGuardUntilUtc = DateTime.UtcNow.AddSeconds(3);
            }
            else TrackProgress.ToolTip = "Перемотка недоступна";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Seek: {ex}");
            TrackProgress.ToolTip = "Ошибка перемотки";
        }
        finally
        {
            _seeking = false;
            UpdateProgress();
        }
        await PollAsync();
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        bool target = !_playing;
        _requestedPlayState = target;
        _playStateGuardUntilUtc = DateTime.UtcNow.AddSeconds(2);
        _playing = target;
        var glyph = Geometry.Parse(target ? "M3,1 H8 V19 H3 Z M12,1 H17 V19 H12 Z" : "M4,1 L18,10 4,19 Z");
        CompactPlayPath.Data = glyph;
        ExpandedPlayPath.Data = glyph;
        UpdatePlaybackTooltips();
        ApplyArtworkButtons();
        _basePosition = TrackProgress.Value;
        _progressClock.Restart();
        try
        {
            if (!await _media.TogglePlayPauseAsync()) _requestedPlayState = null;
            await PollAsync();
        }
        catch (Exception ex)
        {
            _requestedPlayState = null;
            Debug.WriteLine($"Play/Pause: {ex}");
            await PollAsync();
        }
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingRepeatRecovery();
        // Some Yandex clients interpret Previous as "restart current" when the
        // position is > 3 seconds. Retry only if a reset to 0 was CONFIRMED.
        var before = _lastMedia;
        double beforePosition = TrackProgress.Value;
        try
        {
            if (!await _media.PreviousAsync()) return;
            if (before.HasSession && beforePosition > 4)
            {
                await Task.Delay(650);
                var after = await _media.ReadAsync();
                bool sameTrack = after.HasSession &&
                    after.SourceId == before.SourceId &&
                    after.Title == before.Title && after.Artist == before.Artist;
                bool restarted = sameTrack && after.PositionSeconds < 2.5 &&
                                 after.PositionSeconds + 2.5 < beforePosition;
                if (restarted) await _media.PreviousAsync();
            }
            await PollAsync();
        }
        catch (Exception ex) { Debug.WriteLine($"Previous: {ex}"); }
    }

    private void OpenYandexMusic_Click(object sender, RoutedEventArgs e)
    {
        if (!YandexWindowService.TryActivate())
            System.Windows.MessageBox.Show(
                "Не удалось найти окно установленной Яндекс Музыки. Запусти приложение вручную и попробуй снова.",
                "YMusic Mini", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingRepeatRecovery();
        await ActAsync(_media.NextAsync);
    }

    private void CancelPendingRepeatRecovery()
    {
        _repeatManualSkipUntilUtc = DateTime.UtcNow.AddSeconds(5);
        _repeatExpectedTrack = null;
        _repeatRecoveryUntilUtc = DateTime.MinValue;
        _repeatRetryAfterUtc = DateTime.UtcNow.AddSeconds(3);
    }
    private async Task ActAsync(Func<Task<bool>> command)
    {
        try { await command(); await PollAsync(); }
        catch (Exception ex) { ArtistLabel.Text = $"Ошибка команды: {ex.Message}"; }
    }

    private void UpdateVolume()
    {
        if (VolumeSlider.IsMouseCaptureWithin) return;
        var state = _volume.Read(_settings);
        _volumeReady = state.Available;
        _syncVolume = true;
        try
        {
            VolumeSlider.IsEnabled = state.Available;
            VolumeSlider.ToolTip = state.Available ? "Громкость" : "Звук недоступен";
            VolumeLabel.Text = state.Available ? $"{Math.Round(state.Percent):0}%" : string.Empty;
            if (state.Available)
            {
                VolumeSlider.Value = state.Percent;
                VolumeIconPath.Data = (Geometry)FindResource(state.Percent < 0.5
                    ? "Midnight.Icon.VolumeMute" : "Midnight.Icon.Volume");
            }
        }
        finally { _syncVolume = false; }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _syncVolume || !_volumeReady) return;
        if (!_volume.SetVolume(_settings, e.NewValue))
        {
            _volumeReady = false;
            VolumeSlider.IsEnabled = false;
            VolumeLabel.Text = string.Empty;
            return;
        }
        VolumeLabel.Text = $"{Math.Round(e.NewValue):0}%";
        VolumeIconPath.Data = (Geometry)FindResource(e.NewValue < 0.5 ? "Midnight.Icon.VolumeMute" : "Midnight.Icon.Volume");
    }

    private void Volume_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (!_volumeReady) return;
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (e.Delta > 0 ? 5 : -5), 0, 100);
        e.Handled = true;
    }

    private bool _repeatRequested;
    private bool _localRepeatEnabled;
    private bool _repeatSeekInFlight;
    private bool _repeatRecovering;
    private DateTime _repeatRetryAfterUtc = DateTime.MinValue;
    private DateTime _repeatRecoveryUntilUtc = DateTime.MinValue;
    private DateTime _repeatManualSkipUntilUtc = DateTime.MinValue;
    private string? _repeatExpectedTrack;
    private double _lastAudibleVolume = 65;

    private static string RepeatTrackId(MediaSnapshot snapshot) =>
        $"{snapshot.SourceId}\u001f{snapshot.Title}\u001f{snapshot.Artist}";

    // One state controls BOTH representations. The WPF toggle keeps its checked
    // appearance after expanding/collapsing; the capsule badge remains visible.
    private void SyncRepeatVisualState()
    {
        bool active = _repeatRequested && _localRepeatEnabled;
        RepeatButton.IsChecked = active;
        RepeatButton.Opacity = active ? 1.0 : 0.75;
        CompactRepeatBadge.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        CompactExpand.ToolTip = active ? "Развернуть · повтор включён" : "Развернуть";
        RepeatButton.ToolTip = active ? "Выключить повтор" : "Включить повтор";
    }

    private void SetRepeatIndicator(string message)
    {
        SyncRepeatVisualState();
        if (message.Contains("не удалось", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("не сработал", StringComparison.OrdinalIgnoreCase))
            RepeatButton.ToolTip = "Ошибка повтора";
    }

    private async void Repeat_Click(object sender, RoutedEventArgs e)
    {
        _repeatRequested = !_repeatRequested;
        _localRepeatEnabled = _repeatRequested;
        _repeatRecoveryUntilUtc = DateTime.MinValue;
        _repeatExpectedTrack = null;
        _repeatRetryAfterUtc = DateTime.MinValue;
        bool requested = _repeatRequested;
        SetRepeatIndicator(requested ? "Повтор текущего трека (локально)" : "Включить повтор одного трека");
        RepeatDiagnostics.Write($"toggle {(requested ? "on" : "off")}");
        try
        {
            // GSMTC may acknowledge this without Yandex actually enabling repeat-one.
            // The fallback does not rely on that acknowledgement.
            bool accepted = await _media.SetRepeatAsync(requested);
            RepeatDiagnostics.Write($"native_repeat_command accepted={accepted}");
        }
        catch (Exception ex)
        {
            RepeatDiagnostics.Write($"native_repeat_command exception={ex.GetType().Name}");
        }
    }

    private async Task RestartCurrentTrackAsync()
    {
        if (_repeatSeekInFlight || !_localRepeatEnabled || !_playing || !_lastMedia.HasSession)
            return;

        _repeatSeekInFlight = true;
        var expectedId = RepeatTrackId(_lastMedia);
        _repeatExpectedTrack = expectedId;
        _repeatRecoveryUntilUtc = DateTime.UtcNow.AddSeconds(7);
        _repeatRetryAfterUtc = DateTime.UtcNow.AddSeconds(8);
        var initialRaw = _media.GetRawTimeline();
        try
        {
            // Yandex Music may acknowledge TryChangePlaybackPositionAsync(0) but
            // ignore it. The user's Previous button *does* restart the current
            // track when playback is already several seconds in, so try it first.
            // Call the service directly: the UI Previous_Click intentionally has
            // extra logic to skip to the previous song on a second click.
            bool accepted = await _media.PreviousAsync();
            RepeatDiagnostics.Write($"previous_primary accepted={accepted} rawBefore={initialRaw?.RawSeconds:F1}s");
            if (!accepted)
            {
                // Only seek if the player explicitly rejected Previous.
                accepted = await _media.SeekAsync(0);
                RepeatDiagnostics.Write($"seek_if_previous_rejected accepted={accepted}");
            }
            if (!accepted)
            {
                _repeatExpectedTrack = null;
                SetRepeatIndicator("Повтор не поддерживается медиасессией");
                return;
            }

            // Avoid a visual jump to the old (often stale) timeline position while
            // the client processes the command. This is NOT proof of audio restart.
            _basePosition = 0;
            _progressClock.Restart();
            _awaitingSeekConfirmation = true;
            _seekGuardUntilUtc = DateTime.UtcNow.AddSeconds(5);
            UpdateProgress();

            await Task.Delay(750);
            if (!_repeatRequested || _closing || DateTime.UtcNow < _repeatManualSkipUntilUtc)
                return;
            var after = await _media.ReadAsync();
            var rawAfter = _media.GetRawTimeline();
            bool sameTrack = after.HasSession && RepeatTrackId(after) == expectedId;
            bool resetConfirmed = sameTrack &&
                (after.PositionSeconds < 6 || (rawAfter.HasValue && rawAfter.Value.RawSeconds < 6));
            RepeatDiagnostics.Write(
                $"previous_check sameTrack={sameTrack} estimated={after.PositionSeconds:F1}s " +
                $"raw={rawAfter?.RawSeconds:F1}s updated={rawAfter?.UpdatedAt:O} confirmed={resetConfirmed}");

            if (!sameTrack)
            {
                // Never send another Previous: this one might have switched to an
                // actual previous song. Prevent cascaded skips and report failure.
                _repeatExpectedTrack = null;
                _repeatRecoveryUntilUtc = DateTime.MinValue;
                _localRepeatEnabled = false;
                SetRepeatIndicator("Повтор приостановлен: плеер переключил композицию");
                RepeatDiagnostics.Write("repeat_paused_track_changed_after_previous");
                return;
            }

            if (!resetConfirmed)
            {
                // Seeking zero cannot jump to a different song, so it is the safe
                // second action even if Windows has stale progress metadata.
                bool seekAccepted = await _media.SeekAsync(0);
                RepeatDiagnostics.Write($"seek_after_previous_unconfirmed accepted={seekAccepted}");
                if (seekAccepted)
                {
                    await Task.Delay(650);
                    if (!_repeatRequested || _closing || DateTime.UtcNow < _repeatManualSkipUntilUtc)
                        return;
                    var check = await _media.ReadAsync();
                    var rawCheck = _media.GetRawTimeline();
                    sameTrack = check.HasSession && RepeatTrackId(check) == expectedId;
                    resetConfirmed = sameTrack &&
                        (check.PositionSeconds < 6 || (rawCheck.HasValue && rawCheck.Value.RawSeconds < 6));
                    RepeatDiagnostics.Write($"seek_check sameTrack={sameTrack} estimated={check.PositionSeconds:F1}s " +
                                            $"raw={rawCheck?.RawSeconds:F1}s confirmed={resetConfirmed}");
                }
            }

            if (resetConfirmed)
            {
                RepeatDiagnostics.Write("repeat_confirmed");
                _repeatExpectedTrack = null;
                _repeatRecoveryUntilUtc = DateTime.MinValue;
                _basePosition = 0;
                _progressClock.Restart();
                _awaitingSeekConfirmation = false;
                SetRepeatIndicator("Повтор текущего трека включён");
            }
            else
            {
                // Some Yandex builds keep the GSMTC timeline stale even while
                // audio plays. Do not blindly skip tracks to force a confirmation.
                RepeatDiagnostics.Write("repeat_unconfirmed_timeline_may_be_stale");
                SetRepeatIndicator("Повтор отправлен, но позиция не подтверждена — см. repeat.log");
            }
        }
        catch (Exception ex)
        {
            RepeatDiagnostics.Write($"repeat_exception {ex.GetType().Name}: {ex.Message}");
            SetRepeatIndicator("Ошибка повтора — см. журнал repeat.log");
        }
        finally
        {
            _repeatSeekInFlight = false;
        }
    }

    private void CheckRepeatTransition(MediaSnapshot data)
    {
        if (!_repeatRequested || _repeatExpectedTrack is null ||
            _repeatRecovering || _repeatSeekInFlight) return;

        if (DateTime.UtcNow > _repeatRecoveryUntilUtc)
        {
            _repeatExpectedTrack = null;
            return;
        }
        if (!data.HasSession) return;
        if (RepeatTrackId(data) == _repeatExpectedTrack)
        {
            if (data.PositionSeconds < 6)
            {
                _repeatExpectedTrack = null;
                _repeatRecoveryUntilUtc = DateTime.MinValue;
            }
            return;
        }
        if (DateTime.UtcNow < _repeatManualSkipUntilUtc) return;

        // The playlist advanced at the end after our seek attempt. Go back once.
        // Do not run in response to an explicit Next/Previous command from the widget.
        _repeatRecovering = true;
        _ = RecoverRepeatAfterAdvanceAsync();
    }

    private async Task RecoverRepeatAfterAdvanceAsync()
    {
        string? wanted = _repeatExpectedTrack;
        try
        {
            RepeatDiagnostics.Write("track_advanced_before_repeat; previous_once");
            bool accepted = await _media.PreviousAsync();
            if (!accepted)
            {
                RepeatDiagnostics.Write("recovery_previous_rejected");
                SetRepeatIndicator("Автоповтор не сработал — см. журнал repeat.log");
                return;
            }
            await Task.Delay(550);
            var result = await _media.ReadAsync();
            if (wanted is not null && result.HasSession && RepeatTrackId(result) == wanted)
            {
                // A previous-track command usually starts at zero, but seek again
                // if the app resumed near the end of the previous song.
                if (result.PositionSeconds > 6)
                    await _media.SeekAsync(0);
                RepeatDiagnostics.Write("recovery_returned_to_expected_track");
                _basePosition = 0;
                _progressClock.Restart();
            }
            else
            {
                RepeatDiagnostics.Write("recovery_returned_wrong_track");
                SetRepeatIndicator("Не удалось вернуть текущий трек после окончания");
            }
        }
        catch (Exception ex)
        {
            RepeatDiagnostics.Write($"recovery_exception {ex.GetType().Name}");
        }
        finally
        {
            _repeatExpectedTrack = null;
            _repeatRecoveryUntilUtc = DateTime.MinValue;
            _repeatRetryAfterUtc = DateTime.UtcNow.AddSeconds(3);
            _repeatRecovering = false;
        }
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (!_volumeReady) return;
        if (VolumeSlider.Value > 0.5)
        {
            _lastAudibleVolume = VolumeSlider.Value;
            VolumeSlider.Value = 0;
            VolumeIconPath.Data = (Geometry)FindResource("Midnight.Icon.VolumeMute");
        }
        else
        {
            VolumeSlider.Value = Math.Clamp(_lastAudibleVolume, 1, 100);
            VolumeIconPath.Data = (Geometry)FindResource("Midnight.Icon.Volume");
        }
    }

    private void SyncPinControls()
    {
        LockPositionMenu.IsChecked = _settings.PositionLocked;
        TopmostMenu.IsChecked = _settings.Topmost;
        LockButton.ToolTip = _settings.PositionLocked ? "Открепить" : "Закрепить";
        LockButton.Opacity = _settings.PositionLocked ? 1 : 0.7;
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.PositionLocked = !_settings.PositionLocked;
        SyncPinControls(); SaveSettings();
    }
    private void LockPosition_Click(object sender, RoutedEventArgs e)
    {
        _settings.PositionLocked = LockPositionMenu.IsChecked;
        SyncPinControls(); SaveSettings();
    }
    private void Topmost_Click(object sender, RoutedEventArgs e)
    {
        _settings.Topmost = TopmostMenu.IsChecked;
        Topmost = _settings.Topmost;
        SaveSettings();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsDialogOpen) { _openSettings?.Activate(); return; }
        _collapseTimer.Stop();
        string[] processNames = _volume.FindProcesses();
        var capsuleBrush = (SolidColorBrush)FindResource("CapsuleBg");
        var window = new SettingsWindow(_settings, _knownSources, processNames,
            _lastMedia.Cover, _lastMedia.Title, _lastMedia.Artist, _lastMedia.IsPlaying,
            capsuleBrush.Color);
        _settingsDialogOpen = true;
        _openSettings = window;
        // Keep the widget visible while editing its appearance. Do not disable its
        // always-on-top setting: the user needs to compare the real widget with preview.
        // The settings window itself remains a regular, non-topmost, nonmodal window.
        window.Topmost = false;
        if (_automaticallyHidden && !_manuallyHidden)
        {
            _automaticallyHidden = false;
            Show();
            Topmost = _settings.Topmost;
        }
        window.Loaded += (_, _) =>
        {
            // Open settings in the center of the monitor containing the widget.
            // WorkAreaFor uses DIP, matching Window.Left/Top/Width/Height.
            var work = WorkAreaFor(_compactLeft, _compactTop);
            window.Left = work.Left + (work.Width - window.ActualWidth) / 2;
            window.Top = work.Top + (work.Height - window.ActualHeight) / 2;
        };
        window.Closed += async (_, _) =>
        {
            _openSettings = null;
            _settingsDialogOpen = false;
            Topmost = _settings.Topmost;
            if (!window.Saved)
            {
                ApplyAppearance();
                UpdateAutomaticVisibility();
                return;
            }
            if (window.ResetPositionRequested)
            {
                var work = WorkAreaFor(null, null);
                _compactLeft = work.Right - CompactWidth - 16;
                _compactTop = work.Bottom - CompactHeight - 16;
                UpdateFixedWindowPosition();
            }
            _media.SelectedSourceId = _settings.SelectedSourceId;
            Opacity = _expanded ? 1.0 : CompactOpacity;
            _expandTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.ExpandDelayMs, 0, 5000));
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.CollapseDelayMs, 0, 5000));
            SyncPinControls();
            ApplyButtonPersonalization();
            SaveSettings();
            StartupService.Configure(_settings.StartWithWindows);
            if (_hotkeys is not null && !_hotkeys.Configure(_settings))
                System.Windows.MessageBox.Show("Глобальные сочетания уже заняты другим приложением. Они не были включены.",
                    "YMusic Mini", MessageBoxButton.OK, MessageBoxImage.Information);
            _lastThemeLight = null;
            _lastCapsuleBackground = null;
            _lastColors = null;
            ApplyAppearance();
            await PollAsync();
            UpdateAutomaticVisibility();
        };
        window.Show();
    }

    // Only the media buttons are reordered. The album cover and expand arrow
    // always keep their original positions; the animated cover coordinates
    // are then computed from the actual visual tree as before.
    private void ApplyButtonPersonalization()
    {
        var corner = _settings.ButtonShape switch
        {
            "Rounded" => new CornerRadius(9),
            "Square" => new CornerRadius(4),
            _ => new CornerRadius(18)
        };
        Resources["PlayerButtonCornerRadius"] = corner;
        Resources["MediaIconRadius"] = corner;
        Resources["MediaPrimaryRadius"] = corner;
        // После изменения настроек оформление обновит заливку и контраст.
        if (IsLoaded) ApplyAppearance();
        ApplyArtworkButtons();

        string[] order = _settings.ButtonOrder switch
        {
            "PlayPause,Previous,Next" => ["PlayPause", "Previous", "Next"],
            "Previous,Next,PlayPause" => ["Previous", "Next", "PlayPause"],
            _ => ["Previous", "PlayPause", "Next"]
        };

        ReorderMediaButtons(CompactView, 1, order, CompactPrev, CompactToggle, CompactNext);
        ReorderMediaButtons(ExpandedControls, 0, order, ExpandedPrev, ExpandedToggle, ExpandedNext);
    }

    private static void ReorderMediaButtons(
        System.Windows.Controls.StackPanel panel,
        int startingIndex,
        string[] order,
        System.Windows.Controls.Button previous,
        System.Windows.Controls.Button playPause,
        System.Windows.Controls.Button next)
    {
        var buttons = new Dictionary<string, System.Windows.Controls.Button>
        {
            ["Previous"] = previous,
            ["PlayPause"] = playPause,
            ["Next"] = next
        };
        foreach (var button in buttons.Values) panel.Children.Remove(button);
        for (int i = 0; i < order.Length; i++)
            panel.Children.Insert(startingIndex + i, buttons[order[i]]);
    }

    private void ApplyArtworkButtons(bool? playing = null)
    {
        var isPlaying = playing ?? _playing;
        var key = _settings.ButtonVisual;
        bool artwork = ButtonStyleCatalog.IsArtwork(key);
        void Apply(System.Windows.Controls.Image image, FrameworkElement vector, string action)
        {
            image.Source = artwork ? ButtonStyleCatalog.GetImage(key, action) : null;
            image.Visibility = artwork ? Visibility.Visible : Visibility.Collapsed;
            vector.Visibility = artwork ? Visibility.Collapsed : Visibility.Visible;
        }
        Apply(CompactPrevArtwork, CompactPrevVector, "Previous");
        Apply(CompactToggleArtwork, CompactToggleVector, isPlaying ? "Pause" : "Play");
        Apply(CompactNextArtwork, CompactNextVector, "Next");
        Apply(ExpandedPrevArtwork, ExpandedPrevVector, "Previous");
        Apply(ExpandedToggleArtwork, ExpandedToggleVector, isPlaying ? "Pause" : "Play");
        Apply(ExpandedNextArtwork, ExpandedNextVector, "Next");
    }

    private void ApplyAppearance()
    {
        if (!IsLoaded || _settingsDialogOpen) return;
        bool light;
        Color background;
        Color outline;
        Color ink;
        Color secondary;
        var animation = SystemParameters.ClientAreaAnimation
            ? TimeSpan.FromMilliseconds(700) : TimeSpan.Zero;

        if (_settings.ThemeMode is "Surroundings" or "Album")
        {
            Color? source = null;
            if (_settings.ThemeMode == "Surroundings" && !_settingsDialogOpen && IsVisible)
                source = _surrounding.Read(this);
            else if (_settings.ThemeMode == "Album" && _lastMedia.Cover is not null)
            {
                // Cache expensive album color extraction until the artwork changes.
                if (!ReferenceEquals(_cachedAlbumThemeCover, _lastMedia.Cover))
                {
                    _cachedAlbumThemeCover = _lastMedia.Cover;
                    _cachedAlbumThemePalette = _palettes.Read("Album", _lastMedia.Cover);
                }
                if (_cachedAlbumThemePalette is { Length: >= 3 } colors)
                    source = Color.FromRgb(
                        (byte)((colors[0].R + colors[1].R + colors[2].R) / 3),
                        (byte)((colors[0].G + colors[1].G + colors[2].G) / 3),
                        (byte)((colors[0].B + colors[1].B + colors[2].B) / 3));
            }

            if (source is { } sampled)
            {
                bool surroundingIsLight = SurroundingThemeService.Brightness(sampled) >= 139;
                background = SurroundingThemeService.GetCapsuleBackground(sampled,
                    _settings.ThemeMode == "Album" ? 78 : _settings.SurroundingIntensity,
                    surroundingIsLight);
                // Use actual rendered background luminance for text contrast.
                light = PreferDarkText(background);
            }
            else
            {
                light = ForegroundThemeService.SystemIsLight();
                background = light ? Color.FromRgb(249, 250, 253) : Color.FromRgb(29, 33, 43);
            }
            background = CapsuleContrastPolicy.Background(background, light);
            ink = light ? Color.FromRgb(23, 30, 42) : Color.FromRgb(250, 251, 254);
            secondary = SurroundingThemeService.Mix(ink, background, 0.23);
            outline = CapsuleContrastPolicy.Outline(background, light);
        }
        else
        {
            light = _foreground.IsLight(_settings.ThemeMode);
            background = light ? Color.FromRgb(232, 238, 249) : Color.FromRgb(29, 33, 43);
            ink = light ? Color.FromRgb(35, 43, 57) : Color.FromRgb(246, 248, 253);
            secondary = light ? Color.FromRgb(89, 100, 115) : Color.FromRgb(181, 191, 206);
            outline = CapsuleContrastPolicy.Outline(background, light);
        }

        // Keep the capsule surface opaque; compact transparency comes from
        // Window.Opacity, which is animated to 1.0 when expanded.
        background = Color.FromRgb(background.R, background.G, background.B);

        // Screen sampling can return several intermediate colors while windows
        // change. Wait for a reasonably stable target, then animate once.
        bool firstSample = _lastCapsuleBackground is null;
        if (firstSample || Difference(_lastCapsuleBackground!.Value, background) >= 14)
        {
            if (_candidateCapsuleBackground is { } previousCandidate &&
                Difference(previousCandidate, background) <= 12)
                _candidateCapsuleCount++;
            else
                _candidateCapsuleCount = 1;
            _candidateCapsuleBackground = background;
            if (firstSample || _candidateCapsuleCount >= 2)
            {
                var duration = SystemParameters.ClientAreaAnimation
                    ? TimeSpan.FromMilliseconds(700) : TimeSpan.Zero;
                ChangeColor("CapsuleBg", background, duration);
                ChangeColor("CapsuleOutline", outline, duration);
                ChangeColor("SecondaryText", secondary, duration);
                _lastCapsuleBackground = background;
                _candidateCapsuleCount = 0;
                _candidateCapsuleBackground = null;
            }
        }
        else
        {
            _candidateCapsuleCount = 0;
            _candidateCapsuleBackground = null;
        }
        if (_lastThemeLight != light)
        {
            ChangeColor("CapsuleText", ink, animation);
            _lastThemeLight = light;
        }
        var palette = _palettes.Read(_settings.AccentMode, _lastMedia.Cover);
        ApplyButtonVisualStyle(background, ink, light, palette);
        if (_lastColors is null || !_lastColors.SequenceEqual(palette))
        {
            StartGradientTransition(palette);
            _lastColors = (Color[])palette.Clone();
        }
    }

    private void ApplyButtonVisualStyle(Color capsuleBackground, Color textInk, bool light, Color[] palette)
    {
        string key = _settings.ButtonVisual;
        string style = ButtonStyleCatalog.IsArtwork(key) ? "Flat" : key;
        var look = ButtonAppearanceFactory.Create(style, palette, light);
        Resources["MediaIconFill"] = look.SecondaryFill;
        Resources["MediaPrimaryFill"] = look.PrimaryFill;
        Resources["MediaIconBorder"] = look.SecondaryBorder;
        Resources["MediaPrimaryBorder"] = look.PrimaryBorder;
        Resources["MediaIconBorderSize"] = look.SecondaryThickness;
        Resources["MediaPrimaryBorderSize"] = look.PrimaryThickness;
        Resources["MediaGlyphBrush"] = look.Glyph;
        Resources["MediaPrimaryGlowOpacity"] = look.GlowOpacity;
        Resources["PlayerButtonBackground"] = look.SecondaryFill;
        Resources["PlayerButtonBorderBrush"] = look.SecondaryBorder;
        Resources["PlayerButtonBorderThickness"] = look.SecondaryThickness;
        // IconInk is used by auxiliary actions outside the customizable transport row.
        ChangeColor("IconInk", textInk, TimeSpan.Zero);
    }

    private static bool PreferDarkText(Color color)
    {
        static double Linear(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        double luminance = 0.2126 * Linear(color.R) +
                           0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return luminance > 0.179; // Prefer whichever ink has higher WCAG contrast.
    }

    private static int Difference(Color a, Color b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    // На ресурсах WPF GradientStop может быть заморожен. Не анимируем его Color:
    // каждые 25 мс создаём новую кисть, а DynamicResource обновляет кнопки.
    private void StartGradientTransition(Color[] target)
    {
        _gradientTimer.Stop();
        var current = (LinearGradientBrush)FindResource("SharedButtonGradient");
        _gradientFrom = current.GradientStops.OrderBy(stop => stop.Offset)
            .Select(stop => stop.Color).ToArray();
        _gradientTo = (Color[])target.Clone();

        if (!SystemParameters.ClientAreaAnimation)
        {
            ReplaceGradient(_gradientTo);
            return;
        }

        _gradientClock.Restart();
        _gradientTimer.Start();
    }

    private void UpdateGradientFrame()
    {
        double t = Math.Clamp(_gradientClock.Elapsed.TotalMilliseconds / 500.0, 0, 1);
        Color[] colors = new Color[3];
        for (int i = 0; i < colors.Length; i++)
        {
            var a = _gradientFrom[i];
            var b = _gradientTo[i];
            colors[i] = Color.FromArgb(
                Lerp(a.A, b.A, t), Lerp(a.R, b.R, t),
                Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
        }
        ReplaceGradient(colors);
        if (t >= 1)
        {
            _gradientTimer.Stop();
            _gradientClock.Stop();
        }
    }

    private static byte Lerp(byte from, byte to, double t) =>
        (byte)Math.Clamp((int)Math.Round(from + (to - from) * t), 0, 255);

    private void ReplaceGradient(Color[] colors)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(colors[0], 0));
        brush.GradientStops.Add(new GradientStop(colors[1], 0.5));
        brush.GradientStops.Add(new GradientStop(colors[2], 1));
        Resources["SharedButtonGradient"] = brush;
        if (_lastCapsuleBackground is { } current)
        {
            bool light = PreferDarkText(current);
            Color text = light ? Color.FromRgb(23, 30, 42) : Color.FromRgb(250, 251, 254);
            ApplyButtonVisualStyle(current, text, light, colors);
        }
    }

    private void ChangeColor(string key, Color to, TimeSpan duration)
    {
        var brush = (SolidColorBrush)FindResource(key);
        if (brush.IsFrozen)
        {
            brush = brush.CloneCurrentValue();
            Resources[key] = brush;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, duration) { FillBehavior = FillBehavior.HoldEnd });
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        _manuallyHidden = true;
        Hide();
    }

    private void UpdateAutomaticVisibility()
    {
        if (!_started || _closing || _manuallyHidden || _settingsDialogOpen) return;
        bool hide = (_settings.HideWhenNotPlaying && !_lastMedia.IsPlaying) ||
            (_settings.HideWhenYandexClosed &&
             !YandexWindowService.IsRunning(_settings.AudioProcessName)) ||
            _visibility.ShouldHide(_settings.HideWhileYandexFocused, _settings.HideOverFullscreen);
        if (hide == _automaticallyHidden) return;
        _automaticallyHidden = hide;
        if (hide) Hide();
        else
        {
            // Don't steal keyboard focus when auto-reappearing.
            Show();
            Topmost = _settings.Topmost;
        }
    }

    private void HotkeyInvoked(int action)
    {
        if (action == 1) Toggle_Click(this, new RoutedEventArgs());
        if (action == 2) Previous_Click(this, new RoutedEventArgs());
        if (action == 3) Next_Click(this, new RoutedEventArgs());
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private static System.Drawing.Icon CreateTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/Icons/midnight-player.ico"));
            if (resource is not null)
            {
                using (resource.Stream)
                using (var memory = new System.IO.MemoryStream())
                {
                    resource.Stream.CopyTo(memory);
                    memory.Position = 0;
                    using var raw = new System.Drawing.Icon(memory);
                    return (System.Drawing.Icon)raw.Clone();
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Tray icon: {ex.Message}"); }
        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private void SetupTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Показать капсулу", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray)));
        menu.Items.Add("Настройки...", null, (_, _) => Dispatcher.BeginInvoke(new Action(() => Settings_Click(this, new RoutedEventArgs()))));
        menu.Items.Add("Скрыть капсулу", null, (_, _) => Dispatcher.BeginInvoke(new Action(HideToTray)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.BeginInvoke(new Action(Close)));
        _tray = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "YMusic Mini",
            ContextMenuStrip = menu,
            Visible = true
        };
        // One LEFT click is enough; right click continues to open the context menu.
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsVisible && !_automaticallyHidden)
                {
                    _manuallyHidden = true;
                    Hide();
                }
                else ShowFromTray();
            }));
        };
    }
    private void HideToTray()
    {
        _manuallyHidden = true;
        Hide();
    }

    private void ShowFromTray()
    {
        // A manually requested reveal should not flash briefly if the client is closed.
        if (_settings.HideWhenYandexClosed && !YandexWindowService.IsRunning(_settings.AudioProcessName))
            return;
        _manuallyHidden = false;
        _automaticallyHidden = false;
        Show();
        if (_windowAnimating) FinishWindowAnimation();
        _expanded = false;
        UpdateFixedWindowPosition();
        SetWindowContentState(false);
        Topmost = _settings.Topmost;
        Activate();
    }
    private void SaveSettings()
    {
        try { _settingsService.Save(_settings); }
        catch (Exception ex) { Debug.WriteLine($"Сохранение настроек: {ex.Message}"); }
    }
    private void Window_Closed(object sender, EventArgs e)
    {
        _closing = true;
        _expandTimer.Stop(); _collapseTimer.Stop(); _pollTimer.Stop(); _progressTimer.Stop(); _themeTimer.Stop(); _gradientTimer.Stop();
        CompositionTarget.Rendering -= WindowAnimation_Rendering;
        _windowAnimationClock.Stop();
        _desktopVisibility?.Dispose();
        _hotkeys?.Dispose();
        _settings.CompactLeft = _compactLeft;
        _settings.CompactTop = _compactTop;
        SaveSettings();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
    }
}
