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
    public void UnusedSpectrumDefersBuffersAndFirstCaptureSupportsEveryFftSize()
    {
        WithSettingsDirectory(_ =>
        {
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
            { CaptureSource = SpectrumCaptureSource.AllApplications });
            using var devices = new AudioCaptureDeviceResolver(new MediaSourceProcessResolver());
            using var monitor = new AudioMonitorService(devices, new SpectrumCaptureProcessResolver(new MediaSourceProcessResolver()));
            var bands = new float[9];
            Assert.IsFalse(monitor.GetSpectrum(bands, bands.Length));
            foreach (var field in new[] { "_sampleRing", "_fftReal", "_fftImaginary", "_fftWindow", "_powerSpectrum", "_bandEdges", "_bandMagnitudes" })
                Assert.AreEqual(0, GetField<Array>(monitor, field).Length, "尚未采集时不分配分析缓冲。");

            var configure = typeof(AudioMonitorService).GetMethod("ConfigureFft", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var push = typeof(AudioMonitorService).GetMethod("PushSample", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var sampleRate in new[] { 22050, 48000, 96000 })
            {
                configure.Invoke(monitor, [sampleRate]);
                SetField(monitor, "_sampleRate", sampleRate);
                var fftSize = SpectrumAnalysisPolicy.ResolveFftSize(sampleRate);
                Assert.AreEqual(fftSize, GetField<double[]>(monitor, "_fftReal").Length);
                var ring = GetField<float[]>(monitor, "_sampleRing");
                configure.Invoke(monitor, [sampleRate]);
                Assert.AreSame(ring, GetField<float[]>(monitor, "_sampleRing"), "相同格式应复用已创建缓冲。");

                for (var index = 0; index < fftSize; index++)
                    push.Invoke(monitor, [(float)Math.Sin(2 * Math.PI * 1000 * index / sampleRate)]);
                SetField(monitor, "_lastPacketTick", Environment.TickCount64);
                SetField(monitor, "_activeCaptureVersion", GetField<int>(monitor, "_captureVersion"));
                SetField(monitor, "_captureReady", true);
                Assert.IsTrue(monitor.GetSpectrum(bands, bands.Length));
                Assert.IsTrue(bands.Any(value => value > 0), "延迟初始化和格式切换后仍能分析缓存的正弦样本。");
            }
        });
    }

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
            Assert.IsNull(SettingsManager.Current.SpectrumComponent.CaptureDeviceId);
        });
    }

    [TestMethod]
    [DataRow(null, "", SpectrumCaptureDeviceMode.FollowMediaSource)]
    [DataRow("endpoint-id", "", SpectrumCaptureDeviceMode.SpecificDevice)]
    [DataRow(null, ",\"captureDeviceMode\":null", SpectrumCaptureDeviceMode.FollowMediaSource)]
    [DataRow("endpoint-id", ",\"captureDeviceMode\":null", SpectrumCaptureDeviceMode.SpecificDevice)]
    [DataRow(null, ",\"captureDeviceMode\":999", SpectrumCaptureDeviceMode.FollowMediaSource)]
    [DataRow("endpoint-id", ",\"captureDeviceMode\":999", SpectrumCaptureDeviceMode.SpecificDevice)]
    [DataRow(null, ",\"captureDeviceMode\":\"unknown\"", SpectrumCaptureDeviceMode.FollowMediaSource)]
    [DataRow("endpoint-id", ",\"captureDeviceMode\":\"unknown\"", SpectrumCaptureDeviceMode.SpecificDevice)]
    public void OldOrInvalidDeviceModeKeepsPriorBehaviour(string? deviceId, string field, SpectrumCaptureDeviceMode expected)
    {
        WithSettingsDirectory(directory =>
        {
            var device = deviceId is null ? string.Empty : ",\"captureDeviceId\":\"" + deviceId + "\"";
            File.WriteAllText(Path.Combine(directory, "settings.json"),
                "{\"schemaVersion\":2,\"settings\":{\"spectrumComponent\":{\"bandCount\":12,\"refreshRateHz\":20,\"sensitivityPercent\":100" + device + field + "}}}");
            using var persistence = new SettingsPersistenceService(directory, TimeSpan.FromMinutes(5));
            persistence.Initialize();
            var settings = SettingsManager.Current.SpectrumComponent;
            Assert.IsNull(settings.CaptureDeviceMode, "缺失、null 或无效策略均保留为未指定。");
            Assert.AreEqual(expected, settings.ResolveCaptureDeviceMode(), "未指定策略时按 CaptureDeviceId 保留原来的设备选择。");
            Assert.AreEqual(deviceId, settings.CaptureDeviceId, "旧配置保存的设备标识原样保留。");
        });
    }

    [TestMethod]
    [DataRow(SpectrumCaptureDeviceMode.FollowMediaSource)]
    [DataRow(SpectrumCaptureDeviceMode.SystemDefault)]
    [DataRow(SpectrumCaptureDeviceMode.SpecificDevice)]
    public void DeviceModeSurvivesPersistenceCloneAndUserDefaultReset(SpectrumCaptureDeviceMode mode)
    {
        WithSettingsDirectory(directory =>
        {
            using (var persistence = new SettingsPersistenceService(directory, TimeSpan.FromMinutes(5)))
            {
                persistence.Initialize();
                SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
                { CaptureDeviceMode = mode, CaptureDeviceId = " endpoint-id ", CaptureDeviceName = "Headphones" });
                persistence.Flush();
                using var document = JsonDocument.Parse(File.ReadAllText(persistence.SettingsPath));
                // 序列化用成员名而不是序号，改名会让旧文件读到回退值。
                Assert.AreEqual(mode.ToString(), document.RootElement.GetProperty("settings").GetProperty("spectrumComponent")
                    .GetProperty("captureDeviceMode").GetString());
            }
            SettingsManager.Current = new AppSettings();
            using var reloaded = new SettingsPersistenceService(directory, TimeSpan.FromMinutes(5));
            reloaded.Initialize();
            Assert.AreEqual(mode, SettingsManager.Current.SpectrumComponent.CaptureDeviceMode);
            Assert.AreEqual(mode, SettingsManager.Current.SpectrumComponent.ResolveCaptureDeviceMode());
            Assert.AreEqual("endpoint-id", SettingsManager.Current.SpectrumComponent.CaptureDeviceId);
            Assert.AreEqual("Headphones", SettingsManager.Current.SpectrumComponent.CaptureDeviceName);
            var saved = SettingsManager.Current.Clone();
            SettingsManager.SetUserDefaults(saved);
            SettingsManager.ResetComponents();
            Assert.AreEqual(mode, SettingsManager.Current.SpectrumComponent.CaptureDeviceMode, "恢复默认后策略与设备标识一起回到用户快照。");
            Assert.AreEqual("endpoint-id", SettingsManager.Current.SpectrumComponent.CaptureDeviceId);
            Assert.AreEqual("Headphones", SettingsManager.Current.SpectrumComponent.CaptureDeviceName);
        });
    }

    [TestMethod]
    [DataRow((SpectrumCaptureDeviceMode)999)]
    public void InvalidDeviceModeFallsBackToLegacyResolution(SpectrumCaptureDeviceMode mode)
    {
        var normalized = (SpectrumComponentSettings.Default with { CaptureDeviceMode = mode, CaptureDeviceId = "endpoint-id" }).Normalize();
        Assert.IsNull(normalized.CaptureDeviceMode, "无法识别的策略值按「没有表达过」处理。");
        Assert.AreEqual(SpectrumCaptureDeviceMode.SpecificDevice, normalized.ResolveCaptureDeviceMode());
    }

    [TestMethod]
    public void SwitchingDeviceModeRebuildsCaptureEvenWhenDeviceIdIsUnchanged()
    {
        WithSettingsDirectory(_ =>
        {
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
            { CaptureSource = SpectrumCaptureSource.CurrentDevice, CaptureDeviceMode = SpectrumCaptureDeviceMode.FollowMediaSource });
            using var devices = new AudioCaptureDeviceResolver(new MediaSourceProcessResolver());
            using var monitor = new AudioMonitorService(devices, new SpectrumCaptureProcessResolver(new MediaSourceProcessResolver()));
            var version = GetField<int>(monitor, "_captureVersion");
            SetField(monitor, "_captureReady", true);
            Assert.IsTrue(monitor.IsCaptureCurrent(version), "初始代际可用。");

            // 跟随媒体来源 → 系统默认输出设备：设备标识没变，也必须重建采集。
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
            { CaptureSource = SpectrumCaptureSource.CurrentDevice, CaptureDeviceMode = SpectrumCaptureDeviceMode.SystemDefault });
            Assert.IsFalse(monitor.IsCaptureCurrent(version), "切到系统默认输出设备必须重建采集。");

            // 指定设备 → 系统默认输出设备（保留旧设备标识）：同样必须重建。
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
            { CaptureSource = SpectrumCaptureSource.CurrentDevice, CaptureDeviceMode = SpectrumCaptureDeviceMode.SpecificDevice, CaptureDeviceId = "endpoint-id" });
            version = GetField<int>(monitor, "_captureVersion");
            SetField(monitor, "_captureReady", true);
            Assert.IsTrue(monitor.IsCaptureCurrent(version));
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
            { CaptureSource = SpectrumCaptureSource.CurrentDevice, CaptureDeviceMode = SpectrumCaptureDeviceMode.SystemDefault, CaptureDeviceId = "endpoint-id" });
            Assert.IsFalse(monitor.IsCaptureCurrent(version), "仅策略变化也要重建采集。");

            // 策略与设备都没变：不重建。
            version = GetField<int>(monitor, "_captureVersion");
            SetField(monitor, "_captureReady", true);
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
            { CaptureSource = SpectrumCaptureSource.CurrentDevice, CaptureDeviceMode = SpectrumCaptureDeviceMode.SystemDefault, CaptureDeviceId = "endpoint-id" });
            Assert.IsTrue(monitor.IsCaptureCurrent(version), "策略与设备均未变化时不重建采集。");
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
                SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with
                { CaptureSource = source, CaptureDeviceId = " endpoint-id ", CaptureDeviceName = "Headphones" });
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
            Assert.AreEqual("endpoint-id", SettingsManager.Current.SpectrumComponent.CaptureDeviceId);
            Assert.AreEqual("Headphones", SettingsManager.Current.SpectrumComponent.CaptureDeviceName);
            var saved = SettingsManager.Current.Clone();
            SettingsManager.SetUserDefaults(saved);
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with { CaptureSource = (SpectrumCaptureSource)999 });
            SettingsManager.ResetComponents();
            Assert.AreEqual(source, SettingsManager.Current.SpectrumComponent.CaptureSource);
            Assert.AreEqual("endpoint-id", SettingsManager.Current.SpectrumComponent.CaptureDeviceId);
            Assert.AreEqual("Headphones", SettingsManager.Current.SpectrumComponent.CaptureDeviceName);
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
            using var devices = new AudioCaptureDeviceResolver(new MediaSourceProcessResolver());
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
            // 固定端点不因媒体切换重建，修改端点后拒绝旧帧；自动模式随媒体身份失效。
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with { CaptureDeviceId = "fixed" });
            version = GetField<int>(monitor, "_captureVersion");
            SetField(monitor, "_captureReady", true);
            monitor.UpdateMediaSource(snapshot);
            Assert.IsTrue(monitor.IsCaptureCurrent(version));
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default with { CaptureDeviceId = "other" });
            Assert.IsFalse(monitor.IsCaptureCurrent(version));
            SettingsManager.SetSpectrumComponentSettings(SpectrumComponentSettings.Default);
            version = GetField<int>(monitor, "_captureVersion");
            SetField(monitor, "_captureReady", true);
            monitor.UpdateMediaSource(snapshot with { SourceId = "Spotify", SourceName = "Spotify" });
            Assert.IsFalse(monitor.IsCaptureCurrent(version));
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
            using var devices = new AudioCaptureDeviceResolver(new MediaSourceProcessResolver());
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
