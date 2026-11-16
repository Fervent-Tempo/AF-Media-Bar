using System.Net;
using System.Net.Http;
using System.Text.Json;
using AFMediaBar.Classes.Services.Media.Companions.Nons;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>锁定 Nons 1.12.4 的播放器/队列 JSON 与命令字段，防止接口漂移时静默读错队列。</summary>
[TestClass]
public sealed class NonsApiContractTests
{
    private const string PlayerJson = """
        {
          "sequence": 1026,
          "queueRevision": 7,
          "revision": 3,
          "status": "paused",
          "index": 0,
          "queueLength": 2,
          "current": {
            "trackId": "track-a",
            "title": "Song A",
            "artists": ["Artist A", "Artist B"],
            "album": "Album",
            "durationMs": 180000,
            "source": "netease",
            "reference": { "source": "netease", "kind": "track", "id": "1001" },
            "coverUrl": "/v1/cover?trackId=track-a"
          },
          "positionMs": 45000,
          "durationMs": 180000,
          "volume": 0.5,
          "shuffle": true,
          "repeatMode": "off",
          "actualQuality": null,
          "playbackMode": null,
          "error": null
        }
        """;

    private const string QueueJson = """
        {
          "sequence": 1026,
          "queueRevision": 7,
          "items": [
            {
              "trackId": "track-a",
              "title": "Song A",
              "artists": ["Artist A", "Artist B"],
              "album": "Album",
              "durationMs": 180000,
              "source": "netease",
              "reference": { "source": "netease", "kind": "track", "id": "1001" },
              "coverUrl": "/v1/cover?trackId=track-a"
            },
            {
              "trackId": "track-b",
              "title": "Song B",
              "artists": ["Artist C"],
              "album": "Album 2",
              "durationMs": 120000,
              "source": "netease",
              "reference": { "source": "netease", "kind": "track", "id": "1002" },
              "coverUrl": null
            }
          ],
          "shuffleOrder": [0, 1]
        }
        """;

    [TestMethod]
    public void PlayerAndQueuePayloadsDeserializeWithStableIdentity()
    {
        var player = JsonSerializer.Deserialize<NonsPlayer>(PlayerJson, NonsApiClient.JsonOptions);
        var queue = JsonSerializer.Deserialize<NonsQueue>(QueueJson, NonsApiClient.JsonOptions);

        Assert.IsNotNull(player);
        Assert.IsNotNull(queue);
        Assert.AreEqual(7L, player.QueueRevision);
        Assert.AreEqual("paused", player.Status);
        Assert.AreEqual(0, player.Index);
        Assert.IsTrue(player.Shuffle);
        Assert.AreEqual("off", player.RepeatMode);
        Assert.AreEqual("track-a", player.Current!.TrackId);
        Assert.AreEqual("1001", player.Current.Reference!.Id);
        Assert.AreEqual(2, queue.Items.Length);
        Assert.AreEqual("track-b", queue.Items[1].TrackId);
        CollectionAssert.AreEqual(new[] { 0, 1 }, queue.ShuffleOrder);
    }

    [TestMethod]
    public void CommandsUseCamelCaseAndExpectedQueueRevision()
    {
        using var jump = JsonDocument.Parse(NonsApiClient.SerializeJumpCommand(7, "track-a"));
        Assert.AreEqual("jump", jump.RootElement.GetProperty("action").GetString());
        Assert.AreEqual(7L, jump.RootElement.GetProperty("queueRevision").GetInt64());
        Assert.AreEqual("track-a", jump.RootElement.GetProperty("trackId").GetString());

        using var shuffle = JsonDocument.Parse(NonsApiClient.SerializeShuffleCommand(8, true));
        Assert.AreEqual("shuffle", shuffle.RootElement.GetProperty("action").GetString());
        Assert.AreEqual(8L, shuffle.RootElement.GetProperty("queueRevision").GetInt64());
        Assert.IsTrue(shuffle.RootElement.GetProperty("shuffle").GetBoolean());
    }

    [TestMethod]
    public async Task ErrorBodyKeepsStableCodeInsteadOfLocalizedText()
    {
        using var http = new HttpClient(new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("""{"code":"stale_queue","message":"播放队列已变化"}""")
        }))
        {
            BaseAddress = NonsApiClient.DefaultBaseAddress
        };
        using var client = new NonsApiClient(http);

        var exception = await Assert.ThrowsExceptionAsync<NonsApiException>(() =>
            client.GetPlayerAsync(CancellationToken.None));
        Assert.AreEqual("stale_queue", exception.Code);
        Assert.AreEqual(HttpStatusCode.Conflict, exception.StatusCode);
    }

    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }
}
