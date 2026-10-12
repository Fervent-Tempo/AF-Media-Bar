// 验证内置名单、缓存与线上回退的优先级；网络用可控响应替代，文件只写独立临时目录。
using System.Net;
using System.Net.Http;
using AFMediaBar.Classes.Models.Credits;
using AFMediaBar.Classes.Services.Credits;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止旧名单覆盖发布副本，以及取消或释放后发布过期结果。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CreditsServiceTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize() => _directory = Path.Combine(Path.GetTempPath(), "afmb-credits-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public void FirstOpenHasBundledNamesWithoutNetworkOrCache()
    {
        using var handler = new FakeHandler((_, _) => throw new InvalidOperationException("构造时不应联网。"));
        using var client = new HttpClient(handler);
        using var service = CreateService(client);
        Assert.IsTrue(service.Current.Contributors.Any(entry => entry.Login == "Heartestrella"));
        Assert.IsTrue(service.Current.Contributors.Any(entry => entry.Login == "furinamikuvn"));
        Assert.IsTrue(service.Current.Sponsors.Any(entry => entry.Name == "Curryx"));
        Assert.IsTrue(service.Current.ContributorsLoaded && service.Current.SponsorsLoaded);
        Assert.IsNull(service.Current.FetchedUtc, "内置副本不能冒充刚联网更新的缓存。");
        Assert.AreEqual(0, handler.Calls);
        Assert.IsFalse(Directory.Exists(_directory));
    }

    [TestMethod]
    public void NewBundleReplacesStillFreshOldCacheAndAllowsRefresh()
    {
        var cache = new CreditsCacheStore(_directory);
        var bundledDate = ReadBundledDate("Contributors", "generatedUtc");
        var cachedDate = bundledDate.AddSeconds(-1);
        Assert.IsTrue(CreditsCachePolicy.IsFresh(cachedDate, bundledDate));
        Assert.IsTrue(cache.TryWrite([Contributor("OldCache")], [], cachedDate));
        using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response("[]"))));
        using var service = CreateService(client);
        Assert.IsTrue(service.Current.Contributors.Any(entry => entry.Login == "Heartestrella"));
        Assert.IsFalse(service.Current.Contributors.Any(entry => entry.Login == "OldCache"));
        Assert.IsNull(service.Current.FetchedUtc);
    }

    [TestMethod]
    public void NewerCacheKeepsAuthoritativeNamesAndEmptySponsors()
    {
        var cache = new CreditsCacheStore(_directory);
        var fetchedUtc = DateTimeOffset.UtcNow;
        Assert.IsTrue(cache.TryWrite([Contributor("CachedContributor")], [], fetchedUtc));
        using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response("[]"))));
        using var service = CreateService(client);
        Assert.AreEqual("CachedContributor", service.Current.Contributors.Single().Login);
        Assert.AreEqual(0, service.Current.Sponsors.Count);
        Assert.IsTrue(service.Current.SponsorsLoaded);
        Assert.AreEqual(fetchedUtc, service.Current.FetchedUtc);
    }

    [TestMethod]
    public async Task RateLimitAndStaleSnapshotsKeepBundledNamesAndExposeRetry()
    {
        using var client = new HttpClient(new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host == "api.github.com" ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) :
            request.RequestUri.AbsolutePath.EndsWith("contributors.json", StringComparison.Ordinal) ?
                Response("""{"generatedUtc":"2000-01-01T00:00:00Z","contributors":[{"login":"StaleSnapshot"}]}""") :
                Response("""{"schemaVersion":1,"updatedUtc":"2000-01-01T00:00:00Z","sponsors":[]}"""))));
        using var service = CreateService(client);
        var initial = service.Current;
        var refreshed = await RefreshAsync(service);
        CollectionAssert.AreEqual(initial.Contributors.ToArray(), refreshed.Contributors.ToArray());
        CollectionAssert.AreEqual(initial.Sponsors.ToArray(), refreshed.Sponsors.ToArray());
        Assert.IsNotNull(refreshed.FailureReason);
        Assert.IsTrue(refreshed.IsPartial);
        Assert.IsNull(refreshed.FetchedUtc, "拒绝旧快照不应延长缓存时效。");
        Assert.IsFalse(File.Exists(new CreditsCacheStore(_directory).FilePath));
    }

    [TestMethod]
    public async Task FreshOnlineDataCanRemoveSponsorsAndRefreshCache()
    {
        using var client = new HttpClient(new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host == "api.github.com" ? Response("""[{"login":"OnlineContributor","contributions":5}]""") :
                Response("""{"schemaVersion":1,"sponsors":[]}"""))));
        using var service = CreateService(client);
        var refreshed = await RefreshAsync(service);
        Assert.AreEqual("OnlineContributor", refreshed.Contributors.Single().Login);
        Assert.AreEqual(0, refreshed.Sponsors.Count);
        Assert.IsTrue(refreshed.SponsorsLoaded);
        Assert.IsNull(refreshed.FailureReason);
        var cached = new CreditsCacheStore(_directory).TryRead();
        Assert.IsNotNull(cached);
        Assert.IsFalse(cached.Partial);
        Assert.AreEqual("OnlineContributor", cached.Contributors.Single().Login);
    }

    [TestMethod]
    public async Task PartialOnlineRefreshKeepsLocalContributorsAndShortensCacheLifetime()
    {
        using var client = new HttpClient(new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("sponsors.json", StringComparison.Ordinal) ?
                Response("""{"schemaVersion":1,"updatedUtc":"2099-01-01T00:00:00Z","sponsors":[]}""") :
                new HttpResponseMessage(HttpStatusCode.TooManyRequests))));
        using var service = CreateService(client);
        var refreshed = await RefreshAsync(service);
        Assert.IsTrue(refreshed.Contributors.Any(entry => entry.Login == "Heartestrella"));
        Assert.IsTrue(refreshed.SponsorsLoaded);
        Assert.IsNotNull(refreshed.FailureReason);
        var cached = new CreditsCacheStore(_directory).TryRead()!;
        Assert.IsTrue(cached.Partial);
        Assert.IsFalse(CreditsCachePolicy.IsFresh(cached.FetchedUtc, cached.FetchedUtc.AddHours(2), cached.Partial));
        using var reloaded = CreateService(client);
        Assert.AreEqual(0, reloaded.Current.Sponsors.Count, "部分成功缓存中的合法空赞助名单不能被内置旧名单补回。");
        Assert.IsTrue(reloaded.Current.SponsorsLoaded);
    }

    [TestMethod]
    public async Task LocalOverridesReplaceBundledListsAndKeepApiFirst()
    {
        Directory.CreateDirectory(_directory);
        var contributorsPath = Path.Combine(_directory, "override-contributors.json");
        var sponsorsPath = Path.Combine(_directory, "override-sponsors.json");
        await File.WriteAllTextAsync(contributorsPath, """[{"login":"ExplicitOverride"}]""");
        await File.WriteAllTextAsync(sponsorsPath, """{"schemaVersion":1,"sponsors":[]}""");
        var previousContributors = Environment.GetEnvironmentVariable(CreditsSourcePolicy.ContributorsSnapshotUrlOverrideVariable);
        var previousSponsors = Environment.GetEnvironmentVariable(CreditsSourcePolicy.SponsorsUrlOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(CreditsSourcePolicy.ContributorsSnapshotUrlOverrideVariable, contributorsPath);
            Environment.SetEnvironmentVariable(CreditsSourcePolicy.SponsorsUrlOverrideVariable, sponsorsPath);
            using var handler = new FakeHandler((request, _) =>
            {
                Assert.AreEqual("api.github.com", request.RequestUri!.Host);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            });
            using var client = new HttpClient(handler);
            using var service = CreateService(client);
            Assert.AreEqual(0, service.Current.Contributors.Count);
            Assert.IsFalse(service.Current.SponsorsLoaded);
            var refreshed = await RefreshAsync(service);
            Assert.AreEqual("ExplicitOverride", refreshed.Contributors.Single().Login);
            Assert.AreEqual(0, refreshed.Sponsors.Count);
            Assert.IsTrue(refreshed.SponsorsLoaded);
            Assert.IsNull(refreshed.FailureReason);
            Assert.AreEqual(1, handler.Calls);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CreditsSourcePolicy.ContributorsSnapshotUrlOverrideVariable, previousContributors);
            Environment.SetEnvironmentVariable(CreditsSourcePolicy.SponsorsUrlOverrideVariable, previousSponsors);
        }
    }

    [TestMethod]
    public async Task SupersededResponseCannotReplaceLatestRequest()
    {
        var firstRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiCalls = 0;
        using var client = new HttpClient(new FakeHandler(async (request, _) =>
        {
            if (request.RequestUri!.Host != "api.github.com")
                return Response("""{"schemaVersion":1,"updatedUtc":"2099-01-01T00:00:00Z","sponsors":[]}""");
            if (Interlocked.Increment(ref apiCalls) == 1)
            {
                firstRequested.SetResult();
                return await firstResponse.Task;
            }
            return Response("""[{"login":"LatestRequest"}]""");
        }));
        using var service = CreateService(client);
        service.RequestRefresh(isManual: true);
        await firstRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = RefreshAsync(service);
        firstResponse.SetResult(Response("""[{"login":"SupersededRequest"}]"""));
        var refreshed = await latest;
        Assert.AreEqual("LatestRequest", refreshed.Contributors.Single().Login);
        Assert.AreEqual("LatestRequest", new CreditsCacheStore(_directory).TryRead()!.Contributors.Single().Login);
    }

    [TestMethod]
    public async Task DisposedServiceDoesNotPublishLateResponseOrDisposeBorrowedClient()
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new FakeHandler(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/borrowed-client") return Response("{}");
            requested.TrySetResult();
            var result = await response.Task;
            finished.TrySetResult();
            return result;
        }));
        using var service = CreateService(client);
        var notifications = 0;
        service.SnapshotChanged += _ => Interlocked.Increment(ref notifications);
        service.RequestRefresh(isManual: true);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Dispose();
        var countAtDispose = Volatile.Read(ref notifications);
        response.SetResult(Response("""[{"login":"TooLate"}]"""));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var borrowedResponse = await client.GetAsync("https://example.invalid/borrowed-client");
        Assert.AreEqual(countAtDispose, Volatile.Read(ref notifications));
        Assert.IsFalse(File.Exists(new CreditsCacheStore(_directory).FilePath));
    }

    private CreditsService CreateService(HttpClient client) => new(null, cache: new CreditsCacheStore(_directory), httpClient: client);

    private static DateTimeOffset ReadBundledDate(string name, string property)
    {
        using var stream = typeof(CreditsService).Assembly.GetManifestResourceStream($"AFMediaBar.Resources.{name}.json")!;
        using var document = System.Text.Json.JsonDocument.Parse(stream);
        return document.RootElement.GetProperty(property).GetDateTimeOffset();
    }

    private static ContributorInfo Contributor(string login) => new(login, 1, $"https://github.com/{login}", null);

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static async Task<CreditsSnapshot> RefreshAsync(CreditsService service)
    {
        var refreshed = new TaskCompletionSource<CreditsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSnapshot(CreditsSnapshot snapshot)
        {
            if (!snapshot.IsLoading) refreshed.TrySetResult(snapshot);
        }
        service.SnapshotChanged += OnSnapshot;
        try
        {
            service.RequestRefresh(isManual: true);
            return await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { service.SnapshotChanged -= OnSnapshot; }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request, cancellationToken);
        }
    }
}
