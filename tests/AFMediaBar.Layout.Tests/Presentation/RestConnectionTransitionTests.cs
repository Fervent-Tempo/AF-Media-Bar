// 使用独立 WPF 测试进程验证媒体断开与重连，不启动播放器、音频服务或任务栏宿主。
// 测试拥有临时窗口和 Application；关闭窗口时释放控件计时器，结束后恢复设置。
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止新增静置组件在媒体断开或重连时造成异常和视觉状态残留。</summary>
[TestClass]
[DoNotParallelize]
public sealed class RestConnectionTransitionTests
{
    [TestMethod]
    public void DisconnectAndReconnectHandleHiddenVisibleAndRetainedInputDevice()
    {
        const string IsolationVariable = "AFMB_REST_CONNECTION_ISOLATED";
        if (Environment.GetEnvironmentVariable(IsolationVariable) != "1")
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            startInfo.ArgumentList.Add("vstest");
            startInfo.ArgumentList.Add(typeof(RestConnectionTransitionTests).Assembly.Location);
            startInfo.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(RestConnectionTransitionTests).FullName}.{nameof(DisconnectAndReconnectHandleHiddenVisibleAndRetainedInputDevice)}");
            startInfo.Environment[IsolationVariable] = "1";
            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            using var child = Process.Start(startInfo)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                Assert.Fail("媒体连接过渡回归超时。");
            }
            Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }

        StaTest.Run(async dispatcher =>
        {
            var original = SettingsManager.Current;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Window? window = null;
            try
            {
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative)
                    });
                SettingsManager.Current = new AppSettings();
                var control = new TaskBarMediaControl { AllowRestConnectionTransition = false };
                control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);
                control.ApplyPrimaryLength(500);
                window = new Window
                {
                    Content = control,
                    Width = 500,
                    Height = 44,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None
                };
                window.Show();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(control.IsLoaded);
                var input = (FrameworkElement)control.FindName("TaskbarInputDeviceHoverSurface");
                var connected = MediaSnapshot.Disconnected with
                {
                    IsConnected = true,
                    IsPlaying = true,
                    Title = "Connection test",
                    Artist = "Artist"
                };
                // 不假定 CI 的桌面偏好；两种宿主开关都验证，系统关闭动画时还要验证即时落地。
                foreach (var allowTransitions in new[] { false, true })
                {
                    foreach (var (enabled, retained) in new[] { (false, false), (true, false), (true, true) })
                    {
                        control.AllowRestConnectionTransition = false;
                        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                        {
                            InputDeviceVisible = enabled,
                            IdleComponents = retained
                                ? [TaskbarRestComponent.Artwork, TaskbarRestComponent.InputDevice]
                                : [TaskbarRestComponent.Artwork]
                        };
                        control.UpdateSongInfo(connected);
                        control.UpdateLayout();
                        Assert.AreEqual(enabled ? Visibility.Visible : Visibility.Collapsed, input.Visibility);
                        control.AllowRestConnectionTransition = allowTransitions;
                        var expectTransition = allowTransitions && MotionPolicy.ResolveCurrent().UseTransitions;

                        await ChangeConnectionAsync(control, MediaSnapshot.Disconnected, expectTransition);
                        Assert.AreEqual(retained ? Visibility.Visible : Visibility.Collapsed, input.Visibility);
                        Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)control.FindName("SongInfoStackPanel")).Visibility);
                        await ChangeConnectionAsync(control, connected, expectTransition);
                        Assert.AreEqual(enabled ? Visibility.Visible : Visibility.Collapsed, input.Visibility);
                        Assert.AreEqual(1d, input.Opacity, "连接过渡结束后输入设备透明度必须恢复。");
                        Assert.IsTrue(((FrameworkElement)control.FindName("InteractionSurface")).IsHitTestVisible);
                    }
                }
                await FloatingBarHostChecks.VerifyAsync();
                await TaskbarHostActivationChecks.VerifyAsync();
            }
            finally
            {
                window?.Close();
                SettingsManager.Current = original;
                app.Shutdown();
            }
        }, TimeSpan.FromSeconds(40));
    }

    private static async Task ChangeConnectionAsync(TaskBarMediaControl control, MediaSnapshot snapshot, bool expectTransition)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished(object? sender, EventArgs args) => completion.TrySetResult();
        control.RestTransitionFinished += OnFinished;
        try
        {
            control.UpdateSongInfo(snapshot);
            if (expectTransition)
            {
                Assert.IsTrue(control.IsRestConnectionTransitionActive, "允许动效时必须实际执行连接过渡。");
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
            }
            Assert.IsFalse(control.IsRestConnectionTransitionActive);
        }
        finally
        {
            control.RestTransitionFinished -= OnFinished;
        }
    }
}
