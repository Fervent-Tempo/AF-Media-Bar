// 本地歌词来源：文件名与元数据匹配、缺歌手不丢分、无词候选不顶掉带词的那份。
using System.IO.Compression;
using System.Text;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Lyricify.Lyrics.Decrypter.Qrc;
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
        // 歌手只是把别名塞进括号、顺序与请求相反，字符集合一致仍属独立佐证，不能被判成低置信度。
        Assert.IsFalse(result.LowConfidence);
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
    public async Task MarksTitleOnlyMatchAsLowConfidence()
    {
        // 请求没有歌手、文件也没写 [ar:] / [length:]：只有标题一条线索，同名翻唱无从区分。
        File.WriteAllText(Path.Combine(_folder, "晴天.lrc"), "[00:01.00]故事的小黄花");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天", string.Empty), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.LowConfidence);
    }

    [TestMethod]
    public async Task ArtistOrDurationCorroborationClearsLowConfidence()
    {
        File.WriteAllText(Path.Combine(_folder, "晴天.lrc"), "[ar:周杰伦]\n[00:01.00]故事的小黄花");
        Enable(true, _folder);
        var byArtist = await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天", "周杰伦"), CancellationToken.None);
        Assert.IsNotNull(byArtist);
        Assert.IsFalse(byArtist.LowConfidence, "歌手对得上就属于有独立佐证。");

        File.WriteAllText(Path.Combine(_folder, "晴天.lrc"), "[length:04:29]\n[00:01.00]故事的小黄花");
        var byDuration = await new LocalFileLyricsProvider().GetLyricsAsync(
            new LyricsRequest("晴天", string.Empty, string.Empty, 269.0, null), CancellationToken.None);
        Assert.IsNotNull(byDuration);
        Assert.IsFalse(byDuration.LowConfidence, "没有歌手时，时长对得上也算独立佐证。");
    }

    [TestMethod]
    public async Task CorroboratedCopyWinsOverTitleOnlyCopy()
    {
        // 只靠标题拿满分的翻唱副本，不能压过标题一致且歌手对得上的原版。
        File.WriteAllText(Path.Combine(_folder, "晴天.lrc"), "[00:01.00]翻唱版");
        File.WriteAllText(Path.Combine(_folder, "周杰伦 - 晴天.lrc"), "[00:01.00]原版");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天", "周杰伦"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("原版", result.Document.Lines[0].Text);
    }

    [TestMethod]
    public async Task SkipsCandidatesThatDeclareAnotherVersion()
    {
        // 同名但自称 Instrumental：它的词不能配给原版或 Live 版。
        File.WriteAllText(Path.Combine(_folder, "晴天 (Instrumental).lrc"), "[ti:晴天]\n[00:01.00]伴奏版");
        Enable(true, _folder);

        Assert.IsNull(await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天 (Live)", "周杰伦"), CancellationToken.None));
    }

    [TestMethod]
    public async Task KeepsTheSameVersionTag()
    {
        // 请求与候选都标了 Instrumental，属于同一版本，照常命中。
        File.WriteAllText(Path.Combine(_folder, "晴天 (Instrumental).lrc"), "[00:01.00]伴奏版");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天 (Instrumental)", "周杰伦"), CancellationToken.None);

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task RejectsCandidateThatNamesAnotherArtist()
    {
        // 真实场景：QQ 音乐播「Gldemo ver.」的 Girls Dead Monster，本地只有 Lia 的同名片。
        // 歌手明确对不上（Jaro-Winkler 对短串很宽容，Lia 能到 0.6），不能拿它的词顶上来。
        File.WriteAllText(Path.Combine(_folder, "My Soul, Your Beats!.lrc"), "[ar:Lia]\n[00:01.00]词");
        Enable(true, _folder);
        var request = new LyricsRequest("My Soul, Your Beats! (Gldemo ver.)", "Girls Dead Monster (ガルデモ)",
            string.Empty, 284.7, null);

        Assert.IsNull(await new LocalFileLyricsProvider().GetLyricsAsync(request, CancellationToken.None));
    }

    [TestMethod]
    public async Task IgnoresTheLabelPrefixInDownloadedFileNames()
    {
        // 「厂牌 _ 歌手 - 歌名」是下载器命名：厂牌不能算进歌手，否则歌手比较全是噪声。
        File.WriteAllText(Path.Combine(_folder, "VISUAL ARTS _ Key Sounds Label _ Lia - 晴天.lrc"), "[00:01.00]词");
        Enable(true, _folder);
        var request = new LyricsRequest("晴天 (Gldemo ver.)", "Girls Dead Monster (ガルデモ)", string.Empty, 284.7, null);

        Assert.IsNull(await new LocalFileLyricsProvider().GetLyricsAsync(request, CancellationToken.None));
    }

    [TestMethod]
    public async Task MatchesSingleCharacterTitle()
    {
        // 单字歌名以前会被 LooksRelated 的长度限制直接排除，连评分都进不去。
        File.WriteAllText(Path.Combine(_folder, "光.lrc"), "[ti:光]\n[ar:陈粒]\n[00:01.00]光");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("光", "陈粒"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.LowConfidence);
    }

    [TestMethod]
    public async Task RejectsLongerTitleForSingleCharacterRequest()
    {
        // 单字标题不能靠相似度沾上别的歌：请求「光」时，「月光」即使歌手对得上也不算。
        File.WriteAllText(Path.Combine(_folder, "月光.lrc"), "[ti:月光]\n[ar:陈粒]\n[00:01.00]词");
        Enable(true, _folder);

        Assert.IsNull(await new LocalFileLyricsProvider().GetLyricsAsync(Request("光", "陈粒"), CancellationToken.None));
    }

    [TestMethod]
    public async Task TreatsDifferentRemixDescriptionsAsLowConfidence()
    {
        // 两者的标记都是 Remix，但 DJ甲 与 DJ乙 是不同版本：不能当成确定匹配。
        File.WriteAllText(Path.Combine(_folder, "晴天 (DJ甲 Remix).lrc"),
            "[ti:晴天 (DJ甲 Remix)]\n[ar:周杰伦]\n[00:01.00]词");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(
            Request("晴天 (DJ乙 Remix)", "周杰伦"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.LowConfidence);
    }

    [TestMethod]
    public async Task KeepsSameVersionDescription()
    {
        File.WriteAllText(Path.Combine(_folder, "晴天 (Live 2010).lrc"), "[ti:晴天 (Live 2010)]\n[ar:周杰伦]\n[00:01.00]词");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(
            Request("晴天 (Live 2010)", "周杰伦"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.LowConfidence);
    }

    [TestMethod]
    public async Task TreatsContradictingArtistTagAsLowConfidence()
    {
        // 文件名写周杰伦、内部标签写王菲：一条支持一条反对，不能当成确定匹配。
        File.WriteAllText(Path.Combine(_folder, "周杰伦 - 晴天.lrc"), "[ar:王菲]\n[00:01.00]词");
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("晴天", "周杰伦"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.LowConfidence);
    }

    [TestMethod]
    public async Task ReadsLocalCachedQrc()
    {
        // 客户端缓存形态：11 字节头 + QMC1 异或，解出来是 XML 包装（LyricContent 里才是逐字时间轴），
        // 与真实缓存文件一致。按这条链反向造一份，验证解密与 XML 提取都成立。
        var encrypted = Convert.FromHexString(EncryptQrcPayload(QrcFullXml("[ti:光]\n[ar:陈粒]\n[0,1500]光(0,500)")));
        var file = new byte[11 + encrypted.Length];
        new byte[] { 0x98, 0x25, 0xB0, 0xAC, 0xE3, 0x02, 0x83, 0x68, 0xE8, 0xFC, 0x6C }.CopyTo(file, 0);
        encrypted.CopyTo(file, 11);
        for (var i = 0; i < file.Length; i++) file[i] ^= Qmc1Key[i & 0x7F];
        File.WriteAllBytes(Path.Combine(_folder, "光_qm.qrc"), file);
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("光", "陈粒"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("光", result.Document.Lines[0].Text);
    }

    [TestMethod]
    public async Task PairsQrcTranslationAndRomanization()
    {
        // QQ 的译文/罗马音是独立的 .qrc：主歌词命中后应把同目录的 _qmts / _qmRoma 按行并入。
        File.WriteAllText(Path.Combine(_folder, "光_qm.qrc"), EncryptQrcPayload("[ti:光]\n[ar:陈粒]\n[00:01.00]光"));
        File.WriteAllText(Path.Combine(_folder, "光_qmts.qrc"), EncryptQrcPayload("[00:01.00]光（译文）"));
        File.WriteAllText(Path.Combine(_folder, "光_qmRoma.qrc"), EncryptQrcPayload("[00:01.00]guang"));
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("光", "陈粒"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("光", result.Document.Lines[0].Text);
        Assert.AreEqual("光（译文）", result.Document.Lines[0].Translation);
        Assert.AreEqual("guang", result.Document.Lines[0].Romanization);
    }

    [TestMethod]
    public async Task ReadsCloudHexQrc()
    {
        // 云端形态：十六进制文本，直接交给解密器。
        File.WriteAllText(Path.Combine(_folder, "光.qrc"),
            EncryptQrcPayload("[ti:光]\n[ar:陈粒]\n[00:01.00]光"));
        Enable(true, _folder);

        var result = await new LocalFileLyricsProvider().GetLyricsAsync(Request("光", "陈粒"), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("光", result.Document.Lines[0].Text);
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

    /// <summary>真实缓存解出来的 QRC Full 结构：逐字时间轴放在 LyricContent 属性里，行与行之间是真实换行（不转义）。</summary>
    private static string QrcFullXml(string content)
    {
        var escaped = content.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?><QrcInfos><QrcHeadInfo SaveTime=\"1\" Version=\"100\"/>"
             + $"<LyricInfo LyricCount=\"1\"><Lyric_1 LyricType=\"1\" LyricContent=\"{escaped}\"/></LyricInfo></QrcInfos>";
    }

    /// <summary>按 QRC 的加密链反向造样本：zlib → 补零到 8 字节 → 3DES → 十六进制。</summary>
    private static string EncryptQrcPayload(string plain)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(Encoding.UTF8.GetBytes(plain));
        var compressed = buffer.ToArray();
        var padded = new byte[(compressed.Length + 7) / 8 * 8];
        compressed.CopyTo(padded, 0);

        var schedule = new byte[3][][];
        for (var k = 0; k < 3; k++)
        {
            schedule[k] = new byte[16][];
            for (var j = 0; j < 16; j++) schedule[k][j] = new byte[6];
        }
        DESHelper.TripleDESKeySetup(Encoding.ASCII.GetBytes("!@#)(*$%123ZXC!@!@#)(NHL"), schedule, DESHelper.ENCRYPT);

        var encrypted = new byte[padded.Length];
        var block = new byte[8];
        var temp = new byte[8];
        for (var i = 0; i < padded.Length; i += 8)
        {
            Buffer.BlockCopy(padded, i, block, 0, 8);
            DESHelper.TripleDESCrypt(block, temp, schedule);
            Buffer.BlockCopy(temp, 0, encrypted, i, 8);
        }
        return Convert.ToHexString(encrypted);
    }

    /// <summary>与生产实现同一张 QMC1 掩码表：测试造缓存形态的样本时要用它异或回去。</summary>
    private static readonly byte[] Qmc1Key =
    [
        0xc3, 0x4a, 0xd6, 0xca, 0x90, 0x67, 0xf7, 0x52, 0xd8, 0xa1, 0x66, 0x62, 0x9f, 0x5b, 0x09, 0x00,
        0xc3, 0x5e, 0x95, 0x23, 0x9f, 0x13, 0x11, 0x7e, 0xd8, 0x92, 0x3f, 0xbc, 0x90, 0xbb, 0x74, 0x0e,
        0xc3, 0x47, 0x74, 0x3d, 0x90, 0xaa, 0x3f, 0x51, 0xd8, 0xf4, 0x11, 0x84, 0x9f, 0xde, 0x95, 0x1d,
        0xc3, 0xc6, 0x09, 0xd5, 0x9f, 0xfa, 0x66, 0xf9, 0xd8, 0xf0, 0xf7, 0xa0, 0x90, 0xa1, 0xd6, 0xf3,
        0xc3, 0xf3, 0xd6, 0xa1, 0x90, 0xa0, 0xf7, 0xf0, 0xd8, 0xf9, 0x66, 0xfa, 0x9f, 0xd5, 0x09, 0xc6,
        0xc3, 0x1d, 0x95, 0xde, 0x9f, 0x84, 0x11, 0xf4, 0xd8, 0x51, 0x3f, 0xaa, 0x90, 0x3d, 0x74, 0x47,
        0xc3, 0x0e, 0x74, 0xbb, 0x90, 0xbc, 0x3f, 0x92, 0xd8, 0x7e, 0x11, 0x13, 0x9f, 0x23, 0x95, 0x5e,
        0xc3, 0x00, 0x09, 0x5b, 0x9f, 0x62, 0x66, 0xa1, 0xd8, 0x52, 0xf7, 0x67, 0x90, 0xca, 0xd6, 0x4a
    ];

    private static void Enable(bool localEnabled, params string[] folders)
    {
        SettingsManager.Current.LyricsSource = SettingsManager.Current.LyricsSource with
        {
            LocalEnabled = localEnabled,
            LocalFolders = folders
        };
    }
}
