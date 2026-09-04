using Microsoft.UI.Xaml.Media;

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
    ImageSource? Thumbnail)
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
        null);
}
