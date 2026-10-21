// 使用测试自有 HWND 验证 UIA 不将媒体栏自身当作任务栏占用；窗口和探测器由测试释放。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TaskbarProbeSelfExclusionTests
{
    [TestMethod]
    public async Task OwnInteractiveDescendantsDoNotConsumeVerticalTaskbarSpace()
    {
        if (await NativeTestProcess.RunIfNeededAsync(typeof(TaskbarProbeSelfExclusionTests).FullName + "." +
            nameof(OwnInteractiveDescendantsDoNotConsumeVerticalTaskbarSpace))) return;
        StaTest.Run(async dispatcher =>
        {
            var button = new Button { Width = 36, Height = 120, Content = "Own media control" };
            var window = new Window
            {
                Width = 48,
                Height = 600,
                Left = 20,
                Top = 20,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Content = button
            };
            using var probe = new TaskbarOccupiedAreaProbe();
            try
            {
                window.Show();
                await dispatcher.InvokeAsync(() => window.UpdateLayout(),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
                var handle = new WindowInteropHelper(window).Handle;
                Assert.IsTrue(GetWindowRect(handle, out var rect));
                var result = new TaskCompletionSource<IReadOnlyList<TaskbarPrimaryRange>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                probe.Start(handle, rect, LayoutOrientation.Vertical, 1, 0,
                    ranges => result.TrySetResult(ranges), () => result.TrySetException(new Exception("Probe failed.")));
                var ranges = await result.Task.WaitAsync(TimeSpan.FromSeconds(10));
                CollectionAssert.AreEqual(new[] { new TaskbarPrimaryRange(0, rect.Bottom - rect.Top) }, ranges.ToArray(),
                    "Showing the bar's own buttons must not subtract the bar from its next safe range.");
            }
            finally { window.Close(); }
        });
    }
}
