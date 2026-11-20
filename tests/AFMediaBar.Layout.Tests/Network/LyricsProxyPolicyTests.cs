// 验证歌词代理策略：国内来源默认直连、境外来源默认走代理、逐来源覆盖生效、代理失效回直连、其它域名维持系统代理。
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Network;

[TestClass]
public sealed class LyricsProxyPolicyTests
{
    private static readonly Uri Lrclib = new("https://lrclib.net/api/get");

    [TestMethod]
    public void EverySourceConnectsDirectlyByDefault()
    {
        var policy = new LyricsProxyPolicy(new StubProxy());
        Assert.IsNull(policy.GetProxy(new Uri("https://music.163.com/api/lyric")), "网易云默认直连。");
        Assert.IsNull(policy.GetProxy(new Uri("https://c.y.qq.com/lyric")), "QQ 音乐默认直连。");
        Assert.IsNull(policy.GetProxy(new Uri("https://krcs.kugou.com/search")), "酷狗子域默认直连。");
        Assert.IsNull(policy.GetProxy(Lrclib), "LRCLIB 默认直连，走代理按来源单独打开。");
        Assert.IsNull(policy.GetProxy(new Uri("https://itunes.apple.com/search")), "Apple Music 默认直连。");
    }

    [TestMethod]
    public void NonLyricHostsAlwaysKeepTheSystemProxy()
    {
        var policy = new LyricsProxyPolicy(new StubProxy());
        Assert.IsNotNull(policy.GetProxy(new Uri("https://example.com/")), "歌词以外的请求不受这个策略影响。");
        Assert.IsTrue(policy.IsBypassed(new Uri("https://music.163.com/api/lyric")), "直连的歌词域名对代理来说是绕过的。");
        Assert.IsFalse(policy.IsBypassed(new Uri("https://example.com/")));
    }

    [TestMethod]
    public void MissingSystemProxyFallsBackToDirectConnections()
    {
        var policy = new LyricsProxyPolicy(system: null);
        Assert.IsNull(policy.GetProxy(new Uri("https://music.163.com/api/lyric")), "本机没有代理时不能把请求卡住。");
        Assert.IsNull(policy.GetProxy(Lrclib), "境外源也回落直连，而不是抛错。");
        Assert.IsNull(policy.GetProxy(new Uri("https://example.com/")));
    }

    [TestMethod]
    public void DeadLoopbackProxyFallsBackToDirectConnections()
    {
        var port = ReserveFreePort();
        var policy = new LyricsProxyPolicy(new WebProxy($"http://127.0.0.1:{port}"));

        Assert.IsNull(policy.GetProxy(Lrclib), "代理软件已退出、系统设置还留着端口时，不能一直等到超时。");
        Assert.IsTrue(policy.IsBypassed(Lrclib), "探测不到代理时按绕过处理。");
    }

    [TestMethod]
    public void LiveLoopbackProxyKeepsTheOverseasSourceOnIt()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var policy = new LyricsProxyPolicy(new WebProxy($"http://127.0.0.1:{port}"));
        policy.SetSourceProxyModes(new Dictionary<string, LyricsSourceProxyMode>
        {
            [LyricsSourceCatalog.Lrclib] = LyricsSourceProxyMode.Proxy
        });

        Assert.IsNotNull(policy.GetProxy(Lrclib), "代理还活着就照旧走代理。");
    }

    [TestMethod]
    public void RestartedProxyIsPickedUpWithoutRestartingTheApp()
    {
        var port = ReserveFreePort();
        var policy = new LyricsProxyPolicy(new WebProxy($"http://127.0.0.1:{port}"));
        policy.SetSourceProxyModes(new Dictionary<string, LyricsSourceProxyMode>
        {
            [LyricsSourceCatalog.Lrclib] = LyricsSourceProxyMode.Proxy
        });
        Assert.IsNull(policy.GetProxy(Lrclib), "先按代理不可用处理。");

        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        ExpireProbeCache(policy);

        Assert.IsNotNull(policy.GetProxy(Lrclib), "代理重新起来后不需要重启程序。");
    }

    [TestMethod]
    public void SystemRoutingRulesAreRespectedPerDestination()
    {
        // 用户把 QQ 设成走代理，而分流规则让 LRCLIB 直连：
        // 决策按请求域名逐个向系统规则确认，不能因为探测目标选了 LRCLIB 就把 QQ 也改成直连。
        var policy = new LyricsProxyPolicy(new RoutingProxy());
        policy.SetSourceProxyModes(new Dictionary<string, LyricsSourceProxyMode>
        {
            [LyricsSourceCatalog.QQMusic] = LyricsSourceProxyMode.Proxy
        });
        Assert.IsNull(policy.GetProxy(Lrclib), "规则让 LRCLIB 直连。");
        Assert.IsNotNull(policy.GetProxy(new Uri("https://c.y.qq.com/lyric")), "QQ 仍按规则走代理。");
    }

    [TestMethod]
    public void PerSourceModesOverrideTheDefaults()
    {
        var policy = new LyricsProxyPolicy(new StubProxy());
        policy.SetSourceProxyModes(new Dictionary<string, LyricsSourceProxyMode>
        {
            [LyricsSourceCatalog.QQMusic] = LyricsSourceProxyMode.Proxy,
            [LyricsSourceCatalog.Lrclib] = LyricsSourceProxyMode.Direct
        });

        Assert.IsNotNull(policy.GetProxy(new Uri("https://c.y.qq.com/lyric")), "QQ 被改成走代理。");
        Assert.IsNull(policy.GetProxy(Lrclib), "LRCLIB 被改成直连。");
        Assert.IsNull(policy.GetProxy(new Uri("https://music.163.com/api/lyric")), "未改的来源仍是默认直连。");
        Assert.IsNull(policy.GetProxy(new Uri("https://music.apple.com/search")), "未改的来源仍是默认直连。");
    }

    [TestMethod]
    public void NormalizationDropsUnknownProxyModes()
    {
        var settings = new LyricsSourceSettings
        {
            SourceProxyModes = new Dictionary<string, LyricsSourceProxyMode>
            {
                [LyricsSourceCatalog.QQMusic] = (LyricsSourceProxyMode)99,
                [LyricsSourceCatalog.Lrclib] = LyricsSourceProxyMode.Direct
            }
        };

        CollectionAssert.AreEquivalent(new[] { LyricsSourceCatalog.Lrclib },
            settings.Normalize().SourceProxyModes!.Keys.ToArray(), "无法解析的设置值被丢弃。");
    }

    private static int ReserveFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void ExpireProbeCache(LyricsProxyPolicy policy) =>
        typeof(LyricsProxyPolicy)
            .GetField("_nextProbeTick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(policy, 0L);

    private sealed class StubProxy : IWebProxy
    {
        private static readonly Uri Target = new("http://proxy.example:8080");

        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination) => Target;

        public bool IsBypassed(Uri host) => false;
    }

    /// <summary>模拟分流规则：LRCLIB 直连，其余域名走代理。/ Simulates split routing: LRCLIB direct, everything else proxied.</summary>
    private sealed class RoutingProxy : IWebProxy
    {
        private static readonly Uri Target = new("http://proxy.example:8080");

        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination) =>
            destination.Host.EndsWith("lrclib.net", StringComparison.OrdinalIgnoreCase) ? destination : Target;

        public bool IsBypassed(Uri host) => false;
    }
}
