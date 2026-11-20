// 歌词请求的代理策略：按域名决定哪些请求绕开系统代理，其余请求保持原样。
using AFMediaBar.Classes.Settings;
using System.Net;
using System.Net.Sockets;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 挂在 <see cref="HttpClient.DefaultProxy"/> 上的代理策略，决定每个请求走不走系统代理。
///
/// 歌词取词一次并行打多个来源，全部经系统代理既慢又容易在代理软件里排队失败，所以所有来源默认直连，
/// 需要走代理的来源（比如国内网络下的 LRCLIB 与 Apple Music）由用户在设置里逐个打开。
///
/// 走代理的决策按请求域名逐个向系统规则确认：系统规则（PAC、分流规则）让某个域名直连就直连，
/// 不会因为别的域名问出了不同答案而跟着改。代理软件退出后系统里常残留死端口，
/// 解析出本机环回地址时先探一次端口再交给它，探不到就按直连处理，代理重新起来后自动恢复。
///
/// A proxy policy installed on <see cref="HttpClient.DefaultProxy"/> that decides, per request, whether the system proxy applies.
///
/// Lyric lookup fans out to several sources at once; routing all of them through the proxy is slow and tends to queue up inside the
/// proxy client, so every source connects directly by default and the ones that need the proxy (LRCLIB and Apple Music on
/// mainland networks, for example) are turned on per source in the settings.
///
/// Whether the proxy applies is confirmed against the system rules per destination: when routing rules say a host connects
/// directly it stays direct, regardless of what another host resolved to. A proxy client that has quit often leaves its port
/// behind in the system settings, so a resolved loopback address is probed before use and requests go direct when the port is
/// dead, recovering automatically once the proxy is back.
/// </summary>
internal sealed class LyricsProxyPolicy : IWebProxy
{
    /// <summary>连接本机代理端口的超时；健康的代理是毫秒级响应，这个值只是防火墙丢包时的兜底。/ Timeout for reaching the local proxy port.</summary>
    private const int ProbeTimeoutMilliseconds = 200;

    /// <summary>代理可用时的复检间隔；代理多半不会频繁切换，没必要每次都探。/ Re-probe interval while the proxy works.</summary>
    private const long HealthyProbeIntervalMilliseconds = 60_000;

    /// <summary>代理不可用时的复检间隔；代理软件随时可能重新启动，探得勤一点才能尽快恢复。/ Re-probe interval while the proxy is down.</summary>
    private const long BrokenProbeIntervalMilliseconds = 10_000;

    private readonly IWebProxy? _system;

    private Uri? _probedAddress;
    private long _nextProbeTick;
    private int _probeGate;
    private volatile bool _probedAlive = true;
    private IReadOnlyDictionary<string, LyricsSourceProxyMode> _sourceModes =
        new Dictionary<string, LyricsSourceProxyMode>();

    /// <summary>沿用系统当前代理作为"需要代理时"的目标；为 null 表示本机没有代理。/ Wraps the system proxy used whenever one is wanted; null means there is none.</summary>
    public LyricsProxyPolicy(IWebProxy? system) => _system = system;

    /// <summary>
    /// 替换每来源的代理模式表；键是歌词来源组 id，缺项按直连处理。
    /// Replaces the per-source proxy modes; keys are lyric source-group ids, missing keys connect directly.
    /// </summary>
    public void SetSourceProxyModes(IReadOnlyDictionary<string, LyricsSourceProxyMode>? modes) =>
        _sourceModes = modes is null
            ? new Dictionary<string, LyricsSourceProxyMode>()
            : new Dictionary<string, LyricsSourceProxyMode>(modes, StringComparer.Ordinal);

    public ICredentials? Credentials
    {
        get => _system?.Credentials;
        set { if (_system is not null) _system.Credentials = value; }
    }

    public Uri? GetProxy(Uri destination) => Resolve(destination);

    public bool IsBypassed(Uri host) => Resolve(host) is null;

    /// <summary>
    /// 解析一个请求最终用的代理地址；null 表示直连。
    /// 歌词域名以外的请求不参与任何决策，完全沿用系统代理（包括绕过列表）。
    /// Resolves the proxy address a request ends up with; null means direct. Requests outside the lyric hosts take no part in
    /// any decision here and follow the system proxy as-is, bypass list included.
    /// </summary>
    private Uri? Resolve(Uri destination)
    {
        if (_system is null)
            return null;
        var sourceId = LyricsSourceHosts.SourceIdFor(destination.Host);
        if (sourceId is null)
            return SystemProxyFor(destination);
        if (_sourceModes.TryGetValue(sourceId, out var mode))
            return mode == LyricsSourceProxyMode.Proxy ? SystemProxyFor(destination) : null;
        return null;
    }

    /// <summary>
    /// 向系统规则确认这个域名该用哪个代理。规则让它直连就返回 null；解析出本机环回地址时先探一次端口，
    /// 端口连不上按直连处理——公司代理与 PAC 返回的远端地址不做 TCP 探测，一次连接既测不出认证是否通过，
    /// 也可能被 DNS 拖慢。
    /// Confirms the proxy for one destination against the system rules and returns null when the rules say direct. A resolved
    /// loopback address is probed before use and treated as direct when the port refuses; corporate proxies and PAC results on
    /// remote hosts are taken at face value because a TCP probe cannot tell whether authentication would succeed and can stall
    /// on DNS.
    /// </summary>
    private Uri? SystemProxyFor(Uri destination)
    {
        if (_system!.IsBypassed(destination))
            return null;
        var proxy = _system.GetProxy(destination);
        if (proxy is null || proxy == destination)
            return null;
        if (!proxy.IsLoopback)
            return proxy;
        return Probe(proxy) ? proxy : null;
    }

    /// <summary>
    /// 探测本机代理端口是否还连得上。结果按地址缓存一段时间：可用时低频复检，不可用时高频重试，
    /// 代理重新起来后能自己恢复。同一时刻只让一个线程去探，其余沿用上一次的结论，
    /// 避免并发取词把探测排成队。
    /// Checks whether the local proxy port still answers. The verdict is cached per address, checked rarely while healthy and
    /// often while down, so the proxy is picked up again on its own once it comes back. Only one thread probes at a time and
    /// concurrent callers keep the previous verdict, so parallel lookups never queue up on the probe.
    /// </summary>
    private bool Probe(Uri proxy)
    {
        if (Uri.Equals(_probedAddress, proxy) && Environment.TickCount64 < Interlocked.Read(ref _nextProbeTick))
            return _probedAlive;
        if (Interlocked.Exchange(ref _probeGate, 1) != 0)
            return _probedAlive;
        try
        {
            var alive = CanConnect(proxy);
            _probedAddress = proxy;
            _probedAlive = alive;
            Interlocked.Exchange(
                ref _nextProbeTick,
                Environment.TickCount64 + (alive ? HealthyProbeIntervalMilliseconds : BrokenProbeIntervalMilliseconds));
            return alive;
        }
        finally
        {
            Interlocked.Exchange(ref _probeGate, 0);
        }
    }

    private static bool CanConnect(Uri proxy)
    {
        try
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            var connect = socket.BeginConnect(proxy.Host, proxy.Port, null, null);
            if (!connect.AsyncWaitHandle.WaitOne(ProbeTimeoutMilliseconds))
                return false;
            socket.EndConnect(connect);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
