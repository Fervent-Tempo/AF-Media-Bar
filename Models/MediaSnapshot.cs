using System.Windows.Media.Imaging;

namespace TaskbarPlayer.Models;

public sealed record MediaSnapshot(
    bool IsConnected,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanSkipPrevious,
    bool CanSkipNext,
    string Title,
    string Artist,
    string SourceId,
    BitmapImage? Artwork)
{
    public static MediaSnapshot Disconnected { get; } = new(
        false,
        false,
        false,
        false,
        false,
        "等待网易云音乐",
        "请先播放一首歌曲",
        string.Empty,
        null);
}
