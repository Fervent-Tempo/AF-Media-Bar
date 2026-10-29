// 支持说明与本次会话能力分别呈现；来源分类复用媒体目录的固定映射。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Home;
using AFMediaBar.Resources;
namespace AFMediaBar.Classes.Services.Home;

/// <summary>播放器卡片的状态种类；页面按它选择状态呈现，不依赖本地化文案。</summary>
public enum PlayerStatusKind { Current, Pending, Playing, Connected }

/// <summary>当前会话的一条能力；<paramref name="Available"/> 为 null 表示本次会话尚未确认。</summary>
public sealed record HomePlayerCapability(string Label, bool? Available);

public sealed record PlayerSupportProfile(string Key, string Name, string Notes, string PluginCondition)
{
    /// <summary>插件条件尚未核实；仅用于支持列表提示，不代表检测到或缺少插件。</summary>
    public bool IsPluginUnverified { get; init; }
}

public sealed record HomePlayerCard(string Key, string Name, string Status, string Capabilities, string Notes)
{
    /// <summary>状态种类，供页面选择状态徽章样式。</summary>
    public PlayerStatusKind StatusKind { get; init; }

    /// <summary>是否为当前有效会话；快照过期时不成立。</summary>
    public bool IsCurrent { get; init; }

    /// <summary>当前会话的能力项；非当前会话为空，不借用其他来源的能力。</summary>
    public IReadOnlyList<HomePlayerCapability> CapabilityItems { get; init; } = [];
}

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
            Translations.Get(note ?? "Home.Player.Smtc"), Translations.Get(plugin ? "Start.UnverifiedPlugin" : "Home.Player.Plugin"))
        { IsPluginUnverified = plugin };
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
            var matchesSnapshot = current is { IsConnected: true } && (string.Equals(current.SourceId, session.SourceId, StringComparison.OrdinalIgnoreCase) ||
                key != "other" && key == MediaSourceNameFormatter.GetKnownSourceKey(current.SourceId));
            var isCurrent = matchesSnapshot && current?.IsStale != true;
            var isPending = !isCurrent && (session.IsSelected || matchesSnapshot);
            var kind = isCurrent ? PlayerStatusKind.Current : isPending ? PlayerStatusKind.Pending
                : session.IsPlaying ? PlayerStatusKind.Playing : PlayerStatusKind.Connected;
            var capabilities = isCurrent ? string.Join(" · ", new[] {
                Translations.Get("Start.Metadata"),
                Translations.Get(current!.CanPlayPause ? "Home.Player.ControlAvailable" : "Home.Player.ControlUnavailable"),
                Translations.Get(current.CanSeek ? "Home.Player.SeekAvailable" : "Home.Player.SeekUnavailable"),
                Translations.Get(current.Lyrics is null ? "Home.Player.LyricsUnknown" : "Home.Player.LyricsAvailable") }) : Translations.Get(isPending ? "Home.Player.AwaitingCapabilities" : "Home.Player.NotSelected");
            var status = kind switch
            {
                PlayerStatusKind.Current => "Home.Player.Current",
                PlayerStatusKind.Pending => "Home.Player.Pending",
                PlayerStatusKind.Playing => "Home.Player.Playing",
                _ => "Home.Player.Connected"
            };
            result.Add(new(key, profile?.Name ?? MediaSourceNameFormatter.GetDisplayName(session.SourceId, Translations.Get("Start.OtherSmtc")),
                Translations.Get(status), capabilities, profile?.Notes ?? Translations.Get("Home.Player.Smtc"))
            {
                StatusKind = kind,
                IsCurrent = isCurrent,
                CapabilityItems = isCurrent ? DescribeCapabilities(current!) : []
            });
        }
        return result;
    }

    /// <summary>当前会话能力：控制与跳转按快照可用性，歌词未知时保持未确认。</summary>
    private static IReadOnlyList<HomePlayerCapability> DescribeCapabilities(MediaSnapshot current) =>
    [
        new(Translations.Get("Start.Metadata"), true),
        new(Translations.Get(current.CanPlayPause ? "Home.Player.ControlAvailable" : "Home.Player.ControlUnavailable"), current.CanPlayPause),
        new(Translations.Get(current.CanSeek ? "Home.Player.SeekAvailable" : "Home.Player.SeekUnavailable"), current.CanSeek),
        new(Translations.Get(current.Lyrics is null ? "Home.Player.LyricsUnknown" : "Home.Player.LyricsAvailable"), current.Lyrics is null ? (bool?)null : true)
    ];
}
