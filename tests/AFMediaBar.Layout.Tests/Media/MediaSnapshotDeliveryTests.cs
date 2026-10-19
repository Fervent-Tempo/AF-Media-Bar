// 验证来源状态先于补全刷新发布，以及 UI 阻塞期间媒体事件不会无限排队；不启动 SMTC 或真实播放器。
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MediaSnapshotDeliveryTests
{
    [TestMethod]
    public void FilterRunsBeforePreferredApplicationSelection()
    {
        StaTest.Run(dispatcher =>
        {
            var original = SettingsManager.Current;
            try
            {
                SettingsManager.Current = new AppSettings
                {
                    SmtcSourceFilter = new(true, ["allowed"]),
                    MediaSelection = new(MediaSelectionMode.PreferredApplication, "blocked")
                };
                var service = EmptyService(dispatcher);
                Set(service, "_sources", new MediaSourceRegistry([]));
                Set(service, "_sourceSnapshots", new Dictionary<IMediaSourceProvider, MediaSnapshot?>());
                Set(service, "_smtcCandidates", new MediaSourceCandidate[]
                {
                    new("blocked-session", "blocked", false, "blocked-session"),
                    new("allowed-session", "allowed", true, "allowed-session")
                });
                var candidates = (IReadOnlyList<MediaSourceCandidate>)typeof(MediaSessionService)
                    .GetMethod("BuildCandidates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!;
                using var selection = new MediaSessionSelectionService(() => null, dispatcher);
                Assert.AreEqual(1, candidates.Count);
                Assert.AreEqual("allowed-session", selection.Resolve(candidates, SettingsManager.Current.MediaSelection)?.Key);
            }
            finally { SettingsManager.Current = original; }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void IndependentAndSmtcChannelsShareArrivalAndPreferredIdentity()
    {
        StaTest.Run(dispatcher =>
        {
            using var provider = new SourceProbe();
            var registry = new MediaSourceRegistry([provider]);
            using var selection = new MediaSessionSelectionService(() => null, dispatcher, normalizeSourceId: registry.NormalizeSourceId);
            var snapshots = new Dictionary<IMediaSourceProvider, MediaSnapshot?>();
            var browser = new MediaSourceCandidate("video", "chrome", true, "video");
            var smtc = new MediaSourceCandidate("smtc", "NETEASE", false, "smtc");
            var latest = new MediaSelectionSettings(MediaSelectionMode.LatestSession);
            selection.Resolve(registry.BuildCandidates([browser], snapshots), latest);
            snapshots[provider] = MediaSnapshot.Disconnected with { IsConnected = true, IsPlaying = true, SourceId = "cloudmusic" };
            Assert.AreEqual(NetEaseSourcePolicy.SelectionKey, selection.Resolve(registry.BuildCandidates([browser], snapshots), latest)?.Key);
            var other = new MediaSourceCandidate("other", "other", false, "other");
            Assert.AreEqual("other", selection.Resolve(registry.BuildCandidates([browser, other], snapshots), latest)?.Key);
            Assert.AreEqual("other", selection.Resolve(registry.BuildCandidates([smtc, browser, other], snapshots), latest)?.Key,
                "补充 SMTC 控制通道不能当作新的独立来源抢占。");
            var preferred = new MediaSelectionSettings(MediaSelectionMode.PreferredApplication, "163MUSIC");
            var candidates = registry.BuildCandidates([smtc, browser, other], snapshots);
            Assert.AreEqual(1, candidates.Count(candidate => candidate.SourceId == "cloudmusic"));
            Assert.AreEqual(NetEaseSourcePolicy.SelectionKey, selection.Resolve(candidates, preferred)?.Key);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void LateSnapshotCannotPublishAfterSwitchingAwayAndBackToTheSameKey()
    {
        StaTest.Run(async dispatcher =>
        {
            var original = SettingsManager.Current;
            using var selection = new MediaSessionSelectionService(() => null, dispatcher);
            var pending = new TaskCompletionSource<MediaSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = EmptyService(dispatcher);
            Task? completion = null;
            try
            {
                SettingsManager.Current = new AppSettings();
                var current = MediaSnapshot.Disconnected with
                {
                    IsConnected = true,
                    IsPlaying = true,
                    CanPlayPause = true,
                    SourceId = "player",
                    SourceName = "Player",
                    Title = "fresh"
                };
                selection.Select("session", [new("session", "player", true, "session")]);
                Set(service, "_selection", selection);
                Set(service, "_sources", new MediaSourceRegistry([]));
                Set(service, "_sourceProviders", Array.Empty<IMediaSourceProvider>());
                Set(service, "_sourceSnapshots", new Dictionary<IMediaSourceProvider, MediaSnapshot?>());
                Set(service, "_browserPresentation", new BrowserMissingPresentationState());
                Set(service, "_publishGate", new object());
                Set(service, "_lastSnapshot", current);
                Set(service, "<CurrentSnapshot>k__BackingField", current);
                Set(service, "_selectedSessionKey", "session");
                Set(service, "_controlSelectionRevision", 1);
                var method = typeof(MediaSessionService).GetMethod("BuildAndPublishSnapshotAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                completion = (Task)method.Invoke(service, [pending.Task, "session", 0, 1])!;
                Set(service, "_controlSelectionRevision", 3); // A → B → A，最终键相同但代际不同。
                pending.SetResult(current with { Title = "stale" });
                await completion.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.AreEqual("fresh", service.CurrentSnapshot?.Title);
                var fresh = Task.FromResult<MediaSnapshot?>(current with { Title = "new" });
                await ((Task)method.Invoke(service, [fresh, "session", 0, 3])!).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.AreEqual("new", service.CurrentSnapshot?.Title, "当前代的结果仍须正常发布。");
            }
            finally
            {
                Set(service, "_isDisposed", true);
                try
                {
                    pending.TrySetResult(null);
                    if (completion is not null) await completion.WaitAsync(TimeSpan.FromSeconds(2));
                }
                finally { SettingsManager.Current = original; }
            }
        });
    }

    [TestMethod]
    public void SourcePauseIsPublishedBeforeCandidateRefresh()
    {
        StaTest.Run(dispatcher =>
        {
            using var selection = new MediaSessionSelectionService(() => null, dispatcher);
            using var provider = new SourceProbe();
            var old = MediaSnapshot.Disconnected with
            {
                IsConnected = true,
                IsPlaying = true,
                SourceId = NetEaseSourcePolicy.SourceId,
                Title = "track",
                Artist = "artist",
                Position = 10
            };
            MediaSourceCandidate[] candidates = [new(NetEaseSourcePolicy.SelectionKey, NetEaseSourcePolicy.SourceId, true, null)];
            selection.Select(NetEaseSourcePolicy.SelectionKey, candidates);
            // 只跳过拥有系统资源的组合根初始化，状态解析、候选选择和发布均使用真实实现。
            var service = EmptyService(dispatcher);
            Set(service, "_selection", selection);
            Set(service, "_sources", new MediaSourceRegistry([provider]));
            Set(service, "_sourceProviders", new IMediaSourceProvider[] { provider });
            Set(service, "_sourceSnapshots", new Dictionary<IMediaSourceProvider, MediaSnapshot?> { [provider] = old });
            Set(service, "_sessionSnapshot", old);
            Set(service, "_lastSnapshot", old);
            Set(service, "<CurrentSnapshot>k__BackingField", old);
            Set(service, "_lastSessions", Array.Empty<MediaSession>());
            Set(service, "_smtcCandidates", Array.Empty<MediaSourceCandidate>());
            Set(service, "_candidates", candidates);
            Set(service, "_lastSessionOptions", Array.Empty<MediaSessionOption>());
            Set(service, "_browserPresentation", new BrowserMissingPresentationState());
            Set(service, "_publishGate", new object());
            var observedPause = false;
            service.SessionsChanged += _ => observedPause = service.CurrentSnapshot?.IsPlaying == false;
            Call(service, "OnSourceSnapshotChanged", provider, old with { IsPlaying = false, Position = 86 });
            Assert.IsTrue(observedPause, "候选及补全刷新开始前，最新暂停状态应已发布。");
            Assert.IsFalse(service.CurrentSnapshot!.IsPlaying);
            Assert.AreEqual(86d, service.CurrentSnapshot.Position);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void MediaEventBurstQueuesOneRefreshAndDisposeDropsIt()
    {
        StaTest.Run(async dispatcher =>
        {
            var service = EmptyService(dispatcher);
            var posted = 0;
            DispatcherHookEventHandler hook = (_, e) =>
            {
                if (e.Operation.Priority == DispatcherPriority.Normal)
                    posted++;
            };
            dispatcher.Hooks.OperationPosted += hook;
            try
            {
                for (var index = 0; index < 100; index++)
                    Call(service, index % 2 == 0 ? "ScheduleRefresh" : "ScheduleSessionsRefresh");
                Assert.AreEqual(1, posted, "事件积压应合并成一次最新状态刷新。");
                Set(service, "_isDisposed", true);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.IsNull(service.CurrentSnapshot);
            }
            finally { dispatcher.Hooks.OperationPosted -= hook; }
        });
    }

    private static MediaSessionService EmptyService(Dispatcher dispatcher)
    {
        var service = (MediaSessionService)RuntimeHelpers.GetUninitializedObject(typeof(MediaSessionService));
        Set(service, "_dispatcher", dispatcher);
        Set(service, "_reconcile", new AFMediaBar.Classes.Services.Media.Smtc.MediaSessionReconcileCoordinator(
            null!, null!, null!, dispatcher, () => false, () => false, () => { }));
        return service;
    }

    private static void Set(object target, string name, object? value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void Call(object target, string name, params object?[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);

    private sealed class SourceProbe : IIndependentMediaSourceProvider
    {
        public IMediaSourcePolicy SourcePolicy { get; } = new NetEaseSourcePolicy();
        public event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged { add { } remove { } }
        public bool CanHandle(string sourceId) => SourcePolicy.Matches(sourceId);
        public void UpdateSessionSnapshot(MediaSnapshot snapshot) { }
        public void Start() { }
        public void Dispose() { }
    }
}
