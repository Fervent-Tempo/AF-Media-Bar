// 以会话写入替身验证音量/静音请求顺序、失败回读和停止；不修改真实音频。
using System.Collections.Concurrent;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>共享音量和静音队列的行为回归。</summary>
[TestClass]
public sealed class ApplicationAudioControlTests
{
    [TestMethod]
    public void ReadbackAndFailureRestoreDoNotWriteVolumeOrEraseMetadata()
    {
        var writes = 0;
        var icon = new byte[] { 1, 2 };
        var row = new AFMediaBar.ViewModels.Windows.ApplicationVolumeItemViewModel(new("A", "Old", 72, false, true, icon), (_, _, _) => writes++);
        row.Synchronize(new("A", "Fallback", 72, true, false, null), updateMetadata: false);
        Assert.AreSame(icon, row.IconData);
        Assert.IsTrue(row.IsCurrentMedia);
        Assert.AreEqual("Old", row.DisplayName);
        Assert.IsTrue(row.IsMuted);
        var replacement = new byte[] { 3 };
        row.Synchronize(new("A", "Renamed", 50, false, false, replacement));
        Assert.AreEqual("Renamed", row.DisplayName);
        Assert.AreSame(replacement, row.IconData);
        Assert.IsFalse(row.IsCurrentMedia);
        Assert.AreEqual(0, writes, "刷新与回读都不能反向写入音量。");
        row.VolumePercent = 20;
        row.IsMuted = true;
        Assert.AreEqual(1, writes);
        row.RestoreConfirmedState();
        Assert.AreEqual(50, row.VolumePercent);
        Assert.IsFalse(row.IsMuted);
        Assert.AreEqual(1, writes, "恢复最后确认状态不能生成另一次音量请求。");
    }

    [TestMethod]
    public async Task MuteSupersedesDelayedVolumeWithoutChangingReadbackLevel()
    {
        var control = new Control();
        using var coordinator = new ApplicationAudioControlCoordinator(control, TimeSpan.FromMilliseconds(100));
        var volume = coordinator.SetVolumeAsync("A", 10, false, CancellationToken.None);
        var mute = await coordinator.SetMuteAsync("A", true, CancellationToken.None);
        Assert.AreEqual(AudioDeviceOperationStatus.Applied, mute.Operation.Status);
        Assert.AreEqual(72, mute.Snapshot!.VolumePercent);
        Assert.IsTrue(mute.Snapshot.IsMuted);
        Assert.AreEqual(AudioDeviceOperationStatus.Cancelled, (await volume).Operation.Status);
        Assert.AreEqual(1, control.Writes.Count);
        Assert.IsTrue(coordinator.IsCurrent("A", mute.Version));
    }

    [TestMethod]
    public async Task SerializesOneApplicationAndKeepsOtherApplicationsIndependent()
    {
        using var control = new BlockingControl();
        using var coordinator = new ApplicationAudioControlCoordinator(control, TimeSpan.Zero);
        var first = coordinator.SetVolumeAsync("A", 5, true, CancellationToken.None);
        await control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = coordinator.SetMuteAsync("A", true, CancellationToken.None);
        try
        {
            var other = await coordinator.SetVolumeAsync("B", 20, true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(20, other.Snapshot!.VolumePercent);
            Assert.IsFalse(control.Writes.Contains("A"));
        }
        finally { control.Release.Set(); }
        Assert.AreEqual(AudioDeviceOperationStatus.Cancelled, (await first).Operation.Status);
        Assert.IsTrue((await latest).Snapshot!.IsMuted);
        Assert.AreEqual(1, control.Writes.Count(name => name == "A"));
    }

    [TestMethod]
    public async Task DisposalCancelsQueuedWritesAndDiscardsLateCompletion()
    {
        using var control = new BlockingControl();
        using var coordinator = new ApplicationAudioControlCoordinator(control, TimeSpan.Zero);
        var active = coordinator.SetVolumeAsync("A", 5, true, CancellationToken.None);
        await control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = coordinator.SetMuteAsync("A", true, CancellationToken.None);
        coordinator.Dispose();
        coordinator.Dispose();
        Assert.AreEqual(AudioDeviceOperationStatus.Cancelled, (await queued).Operation.Status);
        Assert.AreEqual(AudioDeviceOperationStatus.Cancelled, (await coordinator.SetMuteAsync("B", true, CancellationToken.None)).Operation.Status);
        control.Release.Set();
        Assert.AreEqual(AudioDeviceOperationStatus.Cancelled, (await active).Operation.Status);
        Assert.AreEqual(0, control.Writes.Count);
    }

    [TestMethod]
    public async Task PreservesPartialFailureAndActualReadback()
    {
        using var coordinator = new ApplicationAudioControlCoordinator(new PartialControl(), TimeSpan.Zero);
        var result = await coordinator.SetMuteAsync("A", true, CancellationToken.None);
        Assert.AreEqual(AudioDeviceOperationStatus.Partial, result.Operation.Status);
        Assert.IsFalse(result.Snapshot!.IsMuted, "回读不是请求值，不能把部分成功显示为全部静音。");
        Assert.AreEqual(55, result.Snapshot.VolumePercent);
    }

    private sealed class Control : IApplicationAudioControl
    {
        public ConcurrentQueue<string> Writes { get; } = new();
        public ApplicationAudioControlResult Apply(string name, int? percent, bool? muted, Func<bool> isCurrent)
        {
            if (!isCurrent()) return new(new(AudioDeviceOperationStatus.Cancelled));
            Writes.Enqueue(name);
            return new(new(AudioDeviceOperationStatus.Applied), new(name, name, percent ?? 72, muted ?? false, false, null));
        }
    }
    private sealed class BlockingControl : IApplicationAudioControl, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public ConcurrentQueue<string> Writes { get; } = new();
        private int _first;
        public ApplicationAudioControlResult Apply(string name, int? percent, bool? muted, Func<bool> isCurrent)
        {
            if (name == "A" && Interlocked.Exchange(ref _first, 1) == 0)
            { Entered.TrySetResult(); if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); }
            if (!isCurrent()) return new(new(AudioDeviceOperationStatus.Cancelled));
            Writes.Enqueue(name);
            return new(new(AudioDeviceOperationStatus.Applied), new(name, name, percent ?? 72, muted ?? false, false, null));
        }
        public void Dispose() { Release.Set(); Release.Dispose(); }
    }
    private sealed class PartialControl : IApplicationAudioControl
    {
        public ApplicationAudioControlResult Apply(string name, int? percent, bool? muted, Func<bool> isCurrent) =>
            new(new(AudioDeviceOperationStatus.Partial), new(name, name, 55, false, false, null));
    }
}
