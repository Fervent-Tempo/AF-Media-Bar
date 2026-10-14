// 在独立 WPF 进程中实际加载设置页，防止只读计数等绑定导致页面初始化失败。
using System.Diagnostics;
using System.Windows;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.ViewModels.Pages;
using AFMediaBar.Views.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>设备分组设置页的真实 XAML 加载回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AudioSettingsPageLoadingTests
{
    [TestMethod]
    public void LoadsDeviceGroupsWithReadOnlyCounts()
    {
        const string isolation = "AFMB_AUDIO_SETTINGS_LOADING_ISOLATED";
        if (Environment.GetEnvironmentVariable(isolation) != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(AudioSettingsPageLoadingTests).Assembly.Location);
            start.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(AudioSettingsPageLoadingTests).FullName}.{nameof(LoadsDeviceGroupsWithReadOnlyCounts)}");
            start.Environment[isolation] = "1";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(true); Assert.Fail("设置页加载超时。"); }
            Assert.AreEqual(0, process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            return;
        }
        StaTest.Run(async _ =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                foreach (var name in new[] { "MotionResources", "SettingsAppearanceResources", "SettingsDiagrams", "SettingsComponentResources" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/AFMediaBar;component/Resources/{name}.xaml", UriKind.Relative) });
                using var localization = new LocalizationService();
                using var context = new SettingsPageContext();
                using var viewModel = new InteractionViewModel(localization, new LegacySettingsConfiguration(context), new Catalog());
                await viewModel.AudioDevices.RefreshAsync();
                var page = new InteractionPage(viewModel);
                page.Measure(new Size(820, 900));
                Assert.AreEqual(1, viewModel.AudioDevices.OtherOutputDevices.Count);
                Assert.IsNotNull(page.Content);
            }
            finally { application.Shutdown(); }
        });
    }

    private sealed class Catalog : IAudioDeviceCatalog
    {
        public event EventHandler? Changed { add { } remove { } }
        public Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, bool includeHidden = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AudioDeviceOption>>(direction == AudioDeviceDirection.Output ?
                [new("old", "old", "Old", false) { IsActive = false, Availability = AudioDeviceAvailability.Disabled }] : []);
    }
}
