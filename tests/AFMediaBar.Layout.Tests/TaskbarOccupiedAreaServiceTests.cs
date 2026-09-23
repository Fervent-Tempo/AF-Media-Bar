using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
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
    public void CompletedResultsRemainIndependentForTwoTaskbars()
    {
        var probe = new FakeTaskbarOccupiedAreaProbe();
        var service = new TaskbarOccupiedAreaService(probe);
        var firstRect = CreateRect(0, 0, 1000, 48);
        var secondRect = CreateRect(1000, 0, 2200, 48);

        service.GetSafePrimaryRanges((IntPtr)1, firstRect, LayoutOrientation.Horizontal, 1, 20);
        probe.SucceedNext([new TaskbarPrimaryRange(100, 800)]);
        service.GetSafePrimaryRanges((IntPtr)2, secondRect, LayoutOrientation.Horizontal, 1.5, 30);
        probe.SucceedNext([new TaskbarPrimaryRange(200, 900)]);

        var first = service.GetSafePrimaryRanges((IntPtr)1, firstRect, LayoutOrientation.Horizontal, 1, 20);
        var second = service.GetSafePrimaryRanges((IntPtr)2, secondRect, LayoutOrientation.Horizontal, 1.5, 30);
        Assert.AreEqual(new TaskbarPrimaryRange(100, 800), first.Single());
        Assert.AreEqual(new TaskbarPrimaryRange(200, 900), second.Single());
        Assert.AreEqual(2, probe.StartCount);
    }

    [TestMethod]
    public void OnlyShellOwnedOverlaysCanBecomeTaskbarOccupiedAreas()
    {
        Assert.IsTrue(TaskbarOverlayOwnerPolicy.IsShellOwned("explorer"));
        Assert.IsTrue(TaskbarOverlayOwnerPolicy.IsShellOwned("SearchHost.exe"));
        Assert.IsFalse(TaskbarOverlayOwnerPolicy.IsShellOwned("SnippingTool"));
        Assert.IsFalse(TaskbarOverlayOwnerPolicy.IsShellOwned("ShareX.exe"));
        Assert.IsFalse(TaskbarOverlayOwnerPolicy.IsShellOwned(null));
    }

    [TestMethod]
    public void InProgressProbePreventsDuplicatePlatformProbeForTheSameTaskbar()
    {
        var probe = new FakeTaskbarOccupiedAreaProbe();
        var service = new TaskbarOccupiedAreaService(probe);
        var rect = CreateRect(0, 0, 1000, 48);

        service.GetSafePrimaryRanges(
            (IntPtr)1,
            rect,
            LayoutOrientation.Horizontal,
            1,
            20);
        service.GetSafePrimaryRanges(
            (IntPtr)1,
            rect,
            LayoutOrientation.Horizontal,
            1,
            20);

        Assert.AreEqual(1, probe.StartCount);
    }

    [TestMethod]
    public void DifferentTaskbarsProbeIndependently()
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
            CreateRect(1000, 0, 1048, 1200),
            LayoutOrientation.Vertical,
            1.5,
            30);

        Assert.AreEqual(2, probe.StartCount);
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

    [TestMethod]
    public void PublishedRangesRaiseAnImmediateTaskbarSpecificUpdate()
    {
        var probe = new FakeTaskbarOccupiedAreaProbe();
        var service = new TaskbarOccupiedAreaService(probe);
        TaskbarSafeRangesUpdatedEventArgs? updated = null;
        service.SafeRangesUpdated += (_, args) => updated = args;
        var rect = CreateRect(0, 0, 1000, 48);

        service.GetSafePrimaryRanges((IntPtr)7, rect, LayoutOrientation.Horizontal, 1.25, 20);
        probe.SucceedNext([new TaskbarPrimaryRange(320, 900)]);

        Assert.IsNotNull(updated);
        Assert.AreEqual((IntPtr)7, updated.TaskbarHandle);
        Assert.AreEqual(LayoutOrientation.Horizontal, updated.Orientation);
        Assert.AreEqual(new TaskbarPrimaryRange(320, 900), updated.Ranges.Single());
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

    /// <summary>
    /// 盖在任务栏上的外部窗口（Windows 10 的搜索框与任务视图）要计入占用，而浮层与桌面窗口不能计入。
    /// Foreign windows lying over the taskbar (the Windows 10 search box and Task View) count as occupied; flyouts and the desktop
    /// must not.
    /// </summary>
    [TestMethod]
    public void OverlayRangePolicyAcceptsTaskbarBandWindowsAndRejectsFlyouts()
    {
        var taskbar = CreateRect(0, 1392, 2560, 1440);

        // Windows 10 的搜索框：高度与任务栏相当，横向只占一小段。
        // The Windows 10 search box: about as tall as the taskbar and only a short stretch of it.
        Assert.IsTrue(TaskbarOverlayRangePolicy.TryResolve(
            CreateRect(48, 1392, 248, 1440), taskbar, LayoutOrientation.Horizontal, out var searchBox));
        Assert.AreEqual(new TaskbarPrimaryRange(48, 248), searchBox);

        // 搜索宿主窗口比任务栏高得多（展开后的搜索结果会与任务栏同处一个窗口）时不接受：它与"开始菜单打开"在几何上无法区分，
        // 接受它会让空闲区间消失、媒体栏跳到最左边，而在菜单关闭后又跳回来。
        // A search host window far taller than the taskbar (expanded results share the window with the box) is not accepted: it is
        // geometrically indistinguishable from "the Start menu is open", and accepting it would wipe out the free ranges, push the bar
        // to the far left, and bounce it back once the menu closes.
        Assert.IsFalse(TaskbarOverlayRangePolicy.TryResolve(
            CreateRect(48, 900, 248, 1440), taskbar, LayoutOrientation.Horizontal, out _, out var tooTall));
        Assert.AreEqual(TaskbarOverlayRejection.TooTall, tooTall);

        // 比任务栏高一些（6 倍以内）仍然接受：Windows 10 的搜索宿主窗口未必恰好与任务栏等高。
        // Somewhat taller than the taskbar (within six times) is still accepted: the Windows 10 search host is not necessarily exactly
        // as tall as the taskbar.
        Assert.IsTrue(TaskbarOverlayRangePolicy.TryResolve(
            CreateRect(48, 1300, 248, 1440), taskbar, LayoutOrientation.Horizontal, out var tallerSearchBox));
        Assert.AreEqual(new TaskbarPrimaryRange(48, 248), tallerSearchBox);

        // 开始菜单：比任务栏高得多，不是任务栏的一部分。
        // The Start menu: far taller than the taskbar, so not part of it.
        Assert.IsFalse(TaskbarOverlayRangePolicy.TryResolve(
            CreateRect(0, 700, 700, 1440), taskbar, LayoutOrientation.Horizontal, out _));

        // 占掉任务栏大半宽度（全宽浮层）：计入会让空闲区间归零，媒体栏反而跳到最左边。
        // A window taking most of the taskbar's width (a full-width flyout): counting it would leave no free range and push the bar
        // to the far left.
        Assert.IsFalse(TaskbarOverlayRangePolicy.TryResolve(
            CreateRect(100, 1392, 2400, 1440), taskbar, LayoutOrientation.Horizontal, out _, out var tooWide));
        Assert.AreEqual(TaskbarOverlayRejection.TooWide, tooWide);

        // 桌面窗口：尺寸远超任务栏。
        // The desktop window: far larger than the taskbar.
        Assert.IsFalse(TaskbarOverlayRangePolicy.TryResolve(
            CreateRect(0, 0, 2560, 1440), taskbar, LayoutOrientation.Horizontal, out _));

        // 只是擦到任务栏底边（横轴只重叠 6 / 48 像素）。
        // Merely grazing the taskbar's bottom edge: only 6 of the taskbar's 48 pixels overlap on the cross axis.
        Assert.IsFalse(TaskbarOverlayRangePolicy.TryResolve(
            CreateRect(100, 1434, 300, 1500), taskbar, LayoutOrientation.Horizontal, out _));

        // 完全在任务栏外。
        // Entirely outside the taskbar.
        Assert.IsFalse(TaskbarOverlayRangePolicy.TryResolve(
            CreateRect(100, 1200, 300, 1300), taskbar, LayoutOrientation.Horizontal, out _));

        // 竖向任务栏用同一套判断，只是主轴换成纵轴。
        // A vertical taskbar uses the same rules with the primary axis turned.
        var vertical = CreateRect(0, 0, 48, 1440);
        Assert.IsTrue(TaskbarOverlayRangePolicy.TryResolve(
            CreateRect(0, 48, 48, 248), vertical, LayoutOrientation.Vertical, out var verticalRange));
        Assert.AreEqual(new TaskbarPrimaryRange(48, 248), verticalRange);
    }

    /// <summary>
    /// 位置偏好 MUST 在放得下的区间里选：空闲区间里可能有比媒体栏还窄的缝隙，取它会把媒体栏压成细条。
    /// The position preference MUST choose among the ranges that fit: a free range can be narrower than the bar, and picking it would
    /// squash the bar into a sliver.
    /// </summary>
    [TestMethod]
    public void FreeRangeSelectionSkipsRangesTooNarrowForTheBar()
    {
        IReadOnlyList<TaskbarPrimaryRange> ranges =
        [
            new TaskbarPrimaryRange(50, 90),
            new TaskbarPrimaryRange(300, 900),
            new TaskbarPrimaryRange(1000, 1800)
        ];

        Assert.AreEqual(
            new TaskbarPrimaryRange(300, 900),
            TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.Start, requiredPrimaryPixels: 300));
        Assert.AreEqual(
            new TaskbarPrimaryRange(1000, 1800),
            TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.End, requiredPrimaryPixels: 300));
        Assert.AreEqual(
            new TaskbarPrimaryRange(1000, 1800),
            TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.Center, requiredPrimaryPixels: 300));

        // 没有任何区间放得下时退回最长的那个，而不是最左边那条缝。
        // When nothing fits, the longest range wins rather than the leftmost sliver.
        Assert.AreEqual(
            new TaskbarPrimaryRange(1000, 1800),
            TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.Start, requiredPrimaryPixels: 2000));

        // requiredPrimaryPixels 为 0 时不做放得下判断（长度尚未确定时用它问"最多能有多长"）。
        // Zero skips the fitting test, which is what asks "how long may the bar be" before its length is known.
        Assert.AreEqual(
            new TaskbarPrimaryRange(50, 90),
            TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.Start, requiredPrimaryPixels: 0));
        Assert.AreEqual(
            new TaskbarPrimaryRange(1000, 1800),
            TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.End, requiredPrimaryPixels: 0));
        Assert.AreEqual(
            new TaskbarPrimaryRange(1000, 1800),
            TaskbarFreeRangeCalculator.Select(ranges, TaskbarBarPosition.Center, requiredPrimaryPixels: 0));
    }

    [TestMethod]
    public void StableSelectionIsKeptWhileTheLatestSafeRangeStillContainsIt()
    {
        IReadOnlyList<TaskbarPrimaryRange> expandedRanges =
        [
            new TaskbarPrimaryRange(20, 1000),
            new TaskbarPrimaryRange(1200, 1900)
        ];
        var previous = new TaskbarPrimaryRange(360, 1000);

        Assert.IsTrue(TaskbarFreeRangeCalculator.TryKeepSelection(
            expandedRanges,
            previous,
            requiredPrimaryPixels: 500,
            out var kept));
        Assert.AreEqual(previous, kept);
    }

    [TestMethod]
    public void StableSelectionIsRejectedWhenItIsNoLongerSafeOrNoLongerFits()
    {
        IReadOnlyList<TaskbarPrimaryRange> movedRanges = [new TaskbarPrimaryRange(20, 700)];
        var previous = new TaskbarPrimaryRange(360, 1000);

        Assert.IsFalse(TaskbarFreeRangeCalculator.TryKeepSelection(
            movedRanges,
            previous,
            requiredPrimaryPixels: 500,
            out _));
        Assert.IsFalse(TaskbarFreeRangeCalculator.TryKeepSelection(
            [new TaskbarPrimaryRange(20, 1200)],
            previous,
            requiredPrimaryPixels: 800,
            out _));
    }

    /// <summary>
    /// 拖动写回的偏移 MUST 与放置时的加法基准一致：否则媒体栏会与鼠标差出"空闲区间起点 − 边缘留白"，
    /// 自动避让打开、区间起点不在最左边时表现为"拖不动/位置和鼠标不对应"。
    /// The offset written by a drag MUST share the base the placement adds it to: otherwise the bar misses the mouse by
    /// "free range start - edge padding", which with "avoid icons" on and a range that does not start at the left edge is exactly what
    /// makes dragging feel broken.
    /// </summary>
    [TestMethod]
    public void DragOffsetRoundTripsIntoThePlacedPosition()
    {
        // 空闲区间从 625 开始（Windows 11 / 自动避让打开时的实测起点），媒体栏宽 400，鼠标把它的左边拖到 900。
        // The free range starts at 625 (measured on Windows 11 with "avoid icons" on), the bar is 400 wide, and the mouse drags its left
        // edge to 900.
        const int rangeStart = 625;
        const int rangeEnd = 2118;
        const int primarySize = 400;
        const int desired = 900;

        var padding = TaskbarBarPlacementCalculator.ResolveManualPadding(desired, rangeStart, rangeEnd, primarySize);
        var placement = TaskbarBarPlacementCalculator.Calculate(
            primaryLength: 2560,
            primarySize,
            crossLength: 48,
            crossSize: 44,
            preferredRange: new TaskbarPrimaryRange(rangeStart, rangeEnd),
            position: TaskbarBarPosition.Start,
            manualPadding: padding,
            crossAxisOffsetDip: 0,
            dpiScale: 1,
            edgePadding: 4);

        Assert.AreEqual(desired, placement.Primary, "the bar must land exactly where the mouse asked");

        // 拖到区间外时被夹进区间，写入的偏移与放置结果仍然一致（不会越拖越偏）。
        // Dragging outside the range clamps into it, and the stored offset still matches the placed position (no drift).
        var clampedPadding = TaskbarBarPlacementCalculator.ResolveManualPadding(
            desiredPrimary: 2500, rangeStart, rangeEnd, primarySize);
        var clampedPlacement = TaskbarBarPlacementCalculator.Calculate(
            2560, primarySize, 48, 44, new TaskbarPrimaryRange(rangeStart, rangeEnd),
            TaskbarBarPosition.Start, clampedPadding, 0, 1, 4);
        Assert.AreEqual(rangeEnd - primarySize, clampedPlacement.Primary);
        Assert.AreEqual(clampedPlacement.Primary - rangeStart, clampedPadding);
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
