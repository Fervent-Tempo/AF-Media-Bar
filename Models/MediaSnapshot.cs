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
    // 断开快照不携带固定语言文本；窗口根据当前语言资源呈现占位符或错误信息。
    // Disconnected snapshots carry no fixed-language text; windows resolve placeholders or errors from current resources.
    public static MediaSnapshot Disconnected { get; } = new(
        false,
        false,
        false,
        false,
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        null);
}
