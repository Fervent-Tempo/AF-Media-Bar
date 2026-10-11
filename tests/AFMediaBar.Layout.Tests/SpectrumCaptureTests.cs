// 验证采集范围的旧设置回退、进程树选择和切源后的旧样本失效；不启动真实音频采集。
using System.Reflection;
using System.Text.Json;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>防止设置丢失、捕获无关进程，以及媒体栏切源后继续使用旧音频。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SpectrumCaptureTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow(", \"captureSource\": null")]
    [DataRow(", \"captureSource\": \"unknown\"")]
    [DataRow(", \"captureSource\": 999")]
    public void OldOrInvalidCaptureSourceKeepsDeviceScope(string field)
    {
        WithSettingsDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"),
                "{\"schemaVersion\":2,\"settings\":{\"spectrumComponent\":{\"bandCount\":12,\"refreshRateHz\":20,\"sensitivityPercent\":100" + field + "}}}");
            using var persistence = new SettingsPersistenceService(directory, TimeSpan.FromMinutes(5));
            persistence.Initialize();
            Assert.AreEqual(SpectrumCaptureSource.CurrentDevice, SettingsManager.Current.SpectrumComponent.CaptureSource);
            Assert.AreEqual(12, SettingsManager.Current.SpectrumComponent.BandCount);
        });
    }

    [TestMethod]
    [DataRow(SpectrumCaptureSource.CurrentDevice)]
    [DataRow(SpectrumCaptureSource.AllApplications)]
    [DataRow(SpectrumCaptureSource.CurrentMediaApplication)]
    public void CaptureSourceSurvivesPersistenceCloneAndUserDefaultReset(SpectrumCaptureSource source)
    {
        WithSettingsDirectory(directory =>
        {
            using (var persistence = new SettingsPersistenceService(directory, TimeSpan.FromMinutes(5)))
            {
                persistence.Initialize();
                SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with { CaptureSource = source });
                persistence.Flush();
                using var document = JsonDocument.Parse(File.ReadAllText(persistence.SettingsPath));
                Assert.AreEqual(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.AreEqual(source.ToString(), document.RootElement.GetProperty("settings").GetProperty("spectrumComponent")
                    .GetProperty("captureSource").GetString());
            }
            SettingsManager.Current = new AppSettings();
            using var reloaded = new SettingsPersistenceService(directory, TimeSpan.FromMinutes(5));
            reloaded.Initialize();
            Assert.AreEqual(source, SettingsManager.Current.SpectrumComponent.CaptureSource);
            var saved = SettingsManager.Current.Clone();
            SettingsManager.SetUserDefaults(saved);
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with { CaptureSource = (SpectrumCaptureSource)999 });
            SettingsManager.ResetComponents();
            Assert.AreEqual(source, SettingsManager.Current.SpectrumComponent.CaptureSource);
        });
    }

    [TestMethod]
    public void ProcessSelectionKeepsAudibleTargetAndDropsExitedOrSilentTarget()
    {
        Assert.AreEqual((uint)10, SpectrumCaptureProcessResolver.SelectProcess(new Dictionary<uint, float>
        { [10] = 0.01f, [20] = 0.5f }, 10));
        Assert.AreEqual((uint)20, SpectrumCaptureProcessResolver.SelectProcess(new Dictionary<uint, float>
        { [10] = 0, [20] = 0.5f }, 10));
        Assert.AreEqual((uint)20, SpectrumCaptureProcessResolver.SelectProcess(new Dictionary<uint, float>
        { [20] = 0 }, 10));
        Assert.IsNull(SpectrumCaptureProcessResolver.SelectProcess(new Dictionary<uint, float>(), 10));
    }

    [TestMethod]
    public void BrowserAudioChildResolvesToApplicationRootWithoutCapturingLauncher()
    {
        var processes = new Dictionary<uint, SpectrumCaptureProcessResolver.ProcessIdentity>
        {
            [1] = new(1, 0, "AFMediaBar"),
            [2] = new(2, 1, "chrome"),
            [3] = new(3, 2, "chrome"),
            [4] = new(4, 3, "chrome")
        };
        Assert.AreEqual((uint)2, SpectrumCaptureProcessResolver.ResolveRoot(4, processes, name => name == "chrome"));
        processes[1] = new(1, 0, string.Empty);
        Assert.AreEqual((uint)2, SpectrumCaptureProcessResolver.ResolveRoot(4, processes, _ => true));
    }

    [TestMethod]
    public void DisconnectedOrUnidentifiedMediaHasNoCaptureProcess()
    {
        var resolver = new SpectrumCaptureProcessResolver(new MediaSourceProcessResolver());
        Assert.IsNull(resolver.ResolveProcessId(MediaSnapshot.Disconnected, 10, CancellationToken.None));
        Assert.IsNull(resolver.ResolveProcessId(MediaSnapshot.Disconnected with { IsConnected = true }, 10, CancellationToken.None));
    }

    [TestMethod]
    public void SwitchingMediaInvalidatesSamplesWhileMetadataRefreshKeepsCapture()
    {
        WithSettingsDirectory(_ =>
        {
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
            { CaptureSource = SpectrumCaptureSource.CurrentMediaApplication });
            using var devices = new AudioCaptureDeviceResolver();
            using var monitor = new AudioMonitorService(devices, new SpectrumCaptureProcessResolver(new MediaSourceProcessResolver()));
            var snapshot = MediaSnapshot.Disconnected with { IsConnected = true, SourceId = "chrome", SourceName = "Chrome" };
            monitor.UpdateMediaSource(snapshot);
            var version = GetField<int>(monitor, "_captureVersion");
            SetField(monitor, "_activeCaptureVersion", version);
            SetField(monitor, "_captureReady", true);
            var bands = new float[9];
            Assert.IsTrue(monitor.GetSpectrum(bands, bands.Length));
            monitor.UpdateMediaSource(snapshot with { Title = "Updated", Position = 30 });
            Assert.IsTrue(monitor.GetSpectrum(bands, bands.Length));
            monitor.UpdateMediaSource(snapshot with { SourceId = "Spotify", SourceName = "Spotify" });
            // 即使旧回调随后把 ready 标志写回 true，旧代际也不能重新发布样本。
            SetField(monitor, "_captureReady", true);
            Assert.IsFalse(monitor.GetSpectrum(bands, bands.Length));
            Assert.IsFalse(monitor.IsCaptureCurrent(version));
            SetField(monitor, "_activeCaptureVersion", GetField<int>(monitor, "_captureVersion"));
            Assert.IsTrue(monitor.GetSpectrum(bands, bands.Length));
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
            {
                CaptureSource = SpectrumCaptureSource.AllApplications
            });
            Assert.IsFalse(monitor.GetSpectrum(bands, bands.Length));
            monitor.Dispose();
            monitor.UpdateMediaSource(snapshot);
            Assert.IsFalse(monitor.GetSpectrum(bands, bands.Length));
        });
    }

    [TestMethod]
    public void SourceNotificationDoesNotWaitForSpectrumCalculationGate()
    {
        WithSettingsDirectory(_ =>
        {
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
            {
                CaptureSource = SpectrumCaptureSource.CurrentMediaApplication
            });
            using var devices = new AudioCaptureDeviceResolver();
            using var monitor = new AudioMonitorService(devices, new SpectrumCaptureProcessResolver(new MediaSourceProcessResolver()));
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var holder = Task.Run(() =>
            {
                lock (GetField<object>(monitor, "_gate"))
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(5));
                }
            });
            Task? publish = null;
            try
            {
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                publish = Task.Run(() => monitor.UpdateMediaSource(MediaSnapshot.Disconnected with
                {
                    IsConnected = true,
                    SourceId = "chrome",
                    SourceName = "Chrome"
                }));
                Assert.IsTrue(publish.Wait(TimeSpan.FromSeconds(2)), "来源通知不能同步等待采样或 FFT。");
            }
            finally
            {
                release.Set();
                Assert.IsTrue(holder.Wait(TimeSpan.FromSeconds(5)));
                if (publish is not null) Assert.IsTrue(publish.Wait(TimeSpan.FromSeconds(5)));
            }
        });
    }

    private static T GetField<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void SetField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static void WithSettingsDirectory(Action<string> action)
    {
        var previous = SettingsManager.Current;
        var previousDefaults = SettingsManager.UserDefaults;
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-spectrum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SettingsManager.SetUserDefaults(null);
            SettingsManager.Current = new AppSettings();
            action(directory);
        }
        finally
        {
            SettingsManager.SetUserDefaults(previousDefaults);
            SettingsManager.Current = previous;
            Directory.Delete(directory, true);
        }
    }
}
