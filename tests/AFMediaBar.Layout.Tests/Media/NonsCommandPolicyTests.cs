using System.Net;
using System.Net.Http;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Media.Companions.Nons;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>队列版本变化后的重试边界与稳定错误码映射。</summary>
[TestClass]
public sealed class NonsCommandPolicyTests
{
    [TestMethod]
    public void StaleQueueRetriesOnlyWhenTargetStillExistsAndOnlyOnce()
    {
        Assert.IsTrue(NonsCommandPolicy.ShouldRetryAfterStale(targetStillPresent: true, alreadyRetried: false));
        Assert.IsFalse(NonsCommandPolicy.ShouldRetryAfterStale(targetStillPresent: false, alreadyRetried: false));
        Assert.IsFalse(NonsCommandPolicy.ShouldRetryAfterStale(targetStillPresent: true, alreadyRetried: true));
    }

    [TestMethod]
    public void TrackIdentityIsOrdinalAndDoesNotUseDisplayText()
    {
        var queue = new[]
        {
            new PlayerCompanionTrack("track-a", "Same title", [], "Album", 1, "netease",
                new PlayerCompanionReference("netease", "track", "1"), null),
            new PlayerCompanionTrack("track-b", "Same title", [], "Album", 1, "netease",
                new PlayerCompanionReference("netease", "track", "2"), null)
        };

        Assert.IsTrue(NonsCommandPolicy.ContainsTrack(queue, "track-b"));
        Assert.IsFalse(NonsCommandPolicy.ContainsTrack(queue, "TRACK-B"));
        Assert.IsFalse(NonsCommandPolicy.ContainsTrack(queue, "Same title"));
    }

    [TestMethod]
    public void ApiErrorsMapToStableCodes()
    {
        Assert.AreEqual(PlayerCompanionError.StaleQueue,
            NonsErrorMapper.Map(new NonsApiException(HttpStatusCode.Conflict, "stale_queue", string.Empty)));
        Assert.AreEqual(PlayerCompanionError.Busy,
            NonsErrorMapper.Map(new NonsApiException(HttpStatusCode.TooManyRequests, "busy", string.Empty)));
        Assert.AreEqual(PlayerCompanionError.ServiceUnavailable,
            NonsErrorMapper.Map(new NonsApiException(HttpStatusCode.ServiceUnavailable, "unavailable", string.Empty)));
        Assert.AreEqual(PlayerCompanionError.Timeout,
            NonsErrorMapper.Map(new NonsApiException(HttpStatusCode.GatewayTimeout, "timeout", string.Empty)));
        Assert.AreEqual(PlayerCompanionError.Busy,
            NonsErrorMapper.Map(new NonsApiException(HttpStatusCode.ServiceUnavailable, "too_many_connections", string.Empty)));
        Assert.AreEqual(PlayerCompanionError.ServiceUnavailable,
            NonsErrorMapper.Map(new HttpRequestException("offline")));
        Assert.AreEqual(PlayerCompanionError.Timeout,
            NonsErrorMapper.Map(new TaskCanceledException("timeout")));
    }
}
