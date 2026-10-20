using System.Windows.Media;

namespace YMusicMini.Models;

public sealed record MediaSnapshot(
    bool HasSession,
    string Title,
    string Artist,
    string? SourceId,
    bool IsPlaying,
    ImageSource? Cover,
    double PositionSeconds,
    double DurationSeconds,
    DateTimeOffset TimelineUpdatedAt)
{
    public static MediaSnapshot Empty => new(
        false, "Яндекс Музыка не найдена", "Выбери медиасессию", null,
        false, null, 0, 0, DateTimeOffset.MinValue);
}
