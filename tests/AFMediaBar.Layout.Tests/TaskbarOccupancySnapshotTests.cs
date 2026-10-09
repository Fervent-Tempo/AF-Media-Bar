// 使用可控平台探测回调验证发布状态、超时与缓存覆盖，不调用 Explorer/UIA。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Layout.Tests;

/// <summary>探测快照与失败发布隔离回归。</summary>
[TestClass]
public sealed class TaskbarOccupancySnapshotTests
{
    private sealed class Probe : ITaskbarOccupiedAreaProbe
    {
        public List<(Action<IReadOnlyList<TaskbarPrimaryRange>> Success, Action Failure)> Requests { get; } = [];
        public void Start(nint handle, RECT rect, LayoutOrientation orientation, double dpi, int padding,
            Action<IReadOnlyList<TaskbarPrimaryRange>> succeeded, Action failed) => Requests.Add((succeeded, failed));
    }

    [TestMethod]
    public void PendingSuccessfulEmptyAndFailureHaveDifferentStatuses()
    {
        var clock = DateTime.UtcNow;
        var seconds = 0.0;
        var probe = new Probe();
        var service = new TaskbarOccupiedAreaService(probe, () => clock, () => seconds);
        var rect = new RECT { Right = 2000, Bottom = 48 };
        TaskbarOccupancySnapshot Get() => service.GetSnapshot((nint)1, rect, LayoutOrientation.Horizontal, 1, 20);
        Assert.AreEqual(TaskbarProbeStatus.Pending, Get().Status);
        probe.Requests[0].Success([]);
        var empty = Get();
        Assert.AreEqual(TaskbarProbeStatus.Success, empty.Status);
        Assert.AreEqual(0, empty.Ranges.Count);
        clock = clock.AddMilliseconds(500); seconds = 0.5;
        Get();
        probe.Requests[1].Failure();
        var failure = Get();
        Assert.AreEqual(TaskbarProbeStatus.Failed, failure.Status);
        clock = clock.AddSeconds(3); seconds = 3.5;
        Get();
        probe.Requests[2].Success([new(20, 1900)]);
        var recovered = Get();
        Assert.AreEqual(TaskbarProbeStatus.Success, recovered.Status);
        Assert.AreEqual(failure.PublicationId, recovered.LastFailurePublicationId);
        Assert.AreEqual(3.5, recovered.CompletedSeconds);
    }

    [TestMethod]
    public void ExpiredProbePublishesFailureAndOldGenerationCannotOverrideNewResult()
    {
        var clock = DateTime.UtcNow;
        var probe = new Probe();
        var service = new TaskbarOccupiedAreaService(probe, () => clock);
        var rect = new RECT { Right = 2000, Bottom = 48 };
        TaskbarOccupancySnapshot Get() => service.GetSnapshot((nint)1, rect, LayoutOrientation.Horizontal, 1, 20);
        Get();
        clock = clock.AddSeconds(2);
        Assert.AreEqual(TaskbarProbeStatus.Failed, Get().Status);
        service.InvalidateCache();
        Get();
        probe.Requests[^1].Success([new(100, 1900)]);
        var current = Get();
        probe.Requests[0].Success([]);
        Assert.AreEqual(current, Get());
        Assert.AreEqual(1, current.Generation);
    }
}
