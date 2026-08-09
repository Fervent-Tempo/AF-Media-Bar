using System.Windows.Media.Imaging;

namespace AFMediaBar.Models;

public sealed record MediaSnapshot(
    bool IsConnected,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanSkipPrevious,
    bool CanSkipNext,
    string Title,
    string Artist,
    string SourceId,
    string SourceName,
    BitmapImage? Artwork)
{
    public static MediaSnapshot Disconnected { get; } = new(
        false,
        false,
        false,
        false,
        false,
        "等待媒体播放",
        "请在任意应用中播放音频或视频",
        string.Empty,
        "媒体",
        null);
}
