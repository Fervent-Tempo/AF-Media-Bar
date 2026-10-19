// 使用可控时钟和本地提供器验证三条自动取词路径，防止暂停、缓存过期或剪枝绕过重试限制。
using System.Reflection;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>自动取词的暂停与失败次数限制回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class LyricsAutomaticRetryTests
{
    [DataTestMethod]
    [DataRow("SMTC")]
    [DataRow("OnlineFallback")]
    [DataRow("NetEase")]
    public void FailedRetrievalWaitsForPlaybackAndStopsAfterTenRounds(string path)
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            SettingsManager.Current = new AppSettings { AllowBrowserAndVideoLyrics = true, LyricsSource = LyricsSourceSettings.Default };
            try
            {
                var clock = new TestTimeProvider();
                var source = new FailingProvider();
                var service = new LyricsService(source);
                using var builder = new MediaSnapshotBuilder(service, clock);
                using var netEase = path == "NetEase"
                    ? new NetEaseMediaProvider(service, System.Windows.Threading.Dispatcher.CurrentDispatcher,
                        () => throw new InvalidOperationException("测试不得读取播放器进程。"), clock)
                    : null;
                var publish = typeof(NetEaseMediaProvider).GetMethod("PublishPlayerInfo", BindingFlags.Instance | BindingFlags.NonPublic)!;
                void Fetch(bool playing, string title = "Track")
                {
                    if (netEase is not null)
                    {
                        var info = new PlayerInfo
                        {
                            Identity = title, Title = title, Artists = "Artist", Album = "Album",
                            Cover = "", Url = "", Duration = 180, Schedule = 0, Pause = !playing
                        };
                        publish.Invoke(netEase, [info, DateTimeOffset.UtcNow, CancellationToken.None]);
                        return;
                    }
                    var sourceId = path == "OnlineFallback" ? "cloudmusic" : "Player";
                    var request = new LyricsRequest(title, "Artist", "Album", 180, null);
                    builder.GetLyrics("session", sourceId, request, isPlaying: playing);
                    if (path == "OnlineFallback")
                        builder.RequestOnlineLyrics("session", sourceId, title, "Artist", 180, playing);
                }

                Fetch(false);
                Assert.AreEqual(1, source.Calls, "暂停时仍应首次加载歌词。");
                clock.Advance(TimeSpan.FromSeconds(31));
                Fetch(false);
                Assert.AreEqual(1, source.Calls, "失败缓存到期后，暂停不能触发重试。");
                Fetch(true);
                Assert.AreEqual(2, source.Calls, "恢复播放后允许冷却已结束的重试。");
                Fetch(true);
                Assert.AreEqual(2, source.Calls, "播放中仍须遵守失败冷却。");

                for (var round = 3; round <= 10; round++)
                {
                    clock.Advance(TimeSpan.FromSeconds(31));
                    Fetch(true);
                    Assert.AreEqual(round, source.Calls);
                }
                clock.Advance(TimeSpan.FromDays(1));
                Fetch(false);
                Fetch(true);
                Assert.AreEqual(10, source.Calls, "达到十轮后，时间流逝和暂停切换不能重置次数。");

                builder.Prune(MemoryPruneLevel.DisplayOff);
                netEase?.Prune(MemoryPruneLevel.DisplayOff);
                Fetch(true);
                Assert.AreEqual(10, source.Calls, "内存剪枝不能重置失败次数。");
                Fetch(false, "Other track");
                Assert.AreEqual(11, source.Calls, "另一首歌有独立预算。");
                Fetch(true);
                Assert.AreEqual(11, source.Calls, "切回原曲仍须保留其失败上限。");

                SettingsManager.Current.LyricsSource = LyricsSourceSettings.Default with { Rule = LyricsSourceRule.Manual };
                Fetch(false);
                Assert.AreEqual(12, source.Calls, "更改取词设置后，暂停时也允许按新设置首次加载。");
            }
            finally
            {
                SettingsManager.Current = previous;
            }
            return Task.CompletedTask;
        });
    }

    private sealed class FailingProvider : ILyricsProvider
    {
        public string SourceName => "QQMusic";
        public int Calls { get; private set; }

        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<LyricsResult?>(null);
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }
}
