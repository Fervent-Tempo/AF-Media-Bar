// 在既有隔离 WPF 测试中检查通知租约与音频边界，不启动采集或播放。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class PetExperienceChecks
{
    internal static void Verify()
    {
        var previous = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new AppSettings { PetComponent = new() { MusicMotionEnabled = false, NotificationsEnabled = true } };
            using var devices = new AudioCaptureDeviceResolver(new MediaSourceProcessResolver());
            using var audio = new AudioMonitorService(devices, new SpectrumCaptureProcessResolver(new MediaSourceProcessResolver()));
            using var voice = new PetVoiceService();
            var display = new Display();
            using var pet = new PetCompanionService(audio, display, voice);
            Assert.IsFalse(pet.TryNotify("title", "body"), "无可见宿主必须回退。");
            string? animation = null;
            var accept = false;
            Action? activate = null;
            var lease = pet.Subscribe((state, _) => animation = state, (_, action) => { activate = action; return accept; });
            Assert.IsFalse(pet.TryNotify("title", "body"), "宿主忙碌必须回退。");
            accept = true;
            var detailsOpened = false;
            Assert.IsTrue(pet.TryNotify("title", "body", () => detailsOpened = true));
            activate?.Invoke();
            Assert.IsTrue(detailsOpened, "接管通知必须保留详情入口。");
            Assert.AreEqual("notifying", animation);
            display.Fullscreen = true;
            Assert.IsFalse(pet.TryNotify("title", "body"));
            display.Fullscreen = false;
            SettingsManager.Current.PetComponent = SettingsManager.Current.PetComponent with { NotificationsEnabled = false };
            Assert.IsFalse(pet.TryNotify("title", "body"));
            lease.Dispose();
            lease.Dispose();
            SettingsManager.Current.PetComponent = SettingsManager.Current.PetComponent with { NotificationsEnabled = true };
            Assert.IsFalse(pet.TryNotify("title", "body"), "释放后不能吞掉原通知。");
            pet.Dispose();
            pet.UpdateSnapshot(MediaSnapshot.Disconnected);
            Assert.IsFalse(pet.TryNotify("title", "body"));
            var pcm = PetVoiceService.CreatePcm(100, "你好，AF！");
            Assert.AreEqual(48640, pcm.Length, "四个音节与标点停顿决定时长。");
            Assert.IsFalse(pcm.SequenceEqual(PetVoiceService.CreatePcm(100, "世界，AF！")));
            Assert.IsTrue(pcm.Any(value => value != 0));
            Assert.IsTrue(PetVoiceService.CreatePcm(0, "你好").All(value => value == 0));
            for (var i = 0; i < pcm.Length; i += 2)
                Assert.IsTrue(Math.Abs((int)BitConverter.ToInt16(pcm, i)) <= short.MaxValue * 0.36, "合成音必须保留音量余量。");
        }
        finally { SettingsManager.Current = previous; }
    }

    private sealed class Display : IDisplayMonitorService
    {
        public bool Fullscreen;
        public event EventHandler? MonitorsChanged { add { } remove { } }
        public IReadOnlyList<DisplayMonitorInfo> GetMonitors() => [];
        public void Refresh() { }
        public DisplayMonitorInfo? ResolveFixedMonitor(string? deviceId) => null;
        public DisplayMonitorInfo? ResolveNotificationMonitor(NotificationTargetMode mode, string? fixedDeviceId) => null;
        public bool IsForegroundWindowFullscreen() => Fullscreen;
    }
}
