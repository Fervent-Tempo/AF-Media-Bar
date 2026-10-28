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
}
