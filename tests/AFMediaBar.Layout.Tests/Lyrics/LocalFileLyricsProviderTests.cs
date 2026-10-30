// 本地歌词来源：文件名与元数据匹配、缺歌手不丢分、无词候选不顶掉带词的那份。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LocalFileLyricsProviderTests
{
    private string _folder = string.Empty;
    private LyricsSourceSettings _original;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "afmb-lyrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _original = SettingsManager.Current.LyricsSource;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SettingsManager.Current.LyricsSource = _original;
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public async Task MatchesByArtistAndTitleInFileName()
    {
        File.WriteAllText(Path.Combine(_folder, "周杰伦 - 晴天.lrc"), "[00:01.00]故事的小黄花");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天", "周杰伦"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(LyricsSourceCatalog.LocalFile, result.Source);
        Assert.AreEqual(1, result.Document.Lines.Count);
    }

    [TestMethod]
    public async Task KeepsMatchingWhenFileHasNoArtist()
    {
        // 歌词文件没有 [ar:]，请求带歌手：缺项不能按 0 分计入，否则正确文件会被门槛挡掉。
        File.WriteAllText(Path.Combine(_folder, "晴天.lrc"), "[00:01.00]故事的小黄花");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天", "周杰伦"), CancellationToken.None);

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task PrefersTheCopyThatHasLyrics()
    {
        // 同名的空副本不能顶掉真正带词的那一份。
        Directory.CreateDirectory(Path.Combine(_folder, "empty"));
        Directory.CreateDirectory(Path.Combine(_folder, "full"));
        File.WriteAllText(Path.Combine(_folder, "empty", "晴天.lrc"), string.Empty);
        File.WriteAllText(Path.Combine(_folder, "full", "晴天.lrc"), "[00:01.00]故事的小黄花");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天", "周杰伦"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Document.Lines.Count > 0);
    }

    [TestMethod]
    public async Task ReturnsNullWhenDisabledOrUnmatched()
    {
        File.WriteAllText(Path.Combine(_folder, "晴天.lrc"), "[00:01.00]故事的小黄花");

        Enable(false, _folder);
        Assert.IsNull(await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天", "周杰伦"), CancellationToken.None));

        Enable(true, _folder);
        Assert.IsNull(await new LocalFileLyricsProvider().GetLyricsAsync(Request("完全不同的歌", "别的歌手"), CancellationToken.None));
    }

    [TestMethod]
    public async Task MatchesWhenMetadataAndFileNameBothDifferFromRequest()
    {
        // 真实曲库里的常见写法：文件名只写「歌手 - 歌名」，元数据写短标题、歌手把别名塞进括号且顺序与请求相反、
        // 文件没写时长；请求标题却带重复后缀和 (Single Version)。这几处任何一处处理不当都会漏掉正确文件。
        File.WriteAllText(
            Path.Combine(_folder, "缪斯 (μ's) - 輝夜の城で踊りたい.lrc"),
            "[ti:輝夜の城で踊りたい (欲于辉夜之城起舞)]\n[ar:缪斯 (μ's)]\n[00:01.00]第一行\n[00:02.00]第二行");
        Enable(true, _folder);
        var request = new LyricsRequest(
            "輝夜の城で踊りたい (欲于辉夜之城起舞) (欲于辉夜之城起舞) (Single Version)",
            "μ's (缪斯)", string.Empty, 271.0, null);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(request, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Document.Lines.Count);
    }

    [TestMethod]
    public async Task ReadsTranslationAndRomanizationFromQqCompanionFiles()
    {
        // QQ 音乐转出来的文件常见这组命名：主歌词 _qm，翻译 _qmts，罗马音 _qmRoma；文件名里还带时长秒。
        const string name = "22_7 - ムズイ - 247 - ムズイ (Special Edition)";
        File.WriteAllText(Path.Combine(_folder, name + "_qm.lrc"), "[ti:ムズイ]\n[00:01.00]原文一\n[00:02.00]原文二");
        File.WriteAllText(Path.Combine(_folder, name + "_qmts.lrc"), "[00:01.00]翻译一\n[00:02.00]翻译二");
        File.WriteAllText(Path.Combine(_folder, name + "_qmRoma.lrc"), "[00:01.00]genbun ichi\n[00:02.00]genbun ni");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("ムズイ", "22/7"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Document.Lines.Count);
        Assert.AreEqual("翻译一", result.Document.Lines[0].Translation);
        Assert.AreEqual("genbun ichi", result.Document.Lines[0].Romanization);
    }

    [TestMethod]
    public async Task ReadsNetEaseStyleTranslationSidecar()
    {
        File.WriteAllText(Path.Combine(_folder, "晴天.lrc"), "[00:01.00]故事的小黄花");
        File.WriteAllText(Path.Combine(_folder, "晴天.tlyric"), "[00:01.00]The story's little yellow flower");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天", "周杰伦"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("The story's little yellow flower", result.Document.Lines[0].Translation);
    }

    [TestMethod]
    public void NormalizeDropsBlankAndDuplicateFolders()
    {
        var settings = new LyricsSourceSettings(null)
        {
            LocalFolders = ["  D:\\Lyrics ", "D:\\Lyrics", "", "E:\\More"]
        }.Normalize();

        CollectionAssert.AreEqual(new[] { "D:\\Lyrics", "E:\\More" }, settings.LocalFolders!.ToArray());
    }

    private static LyricsRequest Request(string title, string artist) => new(title, artist, string.Empty, null, null);

    private static void Enable(bool localEnabled, params string[] folders)
    {
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with
        {
            LocalEnabled = localEnabled,
            LocalFolders = folders
        };
    }
}
