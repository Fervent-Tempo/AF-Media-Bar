using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LyricsSearchTests
{
    [DataTestMethod]
    [DataRow("")]
    [DataRow("  ")]
    public async Task MissingAlbumDoesNotRejectOtherwiseMatchingTrack(string album)
    {
        var request = new LyricsRequest("远航星的告别", "鸣潮先约电台", album, null, null);
        var candidate = new QQMusicSearchResult(request.Title,
            [request.Artist, "jixwang", "Tarokiki", "Emi Evans"], "星轨消逝之夜", null, 225000, "fixture", "fixture");
        var track = LyricsSearch.ToTrackMetadata(request);
        var match = CompareHelper.CompareTrack(track, candidate);
        Assert.IsTrue(match >= CompareHelper.MatchType.High, $"Rejected matching track: {match}");
        // 复用库的筛选路径，只替换联网获取候选的步骤。
        // Exercise the library's selection path, replacing only the network candidate lookup.
        var selected = await SearchHelper.Search(track, new FixtureSearcher(candidate), CompareHelper.MatchType.High);
        Assert.AreSame(candidate, selected);
        Assert.AreEqual(album, request.Album);
    }

    [TestMethod]
    public void KnownAlbumAndDurationRemainAvailableForMatching()
    {
        var request = new LyricsRequest("远航星的告别", "鸣潮先约电台", "星轨消逝之夜", 225, null);
        var track = LyricsSearch.ToTrackMetadata(request);
        Assert.AreEqual(request.Album, track.Album);
        Assert.AreEqual(225000, track.DurationMs);
    }

    [TestMethod]
    public async Task MissingAlbumDoesNotAdmitUnrelatedTrack()
    {
        var track = LyricsSearch.ToTrackMetadata(new LyricsRequest("远航星的告别", "鸣潮先约电台", "", null, null));
        var candidate = new QQMusicSearchResult("Loafers", ["BoyWithUke"], "Faded", null, 215000, "fixture", "fixture");
        Assert.IsNull(await SearchHelper.Search(track, new FixtureSearcher(candidate), CompareHelper.MatchType.High));
    }

    private sealed class FixtureSearcher(ISearchResult candidate) : Searcher
    {
        public override string Name => "Fixture";
        public override string DisplayName => Name;
        public override Searchers SearcherType => Searchers.QQMusic;
        public override Task<List<ISearchResult>?> SearchForResults(string searchString) =>
            Task.FromResult<List<ISearchResult>?>([candidate]);
    }
}
