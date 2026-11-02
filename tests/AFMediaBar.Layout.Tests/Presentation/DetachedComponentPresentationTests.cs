// 在独立进程中创建 WPF Application，隔离真实 HWND 对其他纯布局测试的 DPI 环境影响。
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>独立组件跨窗口重挂后的真实绑定、身份与输入恢复检查。</summary>
[TestClass]
[DoNotParallelize]
public sealed class DetachedComponentPresentationTests
{
    [TestMethod]
    public void ReparentedComponentsKeepBindingsAndRestoreInput()
    {
        const string IsolationVariable = "AFMB_DETACHED_PRESENTATION_ISOLATED";
        if (Environment.GetEnvironmentVariable(IsolationVariable) != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(DetachedComponentPresentationTests).Assembly.Location);
            start.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={GetType().FullName}.{nameof(ReparentedComponentsKeepBindingsAndRestoreInput)}");
            start.Environment[IsolationVariable] = "1";
            start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                Assert.Fail("Isolated detached-component presentation test timed out.");
            }
            Assert.AreEqual(0, child.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }

        StaTest.Run(_ =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                app.Resources["AfBooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var resource in new[] { "MotionResources", "SettingsAppearanceResources", "SettingsDiagrams", "SettingsComponentResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/AFMediaBar;component/Resources/{resource}.xaml", UriKind.Relative) });
                DetachedComponentPresentationChecks.Verify();
            }
            finally { app.Shutdown(); }
            return Task.CompletedTask;
        });
    }
}
