// 验证用户分隔符在搜索、评分和缓存失效中一致生效；全部使用内存数据，不访问歌词网络接口。
using System.Text.Json;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LyricsArtistSeparatorsTests
{
    private static LyricsRequest Request(string artist, string separators = "") =>
        new("When It Snows", artist, "Album", 342.4, null, PlaybackSourceId: "MSEdge")
        { ArtistSeparators = separators };

    [TestMethod]
    public void SplitsMultipleLiteralSeparatorsAndIgnoresCase()
    {
        var request = Request("Sarah Kang & Sam Ock FEAT. Guest", " & \r\n feat. \n\n");
        CollectionAssert.AreEqual(new[] { "Sarah Kang", "Sam Ock", "Guest" }, LyricsArtistPolicy.Split(request));
        Assert.AreEqual("Sarah Kang & Sam Ock FEAT. Guest", request.Artist);
    }

    [TestMethod]
    public void PreservesSpacesAndNamesContainingUnconfiguredPunctuation()
    {
        CollectionAssert.AreEqual(new[] { "AC/DC", "Earth, Wind & Fire" },
            LyricsArtistPolicy.Split(Request("AC/DC / Earth, Wind & Fire", " / ")));
        CollectionAssert.AreEqual(new[] { "A&B", "C" }, LyricsArtistPolicy.Split(Request("A&B & C", " & ")));
    }

    [TestMethod]
    public void LongestSeparatorWinsAndEmptyOrDuplicateArtistsAreRemoved()
    {
        CollectionAssert.AreEqual(new[] { "A", "B" },
            LyricsArtistPolicy.Split(Request("A && B && A &&", "&\n && \n&")));
        CollectionAssert.AreEqual(new[] { "" }, LyricsArtistPolicy.Split(Request(" && ", " && ")));
        CollectionAssert.AreEqual(new[] { "A B" }, LyricsArtistPolicy.Split(Request("A B", " \r\n\t")));
    }

    [TestMethod]
    public void EveryPlayerUsesTheSameRulesAndCanDisableSlashSplitting()
    {
        foreach (var source in new[] { "QQMusic", "MSEdge", "OtherPlayer" })
        {
            CollectionAssert.AreEqual(new[] { "A", "B" },
                LyricsArtistPolicy.Split(Request("A/B", new AppSettings().LyricsArtistSeparators) with { PlaybackSourceId = source }));
            CollectionAssert.AreEqual(new[] { "AC/DC" },
                LyricsArtistPolicy.Split(Request("AC/DC", "") with { PlaybackSourceId = source }));
            CollectionAssert.AreEqual(new[] { "AC/DC", "Guest" },
                LyricsArtistPolicy.Split(Request("AC/DC & Guest", " & ") with { PlaybackSourceId = source }));
        }
    }

    [TestMethod]
    public void BothSidesOfScoringUseTheSameSeparators()
    {
        var request = Request("Sarah Kang & Sam Ock", " & ");
        Assert.AreEqual(1d, LyricsMetadataScore.Explain(request, [request.Title], ["Sam Ock"], [request.Album], 342.4).Artist);
        Assert.AreEqual(1d, LyricsMetadataScore.Explain(Request("Sam Ock", " & "),
            [request.Title], ["Sarah Kang & Sam Ock"], [request.Album], 342.4).Artist);
    }

    [TestMethod]
    public void SearchIncludesIndividualArtistsBeforeTitleOnlyFallback()
    {
        var queries = LyricsSearchQueryPolicy.Build(Request("Sarah Kang & Sam Ock", " & "));
        CollectionAssert.Contains(queries.ToArray(), "When It Snows Sarah Kang");
        CollectionAssert.Contains(queries.ToArray(), "When It Snows Sam Ock");
        Assert.AreEqual("When It Snows", queries[^1]);
        CollectionAssert.AreEqual(new[] { "Sarah Kang Sam Ock", "Sarah Kang", "Sam Ock" },
            LyricsSearchQueryPolicy.BuildArtists(Request("Sarah Kang & Sam Ock", " & ")).ToArray());
    }

    [TestMethod]
    public void SettingSurvivesSerializationCloneAndNormalizeWithoutTrimming()
    {
        var settings = new AppSettings { LyricsArtistSeparators = " & \r\n feat. " };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.AreEqual(settings.LyricsArtistSeparators, loaded.Normalize().Clone().LyricsArtistSeparators);
        Assert.AreEqual("/", JsonSerializer.Deserialize<AppSettings>("{}")!.LyricsArtistSeparators);
        Assert.AreEqual("", JsonSerializer.Deserialize<AppSettings>("{\"LyricsArtistSeparators\":\"\"}")!
            .Normalize().Clone().LyricsArtistSeparators);
    }

    [TestMethod]
    public void ChangingSeparatorsInvalidatesCachedLyricsAndSnapshotsNewRules()
    {
        var original = SettingsManager.Current.Clone();
        try
        {
            SettingsManager.Current.AllowBrowserAndVideoLyrics = true;
            SettingsManager.Current.LyricsSource = LyricsSourceSettings.Default;
            SettingsManager.Current.LyricsArtistSeparators = "";
            var provider = new CaptureProvider();
            using var builder = new MediaSnapshotBuilder(new LyricsService(provider));
            builder.GetLyrics("session", "MSEdge", Request("Sarah Kang & Sam Ock"));
            SettingsManager.SetLyricsArtistSeparators(" & ");
            builder.GetLyrics("session", "MSEdge", Request("Sarah Kang & Sam Ock"));
            Assert.AreEqual(2, provider.Requests.Count);
            Assert.AreEqual("", provider.Requests[0].ArtistSeparators);
            Assert.AreEqual(" & ", provider.Requests[1].ArtistSeparators);
            Assert.IsTrue(LyricsCacheInvalidationPolicy.ShouldClearCache(nameof(AppSettings.LyricsArtistSeparators), null));
        }
        finally { SettingsManager.Replace(original); }
    }

    private sealed class CaptureProvider : AFMediaBar.Classes.Abstractions.ILyricsProvider
    {
        public string SourceName => "QQMusic";
        public List<LyricsRequest> Requests { get; } = [];
        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<LyricsResult?>(new("QQMusic", LyricDocument.Empty) { MatchScore = 100 });
        }
    }
}
