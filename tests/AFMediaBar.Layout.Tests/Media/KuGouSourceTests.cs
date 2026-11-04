// 验证酷狗双通道身份、取词所有权与取消后的资源交接；不连接播放器或网络。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Services.Media.Sources.KuGou;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class KuGouSourceTests
{
    [DataTestMethod]
    [DataRow("TSAR、SaMZIng、PonyCat", "QUAKE", true)]
    [DataRow(" tsar , SaMZIng , PonyCat ", "QUAKE", true)]
    [DataRow("TSAR", "QUAKE", true)]
    [DataRow("PonyCat", "QUAKE", false)]
    [DataRow("TSAR、Other", "QUAKE", false)]
    [DataRow("TSAR、SaMZIng、PonyCat", "Other track", false)]
    public void CoverMatchingNormalizesArtistSeparatorsWithoutAcceptingAnotherTrack(string smtcArtist, string title, bool sameTrack)
    {
        var artwork = new System.Windows.Media.DrawingImage();
        artwork.Freeze();
        var memory = MediaSnapshot.Disconnected with
        {
            IsConnected = true, SourceId = "kugou", Title = "QUAKE", Artist = "TSAR,SaMZIng,PonyCat"
        };
        var session = memory with { SourceId = "KuGou.exe", Title = title, Artist = smtcArtist, Artwork = artwork };
        var merged = new KuGouSourcePolicy().Merge(memory, session);
        if (sameTrack) Assert.AreSame(artwork, merged.Artwork);
        else Assert.IsNull(merged.Artwork, "不同曲目或仅匹配客串歌手时不能继承封面。");
    }

    [TestMethod]
    public void RegistryKeepsIndependentIdentityAndOnlyInheritsMatchingSessionData()
    {
        StaTest.Run(dispatcher =>
        {
            using var kuGou = new KuGouMediaProvider(new LyricsService(), dispatcher);
            using var netEase = new NetEaseMediaProvider(new LyricsService(), dispatcher,
                () => throw new InvalidOperationException("测试不得读取进程。"));
            var registry = new MediaSourceRegistry([kuGou, netEase]);
            var memory = MediaSnapshot.Disconnected with
            {
                IsConnected = true, IsPlaying = true, SourceId = "kugou", Title = "Track", Artist = "Artist",
                Position = 17, Duration = 200
            };
            var snapshots = new Dictionary<IMediaSourceProvider, MediaSnapshot?> { [kuGou] = memory };
            var browser = new MediaSourceCandidate("browser", "chrome", false, "browser");
            var candidates = registry.BuildCandidates([browser], snapshots);
            var independent = candidates.Single(item => item.SourceId == "kugou");
            Assert.AreEqual(KuGouSourcePolicy.SelectionKey, independent.Key);
            Assert.IsNull(independent.SessionKey);
            Assert.AreEqual("browser", candidates[0].Key);
            Assert.IsTrue(registry.DiscoverSourceIds([]).Contains("kugou"));
            Assert.IsTrue(registry.IsAllowed("KuGou.exe", new(true, ["KGMUSIC"])));

            candidates = registry.BuildCandidates([browser, new("native", "KuGou.exe", true, "native")], snapshots);
            independent = candidates.Single(item => item.SourceId == "kugou");
            Assert.AreEqual(KuGouSourcePolicy.SelectionKey, independent.Key);
            Assert.AreEqual("native", independent.SessionKey);
            var session = memory with { SourceId = "KuGou.exe", CanPlayPause = true, Lyrics = LyricsResult.NoLyrics("session") };
            var merged = registry.ResolveSnapshot(independent.Key, session, snapshots, new());
            Assert.IsTrue(merged!.CanPlayPause);
            Assert.AreEqual(17d, merged.Position);
            Assert.AreSame(session.Lyrics, merged.Lyrics);
            merged = registry.ResolveSnapshot(independent.Key, session with { Title = "Other" }, snapshots, new());
            Assert.IsNull(merged!.Lyrics, "不同歌曲不能继承旧歌词。");
            merged = registry.ResolveSnapshot(independent.Key, session with { SourceId = "chrome" }, snapshots, new());
            Assert.IsFalse(merged!.CanPlayPause, "不能用其他播放器的会话控制酷狗。");
            snapshots[kuGou] = null;
            merged = registry.ResolveSnapshot(independent.Key, session, snapshots, new());
            Assert.AreEqual("kugou", merged!.SourceId);
            Assert.AreSame(session.Lyrics, merged.Lyrics, "读取失败时仍须保留 SMTC 兜底。");
            Assert.IsFalse(registry.ResolveSnapshot(independent.Key, session, snapshots, new(true, ["chrome"]))!.IsConnected);
            Assert.AreEqual(1, registry.BuildCandidates([browser], snapshots).Count);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void SourceOwnedLyricsSkipDuplicateSmtcRequestAndRetainFallback()
    {
        StaTest.Run(dispatcher =>
        {
            var original = SettingsManager.Current;
            SettingsManager.Current = new AppSettings();
            try
            {
                var lyrics = new CountingLyricsProvider();
                var service = new LyricsService(lyrics);
                using var kuGou = new KuGouMediaProvider(service, dispatcher);
                using var netEase = new NetEaseMediaProvider(service, dispatcher,
                    () => throw new InvalidOperationException("测试不得读取进程。"));
                var registry = new MediaSourceRegistry([kuGou, netEase]);
                using var builder = new MediaSnapshotBuilder(service, TimeProvider.System, sources: registry);
                builder.GetLyrics("session", "KuGou.exe", new("Track", "Artist", "", 200, null));
                builder.GetLyrics("other", "NETEASE", new("Other", "Artist", "", 200, null));
                Assert.AreEqual(0, lyrics.Calls, "两个独立来源均由提供器取词，SMTC 不应重复发起请求。");
                builder.RequestOnlineLyrics("session", "KuGou.exe", "Track", "Artist", 200, true);
                Assert.AreEqual(1, lyrics.Calls, "提供器不可用时仍能显式走原有兜底。");
            }
            finally { SettingsManager.Current = original; }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void RestartWaitsForReaderDisposalAndDropsCancelledRead()
    {
        StaTest.Run(async dispatcher =>
        {
            var original = SettingsManager.Current;
            SettingsManager.Current = new AppSettings();
            using var oldReader = new BlockingReader();
            using var release = oldReader.Release;
            var freshReader = new FixedReader();
            var created = 0;
            var uiThread = Environment.CurrentManagedThreadId;
            using var provider = new KuGouMediaProvider(new LyricsService(), dispatcher, () =>
            {
                if (Interlocked.Increment(ref created) == 1) return oldReader;
                Assert.IsTrue(oldReader.IsDisposed, "旧读取器未释放前不得创建下一轮读取器。");
                return freshReader;
            });
            var received = new TaskCompletionSource<MediaSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var publishedOld = false;
            provider.SnapshotChanged += (_, snapshot) =>
            {
                publishedOld |= snapshot?.Title == "Old";
                if (snapshot?.Title == "Fresh") received.TrySetResult(snapshot);
            };
            try
            {
                provider.Start();
                await oldReader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                provider.Prune(MemoryPruneLevel.DisplayOff);
                provider.Prune(MemoryPruneLevel.None);
                oldReader.Release.Set();
                var snapshot = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsFalse(publishedOld, "取消期间晚到的读取不能重新发布。");
                Assert.AreEqual(5d, snapshot.Position);
                provider.Dispose();
                provider.Dispose();
                await provider.PollCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsTrue(freshReader.IsDisposed);
                Assert.AreNotEqual(uiThread, oldReader.DisposeThread, "进程读取器必须在后台释放。");
            }
            finally
            {
                oldReader.Release.Set();
                provider.Dispose();
                await provider.PollCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                SettingsManager.Current = original;
            }
        });
    }

    private sealed class CountingLyricsProvider : ILyricsProvider
    {
        public string SourceName => "QQMusic";
        public int Calls { get; private set; }
        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<LyricsResult?>(null);
        }
    }

    private sealed class BlockingReader : IKuGouPlaybackReader
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        internal bool IsDisposed;
        internal int DisposeThread;
        public KuGouPlaybackInfo? Read(MediaSnapshot session)
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("读取未释放。");
            return new("Old", "Artist", 20, 200, true);
        }
        public void Dispose()
        {
            IsDisposed = true;
            DisposeThread = Environment.CurrentManagedThreadId;
        }
    }

    private sealed class FixedReader : IKuGouPlaybackReader
    {
        internal bool IsDisposed;
        public KuGouPlaybackInfo Read(MediaSnapshot session) => new("Fresh", "Artist", 5, 200, true);
        public void Dispose() => IsDisposed = true;
    }
}
