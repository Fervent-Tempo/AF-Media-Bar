// 在独立进程中验证音频浮窗的 HWND 创建、重复显示和退出边界；测试拥有窗口与 Dispatcher。
// 使用忙碌的空 ViewModel 跳过真实音频读取，不启动托盘、播放器或全局 Hook。
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using AFMediaBar.Classes.Services;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Views.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>托盘音频浮窗显示重入与永久关闭的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AudioFlyoutLifecycleTests
{
    [TestMethod]
    public void ToggleDefersWindowCreationCoalescesReentryAndStopsAfterDisposal()
    {
        const string IsolationVariable = "AFMB_AUDIO_FLYOUT_ISOLATED";
        if (Environment.GetEnvironmentVariable(IsolationVariable) != "1")
        {
            // Application 的关闭状态属于进程；隔离实际 BAML 所需的应用资源，避免影响其他 STA 测试。
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("vstest");
            startInfo.ArgumentList.Add(typeof(AudioFlyoutLifecycleTests).Assembly.Location);
            startInfo.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(AudioFlyoutLifecycleTests).FullName}.{nameof(ToggleDefersWindowCreationCoalescesReentryAndStopsAfterDisposal)}");
            startInfo.Environment[IsolationVariable] = "1";
            using var child = Process.Start(startInfo)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                Assert.Fail("Isolated audio flyout test timed out");
            }
            Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }

        StaTest.Run(async _ =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var icons = new AppIconService();
            using var appearance = new WindowAppearanceService(new NativeWindowBackdropAdapter(), icons);
            AudioControlFlyoutWindow? window = null;
            AudioControlFlyoutWindow? pendingWindow = null;
            try
            {
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative)
                    });

                // RefreshAsync 的忙碌分支同步返回，覆盖原生托盘回调不能依赖刷新必然异步的情况。
                var viewModel = (AudioControlViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AudioControlViewModel));
                viewModel.IsBusy = true;
                window = CreateWindow(viewModel, appearance);
                var sourceCount = 0;
                Task? reentrantToggle = null;
                window.SourceInitialized += (_, _) =>
                {
                    sourceCount++;
                    reentrantToggle = window.ToggleAsync(null);
                };

                var opening = window.ToggleAsync(null);
                Assert.IsFalse(opening.IsCompleted, "窗口创建必须离开当前消息回调。");
                Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
                await window.ToggleAsync(null);
                Assert.IsFalse(window.IsVisible, "重复请求不能抢先创建窗口。");
                await opening;
                Assert.IsNotNull(reentrantToggle);
                await reentrantToggle;
                Assert.IsTrue(window.IsVisible, "创建 HWND 时的重入不能隐藏刚打开的浮窗。");
                var handle = new WindowInteropHelper(window).Handle;
                Assert.AreNotEqual(nint.Zero, handle);
                Assert.AreSame(window, ((HwndSource)PresentationSource.FromVisual(window)).RootVisual);

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    window.Hide();
                    await window.ToggleAsync(null);
                    Assert.IsTrue(window.IsVisible);
                    Assert.AreEqual(handle, new WindowInteropHelper(window).Handle, "隐藏后应复用同一 HWND。");
                }
                Assert.AreEqual(1, sourceCount);
                window.Dispose();
                window.Dispose();
                await window.ToggleAsync(null);
                Assert.IsFalse(window.IsVisible);
                Assert.IsNull(PresentationSource.FromVisual(window));

                pendingWindow = CreateWindow(viewModel, appearance);
                var pendingSources = 0;
                pendingWindow.SourceInitialized += (_, _) => pendingSources++;
                var pending = pendingWindow.ToggleAsync(null);
                pendingWindow.Dispose();
                await pending;
                Assert.AreEqual(0, pendingSources, "退出前排队的请求不能创建 HWND。");
                Assert.IsFalse(pendingWindow.IsVisible);
            }
            finally
            {
                pendingWindow?.Dispose();
                window?.Dispose();
                app.Shutdown();
            }
        });
    }

    private static AudioControlFlyoutWindow CreateWindow(AudioControlViewModel viewModel, WindowAppearanceService appearance) =>
        new(viewModel, appearance)
        {
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            Topmost = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
}
