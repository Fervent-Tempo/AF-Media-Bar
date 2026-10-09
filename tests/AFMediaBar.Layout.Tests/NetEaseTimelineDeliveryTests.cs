// 验证延后应用和歌词补全不会把旧进度重新标记为当前时间；不访问播放器或网络。
using System.Reflection;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NetEaseTimelineDeliveryTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DelayedReadAndLyricsEnrichmentKeepOriginalTimeline(bool paused)
    {
        StaTest.Run(dispatcher =>
        {
            using var provider = new NetEaseMediaProvider(new LyricsService(), dispatcher,
                () => throw new InvalidOperationException("本测试不得读取真实进程。"));
            var observed = new List<MediaSnapshot>();
            provider.SnapshotChanged += (_, snapshot) =>
            {
                if (snapshot is not null)
                    observed.Add(snapshot);
            };
            var readAt = DateTimeOffset.UtcNow.AddSeconds(-30);
            var info = new PlayerInfo
            {
                Identity = "track",
                Title = "title",
                Artists = "artist",
                Album = "album",
                Cover = string.Empty,
                Url = string.Empty,
                Schedule = 10,
                Duration = 200,
                Pause = paused
            };
            typeof(NetEaseMediaProvider).GetMethod("PublishPlayerInfo", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(provider, [info, readAt, CancellationToken.None]);
            Assert.IsTrue(observed.Count >= 2, "应包含读取发布及无网络歌词补全发布。");
            foreach (var snapshot in observed)
            {
                Assert.AreEqual(readAt, snapshot.TimelineUpdatedAt);
                Assert.AreEqual(paused ? 10d : 40d, TaskbarExperiencePolicy.GetPosition(snapshot, readAt.AddSeconds(30)));
            }
            return Task.CompletedTask;
        });
    }
}
