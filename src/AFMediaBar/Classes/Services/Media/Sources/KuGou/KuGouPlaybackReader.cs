// 后台轮询独占读取器；歌曲信息取自实时窗口标题，不读取滞后的播放历史或加密数据库。
using System.Globalization;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Media.Sources.KuGou;

internal sealed record KuGouPlaybackInfo(string Title, string Artist, double Position, double Duration, bool IsPlaying,
    bool UsesSessionMetadata = false)
{
    // 曲目的原始内存时长可用于区分同名、同歌手的不同版本。
    internal string Identity => string.Join('\u001f', Title.Trim().ToUpperInvariant(), Artist.Trim().ToUpperInvariant(),
        Duration.ToString("R", CultureInfo.InvariantCulture));
}

internal interface IKuGouPlaybackReader : IDisposable
{
    KuGouPlaybackInfo? Read(MediaSnapshot session);
}

internal sealed class KuGouPlaybackReader : IKuGouPlaybackReader
{
    private static readonly KuGouSourcePolicy _sourcePolicy = new();
    private KuGou? _player;
    public KuGouPlaybackInfo? Read(MediaSnapshot session)
    {
        try
        {
            _player ??= KuGou.Attach();
            if (_player?.TryReadPlayback(out var info) == true) return info;
            // 独立读取暂不可用时保留原有的 SMTC 时间轴增强能力。
            if (_player is not null && session.IsConnected && _sourcePolicy.Matches(session.SourceId) &&
                _player.TryReadTimeline(out var position, out var duration) && duration > 0 && duration <= 86_400 && position <= duration + 2)
                return new(session.Title, session.Artist, position, duration, session.IsPlaying, true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 进程退出或窗口重建时重新附加，不保留上一首歌曲。
        }
        Dispose();
        return null;
    }
    public void Dispose()
    {
        _player?.Dispose();
        _player = null;
    }
}

internal static class KuGouTrackMetadata
{
    internal static bool TryParse(string? caption, out string title, out string artist)
    {
        title = artist = string.Empty;
        const string suffix = " - 酷狗音乐";
        var text = caption?.Trim();
        if (text is null || !text.EndsWith(suffix, StringComparison.Ordinal)) return false;
        text = text[..^suffix.Length].Trim();
        if (text.Length == 0) return false;
        var separator = text.IndexOf(" - ", StringComparison.Ordinal);
        if (separator > 0)
        {
            artist = text[..separator].Trim().Replace('、', ',');
            title = text[(separator + 3)..].Trim();
        }
        else title = text;
        return title.Length > 0;
    }
}
