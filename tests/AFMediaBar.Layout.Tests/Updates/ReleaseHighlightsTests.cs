// Exercises catalogue integrity, stale-cache retention and cancellation without reaching production endpoints.
using System.Net;
using System.Net.Http;
using System.Text.Json;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReleaseHighlightsTests
{
    private string? _previousOverride;

    [TestInitialize]
    public void ClearSourceOverride()
    {
        _previousOverride = Environment.GetEnvironmentVariable(ReleaseHighlightsService.OverrideVariable);
        Environment.SetEnvironmentVariable(ReleaseHighlightsService.OverrideVariable, null);
    }

    [TestCleanup]
    public void RestoreSourceOverride() => Environment.SetEnvironmentVariable(ReleaseHighlightsService.OverrideVariable, _previousOverride);

    private static string Catalogue(params string[] versions) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        releases = versions.Select(version => new
        {
            version, releaseDate = "2026-10-02",
            releaseNotesUrl = "https://github.com/Fervent-Tempo/AF-Media-Bar/releases/tag/v" + version,
            localizations = new Dictionary<string, object>
            {
                ["zh-Hans"] = new { title = "Release " + version, highlights = new[] { "Improvement" } },
                ["en"] = new { title = "Release " + version, highlights = new[] { "Improvement" } }
            }
        })
    });

    [TestMethod]
    public void NumericallySortsVersionsAndRejectsDuplicateAliases()
    {
        var records = ReleaseHighlightsPolicy.Parse(Catalogue("1.2.0", "1.10.0", "1.3.0"));
        CollectionAssert.AreEqual(new[] { "1.10.0", "1.3.0", "1.2.0" }, records.Select(item => item.Version).ToArray());
        Assert.ThrowsException<FormatException>(() => ReleaseHighlightsPolicy.Parse(Catalogue("1.2.0", "1.2.0.0")));
    }

    [TestMethod]
    public void RejectsUnsupportedSchemaBadDatesAndUnsafeReleaseLinks()
    {
        foreach (var json in new[]
        {
            Catalogue("1.0.0").Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            Catalogue("1.0.0").Replace("2026-10-02", "2026-02-30"),
            Catalogue("1.0.0").Replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\""),
            Catalogue("1.0.0").Replace("\"en\":", "\"zh-Hans\":"),
            Catalogue("1.0.0").Replace("https://github.com/", "file:///"),
            Catalogue("1.0.0").Replace("1.0.0", "not-a-version")
        })
            Assert.ThrowsException<FormatException>(() => ReleaseHighlightsPolicy.Parse(json));
        Assert.IsFalse(ReleaseHighlightsPolicy.IsSafeNotesUrl("https://github.com.evil.test/Fervent-Tempo/AF-Media-Bar/releases/tag/v1"));
    }

    [TestMethod]
    public void ManifestFillsMissingVersionWithoutReplacingReviewedTranslations()
    {
        var catalogue = ReleaseHighlightsPolicy.Parse(Catalogue("1.3.0"));
        var manifest = new UpdateManifest("1.3.0.0", null, "Different title", ["Different text"], null, null, null, false, [], null, null);
        var merged = ReleaseHighlightsPolicy.Merge(catalogue, manifest);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("Release 1.3.0", merged[0].Localizations["en"].Title);
        Assert.AreEqual("Different text", ReleaseHighlightsPolicy.ResolveText(merged[0], "zh-Hant").Text.Highlights[0]);
        merged = ReleaseHighlightsPolicy.Merge(catalogue, manifest with { Version = "1.4.0" });
        Assert.AreEqual("1.4.0", merged[0].Version);
        var fallback = ReleaseHighlightsPolicy.ResolveText(merged[0], "en");
        Assert.IsTrue(fallback.IsOriginal);
        Assert.AreEqual("Different text", fallback.Text.Highlights[0]);
    }

    [TestMethod]
    public async Task InitialContentIsOfflineAndFreshCacheAvoidsRepeatRequests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-highlights-test-" + Guid.NewGuid().ToString("N"));
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Catalogue("9.0.0")) }));
        using var client = new HttpClient(handler);
        using var reader = new ReleaseHighlightsService(client: client, cachePath: Path.Combine(directory, "cache.json"));
        try
        {
            var embedded = await reader.ReadInitialAsync(CancellationToken.None);
            Assert.IsTrue(embedded.Releases.Count > 0);
            Assert.AreEqual(0, handler.Calls);
            var fresh = await reader.LoadAsync(true, CancellationToken.None);
            Assert.AreEqual("9.0.0", fresh.Releases[0].Version);
            Assert.IsNull(fresh.FailureKey);
            var cached = await reader.LoadAsync(false, CancellationToken.None);
            Assert.AreEqual(1, handler.Calls);
            using var reopened = new ReleaseHighlightsService(client: client, cachePath: Path.Combine(directory, "cache.json"));
            var disk = await reopened.ReadInitialAsync(CancellationToken.None);
            Assert.AreEqual("9.0.0", disk.Releases[0].Version);
            Assert.IsTrue(disk.IsCached);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task FailedOrMalformedRefreshRetainsValidContentAndCacheBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-highlights-test-" + Guid.NewGuid().ToString("N"));
        var content = Catalogue("9.0.0");
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }));
        using var client = new HttpClient(handler);
        var cachePath = Path.Combine(directory, "cache.json");
        using var reader = new ReleaseHighlightsService(client: client, cachePath: cachePath);
        try
        {
            await reader.LoadAsync(true, CancellationToken.None);
            var bytes = await File.ReadAllBytesAsync(cachePath);
            content = "{invalid}";
            var failed = await reader.LoadAsync(true, CancellationToken.None);
            Assert.AreEqual("9.0.0", failed.Releases[0].Version);
            Assert.IsNotNull(failed.FailureKey);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(cachePath));
            content = new string('x', ReleaseHighlightsPolicy.MaximumBytes + 1);
            var oversized = await reader.LoadAsync(true, CancellationToken.None);
            Assert.AreEqual("9.0.0", oversized.Releases[0].Version);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(cachePath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task CancellingARequestDoesNotCommitAResult()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHandler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        var cachePath = Path.Combine(Path.GetTempPath(), "AFMediaBar-highlights-test-" + Guid.NewGuid().ToString("N"), "cache.json");
        using var reader = new ReleaseHighlightsService(client: client, cachePath: cachePath);
        using var cancellation = new CancellationTokenSource();
        var loading = reader.LoadAsync(true, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try { await loading; Assert.Fail("A cancelled request must not return a catalogue."); }
        catch (OperationCanceledException) { Assert.IsTrue(cancellation.IsCancellationRequested); }
        Assert.IsFalse(File.Exists(cachePath));
    }

    [TestMethod]
    public async Task CorruptCacheAndOfflineRefreshKeepBundledHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-highlights-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var cachePath = Path.Combine(directory, "cache.json");
        await File.WriteAllTextAsync(cachePath, "{broken}");
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("Offline"));
        using var client = new HttpClient(handler);
        using var reader = new ReleaseHighlightsService(client: client, cachePath: cachePath);
        try
        {
            var initial = await reader.ReadInitialAsync(CancellationToken.None);
            Assert.IsNull(initial.FetchedAt);
            var failed = await reader.LoadAsync(true, CancellationToken.None);
            Assert.AreEqual(initial.Releases.Count, failed.Releases.Count);
            Assert.IsNotNull(failed.FailureKey);
            Assert.AreEqual(2, handler.Calls);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task LocalOverrideReplacesAllNetworkSources()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-highlights-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.json");
        await File.WriteAllTextAsync(source, Catalogue("9.0.0"));
        Environment.SetEnvironmentVariable(ReleaseHighlightsService.OverrideVariable, source);
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("Must not reach network"));
        using var client = new HttpClient(handler);
        using var reader = new ReleaseHighlightsService(client: client, cachePath: Path.Combine(directory, "cache.json"));
        try
        {
            var loaded = await reader.LoadAsync(true, CancellationToken.None);
            Assert.AreEqual("9.0.0", loaded.Releases[0].Version);
            Assert.AreEqual(0, handler.Calls);
            Assert.IsNull(loaded.FailureKey);
        }
        finally { Directory.Delete(directory, true); }
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
