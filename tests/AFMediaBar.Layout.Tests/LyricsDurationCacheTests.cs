// 验证 SMTC 时间轴时长变化不会清空歌词或重复发起请求；使用内存提供器，不访问网络。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LyricsDurationCacheTests
{
    private bool _allowBrowser;
    private LyricsSourceSettings _sources;
    private static LyricsRequest Request(double? duration) =>
        new("When It Snows", "Sarah Kang & Sam Ock", "Album", duration, null);

    [TestInitialize]
    public void Initialize()
    {
        _allowBrowser = SettingsManager.Current.AllowBrowserAndVideoLyrics;
        _sources = SettingsManager.Current.LyricsSource;
        SettingsManager.Current.AllowBrowserAndVideoLyrics = true;
        SettingsManager.Current.LyricsSource = LyricsSourceSettings.Default;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SettingsManager.Current.AllowBrowserAndVideoLyrics = _allowBrowser;
        SettingsManager.Current.LyricsSource = _sources;
    }

    [TestMethod]
    public void DurationChangeReusesCompletedLyricsAndKeepsMatchingDuration()
    {
        var provider = new Provider();
        using var builder = new MediaSnapshotBuilder(new LyricsService(provider));
        builder.GetLyrics("session", "MSEdge", Request(342.4));

        Assert.AreSame(provider.Result, builder.GetLyrics("session", "MSEdge", Request(352.4)));
        Assert.AreSame(provider.Result, builder.GetLyrics("session", "MSEdge", Request(null)));
        Assert.AreEqual(1, provider.Requests.Count);
        Assert.AreEqual(342.4, provider.Requests[0].DurationSeconds);
    }

    [TestMethod]
    public void DurationChangeDeduplicatesPendingAndOnlineFallbackRequests()
    {
        var provider = new Provider { Pending = true };
        using var builder = new MediaSnapshotBuilder(new LyricsService(provider));
        Assert.IsNull(builder.GetLyrics("session", "MSEdge", Request(342.4)));
        Assert.IsNull(builder.GetLyrics("session", "MSEdge", Request(352.4)));
        builder.RequestOnlineLyrics("session", "MSEdge", "When It Snows", "Sarah Kang & Sam Ock", 362.4);
        Assert.AreEqual(1, provider.Requests.Count);
    }

    [TestMethod]
    public void OnlineFallbackReusesCachedAlbumRequestDespiteDurationChange()
    {
        var provider = new Provider();
        using var builder = new MediaSnapshotBuilder(new LyricsService(provider));
        builder.GetLyrics("session", "MSEdge", Request(342.4));
        builder.RequestOnlineLyrics("session", "MSEdge", "When It Snows", "Sarah Kang & Sam Ock", 352.4);
        Assert.AreEqual(1, provider.Requests.Count);
    }

    [TestMethod]
    public void DifferentTrackAlbumSessionOrSourceStillFetches()
    {
        var provider = new Provider();
        using var builder = new MediaSnapshotBuilder(new LyricsService(provider));
        builder.GetLyrics("session", "MSEdge", Request(342.4));
        builder.GetLyrics("session", "MSEdge", Request(342.4) with { Title = "Other song" });
        builder.GetLyrics("session", "MSEdge", Request(342.4) with { Album = "Other album" });
        builder.GetLyrics("other session", "MSEdge", Request(342.4));
        builder.GetLyrics("session", "Other player", Request(342.4));
        Assert.AreEqual(5, provider.Requests.Count);
    }

    private sealed class Provider : ILyricsProvider
    {
        public string SourceName => "QQMusic";
        public LyricsResult Result { get; } = new("QQMusic", LyricDocument.Empty) { MatchScore = 100 };
        public List<LyricsRequest> Requests { get; } = [];
        public bool Pending { get; init; }

        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Pending ? WaitForCancellation(cancellationToken) : Task.FromResult<LyricsResult?>(Result);
        }

        private static async Task<LyricsResult?> WaitForCancellation(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
