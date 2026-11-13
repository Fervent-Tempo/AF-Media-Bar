using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Media.Companions.Nons;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>队列事件与播放器事件的归并、当前项和版本过期判定。</summary>
[TestClass]
public sealed class NonsStateReducerTests
{
    private static readonly PlayerCompanionCapabilities Capabilities = new(true, true, true, false, false, false, false);
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ResetAndStateProduceCurrentQueueAndPlayback()
    {
        var reducer = new NonsStateReducer();
        reducer.Reset(Player(7, 0, "track-a", "paused"), Queue(7, "track-a", "track-b"));

        var snapshot = Snapshot(reducer);
        Assert.AreEqual(7L, snapshot.QueueRevision);
        Assert.AreEqual("track-a", snapshot.CurrentTrack!.TrackId);
        Assert.AreEqual(PlayerCompanionPlaybackStatus.Paused, snapshot.Player!.Status);
        Assert.IsFalse(snapshot.IsStale);

        reducer.ApplyState(new NonsState(8, 7, 4, "playing", 1, 0.5, false, "off", null, null, null));
        snapshot = Snapshot(reducer);

        Assert.AreEqual("track-b", snapshot.CurrentTrack!.TrackId);
        Assert.AreEqual("track-b", snapshot.Player!.CurrentTrackId);
        Assert.AreEqual(PlayerCompanionPlaybackStatus.Playing, snapshot.Player.Status);
        Assert.IsFalse(snapshot.IsStale);
    }

    [TestMethod]
    public void QueueRevisionMismatchMarksSnapshotStaleUntilQueueArrives()
    {
        var reducer = new NonsStateReducer();
        reducer.Reset(Player(7, 0, "track-a", "paused"), Queue(7, "track-a", "track-b"));
        reducer.ApplyState(new NonsState(9, 8, 5, "playing", 0, 0.5, false, "off", null, null, null));

        var snapshot = Snapshot(reducer);
        Assert.IsTrue(reducer.IsQueueStale);
        Assert.IsTrue(snapshot.IsStale);
        Assert.AreEqual(7L, snapshot.QueueRevision);

        reducer.ApplyQueue(Queue(8, "track-a", "track-b", "track-c"));
        snapshot = Snapshot(reducer);
        Assert.IsFalse(reducer.IsQueueStale);
        Assert.IsFalse(snapshot.IsStale);
        Assert.AreEqual(8L, snapshot.QueueRevision);
        Assert.AreEqual(3, snapshot.Queue.Count);
    }

    [TestMethod]
    public void ProgressUpdatesPositionWithoutPublishingAnotherQueue()
    {
        var reducer = new NonsStateReducer();
        reducer.Reset(Player(7, 0, "track-a", "playing"), Queue(7, "track-a", "track-b"));

        reducer.ApplyProgress(new NonsProgress(10, 3, 65000, 180000, "paused"));
        var snapshot = Snapshot(reducer);

        Assert.AreEqual(65000L, snapshot.Player!.PositionMs);
        Assert.AreEqual(2, snapshot.Queue.Count);
        Assert.AreEqual(PlayerCompanionPlaybackStatus.Paused, snapshot.Player.Status);
    }

    [TestMethod]
    public void EmptyQueueHasNoCurrentTrackAndDoesNotThrow()
    {
        var reducer = new NonsStateReducer();
        reducer.Reset(Player(1, null, null, "stopped"), new NonsQueue(1, 1, [], []));

        var snapshot = Snapshot(reducer);

        Assert.AreEqual(0, snapshot.Queue.Count);
        Assert.IsNull(snapshot.Player!.Index);
        Assert.IsNull(snapshot.CurrentTrack);
    }

    [TestMethod]
    public void ReconnectingSnapshotIsStale()
    {
        var reducer = new NonsStateReducer();
        reducer.Reset(Player(7, 0, "track-a", "paused"), Queue(7, "track-a"));

        var snapshot = Snapshot(reducer, PlayerCompanionConnectionState.Reconnecting);

        Assert.IsTrue(snapshot.IsStale);
    }

    private static PlayerCompanionSnapshot Snapshot(
        NonsStateReducer reducer,
        PlayerCompanionConnectionState state = PlayerCompanionConnectionState.Connected) =>
        reducer.ToSnapshot(state, Capabilities, 1, "1.12.4", PlayerCompanionError.None, null, stale: false, Now);

    private static NonsPlayer Player(long queueRevision, int? index, string? trackId, string status) => new(
        10,
        queueRevision,
        3,
        status,
        index,
        index is null ? 0 : 1,
        trackId is null ? null : Track(trackId),
        0,
        180000,
        0.5,
        false,
        "off",
        null,
        null,
        null);

    private static NonsQueue Queue(long revision, params string[] trackIds) => new(
        10,
        revision,
        trackIds.Select(Track).ToArray(),
        trackIds.Select((_, index) => index).ToArray());

    private static NonsTrack Track(string id) => new(
        id,
        id,
        ["Artist"],
        "Album",
        180000,
        "netease",
        new NonsReference("netease", "track", id),
        null);
}
