// 验证公开数据的口径、缓存与限流；不访问网络或用户配置。
using System.Net;
using System.Net.Http;
using System.Text.Json;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Home;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class HomeContentTests
{
    [TestMethod]
    public void DirectoryRejectsExecutableLinksAndUntrustedImages()
    {
        var valid = """{"schemaVersion":1,"items":[{"id":"test","kind":"feature","url":"https://github.com/Fervent-Tempo/AF-Media-Bar/issues/1","localizations":{"en":{"title":"Title","summary":"Summary"}}}]}""";
        Assert.AreEqual(1, HomeContentPolicy.ParseCatalogue(valid).Items.Count);
        Assert.ThrowsException<FormatException>(() => HomeContentPolicy.ParseCatalogue(valid.Replace("https://github.com/Fervent-Tempo/AF-Media-Bar/issues/1", "file:///C:/Windows/notepad.exe")));
        Assert.IsFalse(HomeContentPolicy.IsOfficialImage("https://cdn.jsdelivr.net/gh/someone/other@main/picture.png"));
        Assert.ThrowsException<FormatException>(() => HomeContentPolicy.ParseCatalogue("""{"schemaVersion":1,"items":[null]}"""));
    }
    [TestMethod]
    public void UnknownCountsStayUnknownAndIssueRowsExcludePullRequests()
    {
        using var data = JsonDocument.Parse("""{"incomplete_results":true,"total_count":50,"items":[{"number":1,"title":"Issue"},{"number":2,"title":"PR","pull_request":{},"draft":true}]}""");
        Assert.IsNull(HomeContentPolicy.Count(data.RootElement, "total_count"));
        Assert.AreEqual(1, HomeContentPolicy.Threads(data.RootElement, false).Single().Number);
        Assert.IsTrue(HomeContentPolicy.Threads(data.RootElement, true).Single().IsDraft);
        using var unknown = JsonDocument.Parse("{}");
        Assert.IsNull(HomeContentPolicy.Count(unknown.RootElement, "stargazers_count"));
    }
    [TestMethod]
    public async Task ProjectUsesSeparateQueriesAndPreservesCacheDuringCooldown()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMB-home-" + Guid.NewGuid().ToString("N"));
        using var log = new AppLogService(Path.Combine(directory, "logs"));
        using var handler = new Responses(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("AF-Media-Bar") ? "{\"stargazers_count\":42}" : "{\"total_count\":3,\"items\":[]}")
        });
        using var client = new HttpClient(handler);
        using var service = new HomeContentService(log, client, directory);
        var first = await service.RefreshProjectAsync(false, CancellationToken.None);
        Assert.AreEqual(42, first.Stars);
        Assert.AreEqual(3, first.OpenIssues);
        Assert.AreEqual(4, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.Any(query => query.Contains("is:issue is:open")));
        Assert.IsTrue(handler.Requests.Any(query => query.Contains("is:pr is:open")));
        var second = await service.RefreshProjectAsync(true, CancellationToken.None);
        Assert.AreEqual(first.Stars, second.Stars);
        Assert.AreEqual(4, handler.Requests.Count, "手动刷新也必须遵守冷却。");
        using var cached = new HomeContentService(log, client, directory);
        Assert.AreEqual(42, (await cached.ReadProjectAsync(CancellationToken.None)).Stars);
    }
    [TestMethod]
    public async Task RateLimitStopsRemainingRequestsAndDoesNotInventZero()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMB-home-" + Guid.NewGuid().ToString("N"));
        using var log = new AppLogService(Path.Combine(directory, "logs"));
        using var handler = new Responses(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var client = new HttpClient(handler);
        using var service = new HomeContentService(log, client, directory);
        var result = await service.RefreshProjectAsync(true, CancellationToken.None);
        Assert.IsNull(result.Stars);
        Assert.IsNull(result.OpenPullRequests);
        Assert.AreEqual(1, handler.Requests.Count);
        await service.RefreshProjectAsync(true, CancellationToken.None);
        Assert.AreEqual(1, handler.Requests.Count);
    }
    private sealed class Responses(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(Uri.UnescapeDataString(request.RequestUri!.ToString()));
            return Task.FromResult(respond(request));
        }
    }
    [TestMethod]
    public async Task HeadersWithoutBodyCannotKeepRefreshWaiting()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMB-home-" + Guid.NewGuid().ToString("N"));
        using var log = new AppLogService(Path.Combine(directory, "logs"));
        var count = 0;
        using var handler = new Responses(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = ++count == 1 ? new StreamContent(new NeverBody()) : new StringContent("{\"total_count\":2,\"items\":[]}") });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var service = new HomeContentService(log, client, directory);
        var result = await service.RefreshProjectAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsNull(result.Stars);
        Assert.AreEqual(4, handler.Requests.Count);
    }
    [TestMethod]
    public void NewlySelectedSessionCannotBorrowPreviousPlayersCapabilities()
    {
        var snapshot = AFMediaBar.Classes.Models.MediaSnapshot.Disconnected with { IsConnected = true, SourceId = "Spotify.exe", CanPlayPause = true, CanSeek = true };
        AFMediaBar.Classes.Models.MediaSessionOption[] sessions = [new("n", "cloudmusic.exe", "NetEase", true, true), new("s", "Spotify.exe", "Spotify", false, false)];
        var cards = PlayerSupportCatalog.Describe(sessions, snapshot);
        Assert.AreEqual(AFMediaBar.Resources.Translations.Get("Home.Player.AwaitingCapabilities"), cards.First().Capabilities);
        Assert.AreEqual(1, cards.Count(card => card.Status == AFMediaBar.Resources.Translations.Get("Home.Player.Current")));
        var stale = PlayerSupportCatalog.Describe(sessions, snapshot with { IsStale = true });
        Assert.IsFalse(stale.Any(card => card.Status == AFMediaBar.Resources.Translations.Get("Home.Player.Current")));
    }
    [TestMethod]
    public void StructuredPlayerStateMatchesExistingStatusAndCapabilities()
    {
        var snapshot = AFMediaBar.Classes.Models.MediaSnapshot.Disconnected with
        { IsConnected = true, SourceId = "Spotify.exe", CanPlayPause = true, CanSeek = false, IsPlaying = true };
        AFMediaBar.Classes.Models.MediaSessionOption[] sessions = [new("n", "cloudmusic.exe", "NetEase", true, true), new("s", "Spotify.exe", "Spotify", false, false)];
        var cards = PlayerSupportCatalog.Describe(sessions, snapshot);
        var current = cards.Single(card => card.IsCurrent);
        Assert.AreEqual(PlayerStatusKind.Current, current.StatusKind);
        Assert.AreEqual(AFMediaBar.Resources.Translations.Get("Home.Player.Current"), current.Status);
        var pending = cards.Single(card => !card.IsCurrent);
        Assert.AreEqual(PlayerStatusKind.Pending, pending.StatusKind);
        Assert.AreEqual(AFMediaBar.Resources.Translations.Get("Home.Player.Pending"), pending.Status);
        Assert.AreEqual(0, pending.CapabilityItems.Count, "非当前会话不能借用当前来源的能力项。");
        CollectionAssert.AreEqual(new[]
        {
            AFMediaBar.Resources.Translations.Get("Start.Metadata"),
            AFMediaBar.Resources.Translations.Get("Home.Player.ControlAvailable"),
            AFMediaBar.Resources.Translations.Get("Home.Player.SeekUnavailable"),
            AFMediaBar.Resources.Translations.Get("Home.Player.LyricsUnknown")
        }, current.CapabilityItems.Select(item => item.Label).ToArray());
        CollectionAssert.AreEqual(new bool?[] { true, true, false, null },
            current.CapabilityItems.Select(item => item.Available).ToArray());
        var stale = PlayerSupportCatalog.Describe(sessions, snapshot with { IsStale = true });
        Assert.IsFalse(stale.Any(card => card.IsCurrent), "过期快照不能产生当前状态。");
        Assert.IsFalse(stale.Any(card => card.CapabilityItems.Count != 0));
    }
    [TestMethod]
    public void OnlyPlayersWithUnverifiedPluginConditionsAreFlagged()
    {
        var flagged = PlayerSupportCatalog.All.Where(profile => profile.IsPluginUnverified).Select(profile => profile.Key).ToArray();
        CollectionAssert.AreEquivalent(new[] { "mpv", "foobar2000" }, flagged);
    }
    [TestMethod]
    public async Task NarrowImageDecodeIsBoundedAndSmallImagesAreNotUpscaled()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMB-home-" + Guid.NewGuid().ToString("N"));
        using var log = new AppLogService(Path.Combine(directory, "logs"));
        foreach (var (width, height) in new[] { (1, 4096), (16, 16) })
        {
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var encoded = new MemoryStream(); encoder.Save(encoded);
            using var handler = new Responses(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(encoded.ToArray()) });
            using var client = new HttpClient(handler);
            using var service = new HomeContentService(log, client, directory);
            var image = await service.ReadImageAsync($"https://raw.githubusercontent.com/Fervent-Tempo/AF-Media-Bar/main/{width}.png", CancellationToken.None);
            Assert.IsNotNull(image);
            Assert.IsTrue(image.PixelWidth <= Math.Min(width, 1600) && image.PixelHeight <= Math.Min(height, 900));
        }
    }
    private sealed class NeverBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(Timeout.InfiniteTimeSpan, token); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
