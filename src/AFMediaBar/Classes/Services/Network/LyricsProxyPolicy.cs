// 仅供歌词客户端使用。显式直连覆盖代理，其余请求按目标地址交给系统。
using System.Net;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

internal sealed class LyricsProxyPolicy(IWebProxy? system) : IWebProxy
{
    private IReadOnlyDictionary<string, LyricsSourceProxyMode> _sourceModes =
        new Dictionary<string, LyricsSourceProxyMode>();

    public void SetSourceProxyModes(IReadOnlyDictionary<string, LyricsSourceProxyMode>? modes) =>
        Volatile.Write(ref _sourceModes, modes is null
            ? new Dictionary<string, LyricsSourceProxyMode>()
            : new Dictionary<string, LyricsSourceProxyMode>(modes, StringComparer.Ordinal));

    public ICredentials? Credentials
    {
        get => system?.Credentials;
        set { if (system is not null) system.Credentials = value; }
    }

    public Uri? GetProxy(Uri destination) => IsDirect(destination) ? null : system?.GetProxy(destination);

    public bool IsBypassed(Uri host) => IsDirect(host) || system?.IsBypassed(host) != false;

    private bool IsDirect(Uri destination) =>
        LyricsSourceHosts.SourceIdFor(destination.Host) is { } source &&
        Volatile.Read(ref _sourceModes).TryGetValue(source, out var mode) && mode == LyricsSourceProxyMode.Direct;
}
