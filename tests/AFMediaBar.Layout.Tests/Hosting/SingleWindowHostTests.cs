// 在独立进程中用真实 WPF 窗口验证创建和 Show 期间的重入，不启动媒体或音频服务。
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止托盘嵌套回调重复初始化同一个窗口及迟到导航重新激活窗口。</summary>
[TestClass]
public sealed class SingleWindowHostTests
{
    [TestMethod]
    public async Task NestedRequestsKeepOneWindowAndDiscardNavigationAfterClose()
    {
        if (await NativeTestProcess.RunIfNeededAsync(GetType().FullName + "." + nameof(NestedRequestsKeepOneWindowAndDiscardNavigationAfterClose))) return;
        StaTest.Run(async dispatcher =>
        {
            SingleWindowHost<Window>? host = null;
            Window? current = null;
            var created = 0;
            var errors = new List<Exception>();
            var navigation = 0;
            host = new(dispatcher, () =>
            {
                created++;
                host!.RequestOpen(_ => navigation = 1);
                dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                current = new Window { Width = 40, Height = 40, Left = -20000, Top = -20000, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
                current.SourceInitialized += (_, _) =>
                {
                    host.RequestOpen(window => { Assert.IsTrue(window.IsLoaded); navigation = 2; });
                    dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                };
                return current;
            }, errors.Add);
            using (host)
            {
                host.RequestOpen();
                host.RequestOpen();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(1, created);
                Assert.IsTrue(current!.IsVisible);
                Assert.AreEqual(2, navigation, "Initialization must retain the latest navigation request.");
                Assert.AreEqual(0, errors.Count);
                current.WindowState = WindowState.Minimized;
                host.RequestOpen();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(WindowState.Normal, current.WindowState);
                host.RequestOpen(_ => navigation = 3);
                // UI 暂不泵消息，让后台生产者先投递请求，随后关闭窗口。
                var trayRequest = new Thread(() => host.RequestOpen(_ => navigation = 3));
                trayRequest.Start();
                Assert.IsTrue(trayRequest.Join(TimeSpan.FromSeconds(2)));
                current.Close();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(1, created);
                Assert.AreEqual(2, navigation);
                host.RequestOpen();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(2, created);
                host.RequestOpen(_ => navigation = 4);
                host.Dispose();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(current.IsVisible);
                Assert.AreEqual(2, navigation);
            }
            var attempts = 0;
            using var retry = new SingleWindowHost<Window>(dispatcher, () =>
            {
                if (++attempts == 1) throw new InvalidOperationException("controlled factory failure");
                return new Window { Width = 40, Height = 40, Left = -20000, Top = -20000, Opacity = 0, ShowInTaskbar = false };
            }, errors.Add);
            retry.RequestOpen();
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            retry.RequestOpen();
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(2, attempts);
            Assert.AreEqual(1, errors.Count);
        });
    }
}
