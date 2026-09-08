namespace AttaquerTaskbar.Models;

public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string Album,
    bool HasSession,
    bool IsPlaying,
    bool CanSkipPrevious,
    bool CanSkipNext,
    bool CanPlayPause,
    bool CanSeek,
    TimeSpan Position,
    TimeSpan EndTime,
    byte[]? Thumbnail)
{
    public static MediaSnapshot Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        false,
        false,
        false,
        false,
        false,
        TimeSpan.Zero,
        TimeSpan.Zero,
        null);
}
