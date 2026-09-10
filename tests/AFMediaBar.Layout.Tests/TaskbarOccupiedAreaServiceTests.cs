using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class TaskbarOccupiedAreaServiceTests
{
    [TestMethod]
    public void MatchingProbeResultIsCachedWithoutStartingAnotherProbe()
    {
        var probe = new FakeTaskbarOccupiedAreaProbe();
        var service = new TaskbarOccupiedAreaService(probe);
        var rect = CreateRect(0, 0, 1000, 48);

        var pending = service.GetSafePrimaryRanges((IntPtr)1, rect, LayoutOrientation.Horizontal, 1, 20);
        probe.SucceedNext([new TaskbarPrimaryRange(100, 800)]);
        var cached = service.GetSafePrimaryRanges((IntPtr)1, rect, LayoutOrientation.Horizontal, 1, 20);

        Assert.AreEqual(0, pending.Count);
        Assert.AreEqual(1, probe.StartCount);
        Assert.AreEqual(1, cached.Count);
        Assert.AreEqual(new TaskbarPrimaryRange(100, 800), cached[0]);
    }

    [TestMethod]
    public void InvalidatedGenerationDoesNotPublishAnOlderProbeResult()
    {
        var probe = new FakeTaskbarOccupiedAreaProbe();
        var service = new TaskbarOccupiedAreaService(probe);
        var rect = CreateRect(0, 0, 1000, 48);

        service.GetSafePrimaryRanges((IntPtr)1, rect, LayoutOrientation.Horizontal, 1, 20);
        service.InvalidateCache();
        probe.SucceedNext([new TaskbarPrimaryRange(100, 800)]);

        var afterInvalidation = service.GetSafePrimaryRanges((IntPtr)1, rect, LayoutOrientation.Horizontal, 1, 20);
        probe.SucceedNext([new TaskbarPrimaryRange(200, 700)]);
        var refreshed = service.GetSafePrimaryRanges((IntPtr)1, rect, LayoutOrientation.Horizontal, 1, 20);

        Assert.AreEqual(0, afterInvalidation.Count);
        Assert.AreEqual(2, probe.StartCount);
        Assert.AreEqual(new TaskbarPrimaryRange(200, 700), refreshed[0]);
    }

    [TestMethod]
    public void DifferentCacheKeyDoesNotExposeUnrelatedRanges()
    {
        var probe = new FakeTaskbarOccupiedAreaProbe();
        var service = new TaskbarOccupiedAreaService(probe);
        var firstRect = CreateRect(0, 0, 1000, 48);
        var secondRect = CreateRect(0, 0, 1200, 48);

        service.GetSafePrimaryRanges((IntPtr)1, firstRect, LayoutOrientation.Horizontal, 1, 20);
        probe.SucceedNext([new TaskbarPrimaryRange(100, 800)]);
        var unrelated = service.GetSafePrimaryRanges((IntPtr)1, secondRect, LayoutOrientation.Horizontal, 1, 20);

        Assert.AreEqual(0, unrelated.Count);
        Assert.AreEqual(2, probe.StartCount);
    }

    [TestMethod]
    public void InProgressProbePreventsConcurrentPlatformProbe()
    {
        var probe = new FakeTaskbarOccupiedAreaProbe();
        var service = new TaskbarOccupiedAreaService(probe);

        service.GetSafePrimaryRanges(
            (IntPtr)1,
            CreateRect(0, 0, 1000, 48),
            LayoutOrientation.Horizontal,
            1,
            20);
        service.GetSafePrimaryRanges(
            (IntPtr)2,
            CreateRect(0, 0, 48, 1000),
            LayoutOrientation.Vertical,
            1.5,
            30);

        Assert.AreEqual(1, probe.StartCount);
    }

    [TestMethod]
    public void FailedProbeAllowsLaterRetry()
    {
        var probe = new FakeTaskbarOccupiedAreaProbe();
        var service = new TaskbarOccupiedAreaService(probe);
        var rect = CreateRect(0, 0, 1000, 48);

        service.GetSafePrimaryRanges((IntPtr)1, rect, LayoutOrientation.Horizontal, 1, 20);
        probe.FailNext();
        service.GetSafePrimaryRanges((IntPtr)1, rect, LayoutOrientation.Horizontal, 1, 20);

        Assert.AreEqual(2, probe.StartCount);
    }

    private static NativeMethods.RECT CreateRect(int left, int top, int right, int bottom)
    {
        return new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
    }

    private sealed class FakeTaskbarOccupiedAreaProbe : ITaskbarOccupiedAreaProbe
    {
        private readonly Queue<(Action<IReadOnlyList<TaskbarPrimaryRange>> Succeeded, Action Failed)> _callbacks = new();

        public int StartCount { get; private set; }

        public void Start(
            IntPtr taskbarHandle,
            NativeMethods.RECT taskbarRect,
            LayoutOrientation orientation,
            double dpiScale,
            int edgePaddingPixels,
            Action<IReadOnlyList<TaskbarPrimaryRange>> succeeded,
            Action failed)
        {
            StartCount++;
            _callbacks.Enqueue((succeeded, failed));
        }

        public void SucceedNext(IReadOnlyList<TaskbarPrimaryRange> ranges)
        {
            _callbacks.Dequeue().Succeeded(ranges);
        }

        public void FailNext()
        {
            _callbacks.Dequeue().Failed();
        }
    }
}
