// 验证逐来源代理覆盖、系统分流和测试是否使用实际歌词 API。
using System.Net;
using System.Net.Http;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Network;

[TestClass]
public sealed class LyricsProxyPolicyTests
{
    private static readonly Uri _qq = new("https://u.y.qq.com/cgi-bin/musicu.fcg");
    private static readonly Uri _lrclib = new("https://lrclib.net/api/search");
    private static readonly Uri _unrelated = new("https://example.com/");
    private static readonly Uri _systemAddress = new("http://127.0.0.1:1");

    [TestMethod]
    public void DefaultsUseTheOriginalSystemRouteForEachDestination()
    {
        var system = new WebProxy(_systemAddress) { BypassList = [@"lrclib\.net"] };
        var policy = new LyricsProxyPolicy(system);
        foreach (var uri in new[] { _qq, _lrclib, _unrelated })
        {
            Assert.AreEqual(system.IsBypassed(uri), policy.IsBypassed(uri));
            Assert.AreEqual(system.GetProxy(uri), policy.GetProxy(uri));
        }
        Assert.AreEqual(_systemAddress, policy.GetProxy(_qq), "本机代理端口的连通性不能悄悄改变路由。");
    }

    [TestMethod]
    public void ExplicitDirectOnlyBypassesTheSelectedSource()
    {
        var policy = new LyricsProxyPolicy(new WebProxy(_systemAddress));
        policy.SetSourceProxyModes(new Dictionary<string, LyricsSourceProxyMode>
        {
            [LyricsSourceCatalog.QQMusic] = LyricsSourceProxyMode.Direct
        });

        Assert.IsTrue(policy.IsBypassed(_qq));
        Assert.IsNull(policy.GetProxy(_qq));
        Assert.AreEqual(_systemAddress, policy.GetProxy(_lrclib));
        Assert.AreEqual(_systemAddress, policy.GetProxy(_unrelated));

        policy.SetSourceProxyModes(null);
        Assert.IsFalse(policy.IsBypassed(_qq));
        Assert.AreEqual(_systemAddress, policy.GetProxy(_qq));
    }

    [TestMethod]
    public void NormalizationOnlyPersistsExplicitDirectOverrides()
    {
        var settings = new LyricsSourceSettings
        {
            SourceProxyModes = new Dictionary<string, LyricsSourceProxyMode>
            {
                [LyricsSourceCatalog.QQMusic] = (LyricsSourceProxyMode)99,
                [LyricsSourceCatalog.Lrclib] = LyricsSourceProxyMode.FollowSystem,
                ["FutureSource"] = LyricsSourceProxyMode.Direct
            }
        };

        CollectionAssert.AreEquivalent(new[] { "FutureSource" }, settings.Normalize().SourceProxyModes!.Keys.ToArray());
        Assert.IsNull((settings with { SourceProxyModes = new Dictionary<string, LyricsSourceProxyMode>() }).Normalize().SourceProxyModes);
    }

    [TestMethod]
    public async Task ConnectivityUsesTheLyricEndpointAndReturnsHttpStatus()
    {
        using var handler = new EndpointRecorder();
        using var http = new HttpClient(handler);
        var tester = new LyricsSourceTester(http);

        var result = await tester.TestAsync(LyricsSourceCatalog.QQMusic, CancellationToken.None);

        Assert.AreEqual(_qq, handler.Destination);
        Assert.IsTrue(result.Reachable);
        Assert.AreEqual("405", result.Detail);
    }

    private sealed class EndpointRecorder : HttpMessageHandler
    {
        public Uri? Destination { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Destination = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        }
    }
}
