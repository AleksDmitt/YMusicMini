using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Media;
using YMusicMini.Models;

namespace YMusicMini.Services;

/// <summary>
/// Используем только системные медиасессии Windows: никаких токенов Яндекса.
/// Автоматически НЕ выбираем произвольный проигрыватель, чтобы не переключать чужие треки.
/// </summary>
public sealed class MediaSessionService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _lastCoverKey;
    private ImageSource? _lastCover;
    private string? _pendingCoverKey;
    private DateTime _missingCoverSinceUtc;

    public string? SelectedSourceId { get; set; }

    private async Task EnsureManagerAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }

    public async Task<string[]> GetSourcesAsync()
    {
        await EnsureManagerAsync();
        try
        {
            return _manager!.GetSessions()
                .Select(s => s.SourceAppUserModelId)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToArray();
        }
        catch
        {
            // A desktop client can close between two media polls.
            _manager = null;
            _session = null;
            return [];
        }
    }

    public async Task<MediaSnapshot> ReadAsync()
    {
        try
        {
        await EnsureManagerAsync();
        var sessions = _manager!.GetSessions();

        _session = SelectedSourceId is not null
            ? sessions.FirstOrDefault(s => string.Equals(s.SourceAppUserModelId,
                SelectedSourceId, StringComparison.OrdinalIgnoreCase))
            : sessions.FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains("yandex", StringComparison.OrdinalIgnoreCase));

        if (_session is null)
        {
            _manager = null;
            _lastCoverKey = null;
            _lastCover = null;
            _pendingCoverKey = null;
            return MediaSnapshot.Empty;
        }

        var props = await _session.TryGetMediaPropertiesAsync();
        var info = _session.GetPlaybackInfo();
        var timeline = _session.GetTimelineProperties();
        var key = $"{_session.SourceAppUserModelId}|{props.Title}|{props.Artist}|{props.AlbumTitle}";
        // A few desktop-client builds temporarily publish a null thumbnail when the
        // track changes. Retry instead of caching the failure for the whole track.
        ImageSource? cover = _lastCover;
        if (!string.Equals(_lastCoverKey, key, StringComparison.Ordinal))
        {
            if (!string.Equals(_pendingCoverKey, key, StringComparison.Ordinal))
            {
                _pendingCoverKey = key;
                _missingCoverSinceUtc = DateTime.UtcNow;
            }
            var candidate = await LoadCoverAsync(props.Thumbnail);
            if (candidate is not null)
            {
                _lastCover = candidate;
                _lastCoverKey = key;
                _pendingCoverKey = null;
                cover = candidate;
            }
            else if (DateTime.UtcNow - _missingCoverSinceUtc > TimeSpan.FromSeconds(2))
            {
                // Never show a previous track's cover indefinitely.
                cover = null;
            }
        }

        // GSMTC position may be a snapshot: Yandex Music sometimes keeps LastUpdatedTime
        // unchanged for many seconds while the music keeps playing. The old 5-second cap
        // made the progress appear stuck after ~9-10 seconds.
        var duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
        var position = (timeline.Position - timeline.StartTime).TotalSeconds;
        if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
            timeline.LastUpdatedTime > DateTimeOffset.UnixEpoch)
        {
            double elapsed = (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds;
            // Ignore obviously invalid timestamps; otherwise extrapolate to the end of track.
            if (elapsed >= 0 && elapsed < 12 * 60 * 60)
                position += elapsed;
        }
        position = Math.Clamp(position, 0, duration);

        return new MediaSnapshot(
            true,
            string.IsNullOrWhiteSpace(props.Title) ? "Без названия" : props.Title,
            string.IsNullOrWhiteSpace(props.Artist) ? "Неизвестный исполнитель" : props.Artist,
            _session.SourceAppUserModelId,
            info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            cover,
            position,
            duration,
            timeline.LastUpdatedTime);
        }
        catch (Exception)
        {
            // The previous session may have been destroyed when Yandex Music exited.
            // Request a fresh session manager on the next poll after a restart.
            _manager = null;
            _session = null;
            _lastCoverKey = null;
            _lastCover = null;
            _pendingCoverKey = null;
            return MediaSnapshot.Empty;
        }
    }

    private static async Task<ImageSource?> LoadCoverAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var winrtStream = await thumbnail.OpenReadAsync();
            if (winrtStream.Size > 16 * 1024 * 1024) return null;
            using var reader = new Windows.Storage.Streams.DataReader(winrtStream);
            var size = checked((uint)winrtStream.Size);
            await reader.LoadAsync(size);
            var bytes = new byte[checked((int)size)];
            reader.ReadBytes(bytes);
            using var buffer = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = buffer;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // Обложка необязательна — интерфейс продолжит работать с заглушкой.
            return null;
        }
    }

    // Raw timeline for diagnosing seek/repeat acknowledgements. Unlike ReadAsync,
    // this does NOT extrapolate stale position using LastUpdatedTime.
    public (double RawSeconds, DateTimeOffset UpdatedAt)? GetRawTimeline()
    {
        if (_session is null) return null;
        try
        {
            var timeline = _session.GetTimelineProperties();
            var seconds = (timeline.Position - timeline.StartTime).TotalSeconds;
            return (Math.Max(0, seconds), timeline.LastUpdatedTime);
        }
        catch { return null; }
    }

    public async Task<bool> TogglePlayPauseAsync()
        => _session is not null && await _session.TryTogglePlayPauseAsync();

    public async Task<bool> PreviousAsync()
        => _session is not null && await _session.TrySkipPreviousAsync();

    public async Task<bool> NextAsync()
        => _session is not null && await _session.TrySkipNextAsync();

    // Optional standard GSMTC commands. Yandex Music may decline them.
    public async Task<bool> SetRepeatAsync(bool repeat)
        => _session is not null && await _session.TryChangeAutoRepeatModeAsync(
            repeat ? MediaPlaybackAutoRepeatMode.Track : MediaPlaybackAutoRepeatMode.None);

    public async Task<bool> SeekAsync(double positionSeconds)
    {
        if (_session is null) return false;
        var timeline = _session.GetTimelineProperties();
        var ticks = (timeline.StartTime + TimeSpan.FromSeconds(Math.Max(0, positionSeconds))).Ticks;
        return await _session.TryChangePlaybackPositionAsync(ticks);
    }
}
