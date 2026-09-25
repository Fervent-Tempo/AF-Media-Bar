using System.IO.Compression;
using System.Text;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 酷狗本地缓存的目录配置、文件匹配与 KRC 解密回归测试。
/// Regression tests for Kugou cache configuration, file matching, and KRC decryption.
/// </summary>
[TestClass]
public sealed class KugouLocalCacheTests
{
    private const string FileName = "Artist - Song-0123456789abcdef0123456789abcdef-12345-00000000.krc";

    [TestMethod]
    public void LyricPathIsReadFromTheConfiguredIniSection()
    {
        using var reader = new StringReader("[Other]\nLyricPath=C:\\Wrong\n[LyricConfigSection]\nLyricPath=D:\\KuGou\\Lyric\\\n");

        var path = KugouLocalCache.ParseLyricPath(reader, CancellationToken.None);

        Assert.AreEqual(@"D:\KuGou\Lyric\", path);
    }

    [TestMethod]
    public void UnicodeIniResolvesAnExistingLyricDirectory()
    {
        var folder = Directory.CreateTempSubdirectory("afmb-kugou-config-");
        try
        {
            var lyricDirectory = Directory.CreateDirectory(Path.Combine(folder.FullName, "Lyric"));
            File.WriteAllText(
                Path.Combine(folder.FullName, "KuGou.ini"),
                $"[LyricConfigSection]\r\nLyricPath={lyricDirectory.FullName}\r\n",
                Encoding.Unicode);

            Assert.AreEqual(
                lyricDirectory.FullName,
                KugouLocalCache.ReadLyricPathFromIni(folder.FullName, CancellationToken.None));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void CacheFileRequiresExactArtistAndTitle()
    {
        Assert.IsTrue(KugouLocalCache.FileNameMatches(FileName, "Artist - Song"));
        Assert.IsFalse(KugouLocalCache.FileNameMatches(FileName, "Other Artist - Song"));
        Assert.IsFalse(KugouLocalCache.FileNameMatches(FileName, "Artist - Other Song"));
    }

    [TestMethod]
    public void LocalKrcIsDecryptedAndWrongDurationIsRejected()
    {
        var folder = Directory.CreateTempSubdirectory("afmb-kugou-cache-");
        try
        {
            var plain = "[ti:Song]\n[ar:Artist]\n[total:200000]\n[1000,2000]<0,500,0>Hello<500,1500,0>world";
            var encrypted = EncryptKrc(plain);
            Assert.AreEqual(plain, Lyricify.Lyrics.Decrypter.Krc.Decrypter.DecryptLyrics(Convert.ToBase64String(encrypted)));
            File.WriteAllBytes(Path.Combine(folder.FullName, FileName), encrypted);

            var matching = new LyricsRequest("Song", "Artist", string.Empty, 200, null);
            var cached = KugouLocalCache.TryReadFromDirectory(folder.FullName, matching, CancellationToken.None);
            Assert.IsNotNull(cached);
            Assert.AreEqual(plain, cached.Value.Main);

            var wrongDuration = matching with { DurationSeconds = 240 };
            Assert.IsNull(KugouLocalCache.TryReadFromDirectory(folder.FullName, wrongDuration, CancellationToken.None));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static byte[] EncryptKrc(string text)
    {
        ReadOnlySpan<byte> key =
        [
            0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47,
            0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69
        ];
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            // Lyricify 的 KRC 解密器会跳过解压内容的首字节；真实缓存前面有一个非歌词字节。
            // Lyricify's KRC decoder skips the first decompressed byte; real cache files have a non-lyric prefix.
            zlib.WriteByte(0);
            zlib.Write(Encoding.UTF8.GetBytes(text));
        }

        var payload = compressed.ToArray();
        var result = new byte[4 + payload.Length];
        "krc1"u8.CopyTo(result);
        for (var index = 0; index < payload.Length; index++)
        {
            result[index + 4] = (byte)(payload[index] ^ key[index % key.Length]);
        }

        return result;
    }
}
