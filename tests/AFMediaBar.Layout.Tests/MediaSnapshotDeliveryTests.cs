// 验证来源状态先于补全刷新发布，以及 UI 阻塞期间媒体事件不会无限排队；不启动 SMTC 或真实播放器。
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class MediaSnapshotDeliveryTests
{
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
