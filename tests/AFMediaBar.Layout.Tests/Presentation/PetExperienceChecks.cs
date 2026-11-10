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
        foreach (var floating in new[] { false, true })
        {
            var dock = floating ? "DockBar" : "Dock";
            var inline = floating ? "InlineBar" : "Inline";
            CollectionAssert.AreEqual(new[] { dock, "Free" }, AFMediaBar.Components.TaskBarMediaControl.PetPlacementChoices(new() { AllowOverflow = false }, floating).Select(c => c.Key).ToArray());
            CollectionAssert.AreEqual(new[] { inline, "Free" }, AFMediaBar.Components.TaskBarMediaControl.PetPlacementChoices(new() { AllowOverflow = true }, floating).Select(c => c.Key).ToArray());
            CollectionAssert.AreEqual(new[] { dock, inline }, AFMediaBar.Components.TaskBarMediaControl.PetPlacementChoices(new() { AllowOverflow = true, FreePlacement = true }, floating).Select(c => c.Key).ToArray());
        }
        var stroke = new AFMediaBar.Components.PetStrokeGesture();
        Assert.IsFalse(stroke.Move(4, 48, 0));
        Assert.IsFalse(stroke.Move(32, 48, 100));
        Assert.IsFalse(stroke.Move(8, 48, 200));
        Assert.IsTrue(stroke.Move(32, 48, 300));
        stroke.Reset();
        Assert.IsFalse(stroke.Move(8, 48, 500), "冷却不受离开重置影响。");
        Assert.IsFalse(stroke.Move(8, 48, 3000));
        Assert.IsFalse(stroke.Move(32, 48, 4000), "超时不能积累摸摸手势。");
        var previous = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new AppSettings { PetComponent = new() { MusicMotionEnabled = false, NotificationsEnabled = true } };
            using var devices = new AudioCaptureDeviceResolver(new MediaSourceProcessResolver());
            using var audio = new AudioMonitorService(devices, new SpectrumCaptureProcessResolver(new MediaSourceProcessResolver()));
            using var voice = new PetVoiceService();
            var display = new Display();
            using var pet = new PetCompanionService(audio, display, voice);
            var clock = new Clock();
            using (var focus = new PetCompanionService(audio, display, voice, clock: clock))
            {
                var reminders = 0;
                var idle = false;
                var focusLease = focus.Subscribe((_, _) => { }, focusReminder: () => { if (!idle) return false; reminders++; return true; });
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => focus.StartFocus(0));
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => focus.StartFocus(121));
                focus.StartFocus(25);
                Assert.AreEqual(TimeSpan.FromMinutes(25), focus.FocusRemaining);
                focus.StartFocus(10);
                Assert.AreEqual(TimeSpan.FromMinutes(25), focus.FocusRemaining, "运行期间不能重开另一份计时。");
                Assert.AreEqual(string.Empty, focus.Speak(), "专注点击不产生台词。");
                focusLease.Dispose();
                clock.Now += TimeSpan.FromMinutes(25);
                focus.UpdateFocus();
                Assert.IsFalse(focus.IsFocusActive);
                Assert.AreEqual(0, reminders, "隐藏时不播放提醒。");
                focusLease = focus.Subscribe((_, _) => { }, focusReminder: () => { reminders++; return true; });
                display.Fullscreen = true; focus.UpdateFocus(); Assert.AreEqual(0, reminders);
                display.Fullscreen = false; focus.UpdateFocus(); focus.UpdateFocus();
                Assert.AreEqual(1, reminders, "回到可见时只提醒一次。");
                focus.StartFocus(1); focus.CancelFocus(); clock.Now += TimeSpan.FromMinutes(2); focus.UpdateFocus();
                Assert.AreEqual(1, reminders, "取消不应留下到期提醒。");
                focus.StartFocus(1); focusLease.Dispose(); clock.Now += TimeSpan.FromMinutes(7); focus.UpdateFocus();
                using var expiredLease = focus.Subscribe((_, _) => { }, focusReminder: () => { reminders++; return true; });
                focus.UpdateFocus(); Assert.AreEqual(1, reminders, "过时提醒必须丢弃。");
            }
            System.Windows.Media.Imaging.BitmapSource Cover(byte red, byte blue)
            {
                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { blue, 32, red, 255 }, 4);
                bitmap.Freeze(); return bitmap;
            }
            pet.UpdateSnapshot(MediaSnapshot.Disconnected with { Artwork = Cover(180, 20) });
            Assert.IsFalse(pet.CanFixOutfitColor, "计算中不能锁住旧封面色。");
            pet.UpdateSnapshot(MediaSnapshot.Disconnected with { Artwork = Cover(20, 180) });
            var colorFrame = new System.Windows.Threading.DispatcherFrame();
            var colorWait = System.Diagnostics.Stopwatch.StartNew();
            var colorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            colorTimer.Tick += (_, _) => { if (!pet.IsArtworkColorPending || colorWait.ElapsedMilliseconds > 3000) { colorTimer.Stop(); colorFrame.Continue = false; } };
            colorTimer.Start(); System.Windows.Threading.Dispatcher.PushFrame(colorFrame);
            Assert.IsTrue(AFMediaBar.Classes.Utils.ColorHex.TryParse(pet.ArtworkColor, out var latestColor) && latestColor.B > latestColor.R * 2, "快速换封面只应用最新蓝色结果。");
            SettingsManager.Current.PetComponent = SettingsManager.Current.PetComponent with { FollowArtworkColor = true };
            pet.FixOutfitColor();
            Assert.IsFalse(SettingsManager.Current.PetComponent.FollowArtworkColor);
            Assert.AreEqual(pet.ArtworkColor, SettingsManager.Current.PetComponent.OutfitColorHex);
            pet.UpdateSnapshot(MediaSnapshot.Disconnected);
            Assert.IsNull(pet.ArtworkColor, "无封面应回退自定义色。");
            Assert.IsFalse(pet.TryNotify("title", "body"), "无可见宿主必须回退。");
            string? animation = null;
            var accept = false;
            Action? activate = null;
            pet.UpdateSnapshot(MediaSnapshot.Disconnected with { IsConnected = true });
            var lease = pet.Subscribe((state, _) => animation = state, (_, action) => { activate = action; return accept; });
            Assert.AreEqual("music-paused", animation, "连接但暂停时必须使用暂停音乐姿态。");
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
            foreach (var snapshot in new[] { MediaSnapshot.Disconnected, MediaSnapshot.Disconnected with { IsConnected = true },
                MediaSnapshot.Disconnected with { IsConnected = true, IsPlaying = true } })
            {
                pet.UpdateSnapshot(snapshot);
                var round = Enumerable.Range(0, 12).Select(_ => pet.NextPhrase()).ToArray();
                Assert.AreEqual(12, round.Distinct().Count());
                Assert.AreNotEqual(round[^1], pet.NextPhrase());
            }
            foreach (var phrase in new[] { new string('歌', 150), new string('！', 150) })
                Assert.IsTrue(PetVoiceService.CreatePcm(100, phrase).Length <= 96000);
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

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
