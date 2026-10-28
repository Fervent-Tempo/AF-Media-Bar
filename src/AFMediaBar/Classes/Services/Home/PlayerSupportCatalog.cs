// 支持说明与本次会话能力分别呈现；来源分类复用媒体目录的固定映射。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Home;
using AFMediaBar.Resources;
namespace AFMediaBar.Classes.Services.Home;

public sealed record PlayerSupportProfile(string Key, string Name, string Notes, string PluginCondition);
public sealed record HomePlayerCard(string Key, string Name, string Status, string Capabilities, string Notes);

/// <summary>维护已知接入方式，不据此声称所有播放器版本均已通过验收。</summary>
public static class PlayerSupportCatalog
{
    public static IReadOnlyList<PlayerSupportProfile> Resolve(HomeCatalogue catalogue, string language) => All.Select(profile =>
    {
        var note = catalogue.Support?.FirstOrDefault(item => item.Key == profile.Key);
        if (note is null) return profile;
        var text = note.Localizations.GetValueOrDefault(language) ?? note.Localizations.GetValueOrDefault("zh-Hans") ?? note.Localizations.Values.First();
        return profile with { Notes = text.Title, PluginCondition = text.Summary };
    }).ToArray();
    public static IReadOnlyList<PlayerSupportProfile> All => new[]
    {
        Profile("Service.MediaSource.NetEaseCloudMusic", "Home.Player.NetEase"),
        Profile("Service.MediaSource.Kugou", "Home.Player.KuGou"),
        Profile("Service.MediaSource.QQMusic"), Profile("Spotify"), Profile("Windows Media Player"),
        Profile("Google Chrome"), Profile("Microsoft Edge"), Profile("Firefox"), Profile("VLC"),
        Profile("PotPlayer"), Profile("mpv", null, true), Profile("foobar2000", null, true)
    };
    private static PlayerSupportProfile Profile(string key, string? note = null, bool plugin = false) =>
        new(key, key.StartsWith("Service.") ? Translations.Get(key) : key,
            Translations.Get(note ?? "Home.Player.Smtc"), Translations.Get(plugin ? "Start.UnverifiedPlugin" : "Home.Player.Plugin"));
    public static IReadOnlyList<HomePlayerCard> Describe(IReadOnlyList<MediaSessionOption> sessions, MediaSnapshot? current)
    {
        var available = sessions.Where(session => !string.IsNullOrWhiteSpace(session.SourceId))
            .OrderByDescending(session => session.IsSelected).ThenByDescending(session => session.IsPlaying)
            .GroupBy(session => MediaSourceNameFormatter.GetKnownSourceKey(session.SourceId) == "other" ? session.SourceId : MediaSourceNameFormatter.GetKnownSourceKey(session.SourceId));
        var result = new List<HomePlayerCard>();
        foreach (var group in available)
        {
            var session = group.First();
            var key = MediaSourceNameFormatter.GetKnownSourceKey(session.SourceId);
            var profile = All.FirstOrDefault(item => item.Key == key);
            var isCurrent = current is { IsConnected: true } && (session.IsSelected || string.Equals(current.SourceId, session.SourceId, StringComparison.OrdinalIgnoreCase));
            var capabilities = isCurrent ? string.Join(" · ", new[] {
                Translations.Get("Start.Metadata"),
                Translations.Get(current!.CanPlayPause ? "Home.Player.ControlAvailable" : "Home.Player.ControlUnavailable"),
                Translations.Get(current.CanSeek ? "Home.Player.SeekAvailable" : "Home.Player.SeekUnavailable"),
                Translations.Get(current.Lyrics is null ? "Home.Player.LyricsUnknown" : "Home.Player.LyricsAvailable") }) : Translations.Get("Home.Player.NotSelected");
            result.Add(new(key, profile?.Name ?? MediaSourceNameFormatter.GetDisplayName(session.SourceId, Translations.Get("Start.OtherSmtc")),
                Translations.Get(isCurrent ? "Home.Player.Current" : session.IsPlaying ? "Home.Player.Playing" : "Home.Player.Connected"), capabilities,
                profile?.Notes ?? Translations.Get("Home.Player.Smtc")));
        }
        return result;
    }
}
