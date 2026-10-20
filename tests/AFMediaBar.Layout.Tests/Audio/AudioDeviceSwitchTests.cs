// 设备协调回归只替换平台边界，不读取或修改真实设备、音量及路由。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证合并请求、目标隔离、取消与失败反馈。</summary>
[TestClass]
public sealed class AudioDeviceSwitchTests
{
    [TestMethod]
    public async Task RapidReverseWheelAppliesOnlyFinalCandidate()
    {
        var platform = new FakeOperations();
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var target = new AudioDeviceTarget(AudioDeviceDirection.Output);
        var owner = new object();
        await coordinator.PreviewAsync(target, 1, owner);
        await coordinator.PreviewAsync(target, 1, owner);
        await coordinator.PreviewAsync(target, -1, owner);
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new[] { "Output:system:B" }, platform.Applied.ToArray());
        Assert.IsFalse(coordinator.HasPending(target));
    }

    [TestMethod]
    public async Task WheelStepsDuringEnumerationAreNotLost()
    {
        var platform = new FakeOperations { EnumerationGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var target = new AudioDeviceTarget(AudioDeviceDirection.Output);
        var owner = new object();
        var first = coordinator.PreviewAsync(target, 1, owner);
        var second = coordinator.PreviewAsync(target, 1, owner);
        platform.EnumerationGate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new[] { "Output:system:C" }, platform.Applied.ToArray());
    }

    [TestMethod]
    public async Task ClickReplacesWheelAndDirectionsRemainIndependent()
    {
        var platform = new FakeOperations();
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var owner = new object();
        var output = new AudioDeviceTarget(AudioDeviceDirection.Output);
        var input = new AudioDeviceTarget(AudioDeviceDirection.Input);
        await coordinator.PreviewAsync(output, 1, owner);
        await coordinator.PreviewAsync(input, 1, owner);
        await coordinator.SelectAsync(output, FakeOperations.Device("C", AudioDeviceDirection.Output), owner);
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEquivalent(new[] { "Output:system:C", "Input:system:B" }, platform.Applied.ToArray());
    }

    [TestMethod]
    public async Task InvalidationAndOwnerReleasePreventQueuedWrites()
    {
        var platform = new FakeOperations();
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var target = new AudioDeviceTarget(AudioDeviceDirection.Output);
        var owner = new object();
        await coordinator.PreviewAsync(target, 1, owner);
        platform.Invalidate();
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, platform.Applied.Count);
        await coordinator.PreviewAsync(target, 1, owner);
        coordinator.CancelOwner(owner);
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, platform.Applied.Count);
        coordinator.Dispose();
        coordinator.Dispose();
    }

    [TestMethod]
    public async Task MissingCurrentStartsAtFirstOrLastAndSingleCandidateStillApplies()
    {
        var platform = new FakeOperations { HasDefault = false };
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var target = new AudioDeviceTarget(AudioDeviceDirection.Output);
        var owner = new object();
        Assert.AreEqual("A", (await coordinator.PreviewAsync(target, 1, owner))?.PolicyId);
        coordinator.CancelOwner(owner);
        Assert.AreEqual("C", (await coordinator.PreviewAsync(target, -1, owner))?.PolicyId);
        coordinator.CancelOwner(owner);
        platform.DeviceCount = 1;
        await coordinator.PreviewAsync(target, 1, owner);
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new[] { "Output:system:A" }, platform.Applied.ToArray());
    }

    [TestMethod]
    public async Task ApplicationsAndResetDirectionsDoNotOverwriteEachOther()
    {
        var platform = new FakeOperations();
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var appA = new ApplicationAudioSnapshot("app-A", "app", "A", [], true, true);
        var appB = appA with { Identity = "app-B", DisplayName = "B" };
        var a = new AudioDeviceTarget(AudioDeviceDirection.Output, appA);
        var b = new AudioDeviceTarget(AudioDeviceDirection.Output, appB);
        await coordinator.PreviewAsync(a, 1, this);
        await coordinator.PreviewAsync(b, 1, this);
        await coordinator.SelectAsync(a, AudioDeviceSwitchCoordinator.DefaultChoice(AudioDeviceDirection.Output), this);
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEquivalent(new[] { "Output:app-A:default", "Output:app-B:A" }, platform.Applied.ToArray());
    }

    [TestMethod]
    public async Task PartialAndFailedResultsReachCallerWithoutClaimingSuccess()
    {
        var platform = new FakeOperations { Result = new(AudioDeviceOperationStatus.Partial) };
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var target = new AudioDeviceTarget(AudioDeviceDirection.Output);
        var result = await coordinator.SelectAsync(target, FakeOperations.Device("B", AudioDeviceDirection.Output), this);
        Assert.AreEqual(AudioDeviceOperationStatus.Partial, result.Status);
        platform.Result = new(AudioDeviceOperationStatus.Failed);
        result = await coordinator.SelectAsync(target, FakeOperations.Device("C", AudioDeviceDirection.Output), this);
        Assert.AreEqual(AudioDeviceOperationStatus.Failed, result.Status);
    }

    [TestMethod]
    public async Task UnsupportedApplicationRoutingDoesNotPublishOrApplyCandidate()
    {
        var platform = new FakeOperations { Supported = false };
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var target = new AudioDeviceTarget(AudioDeviceDirection.Input, new("app", "app", "App", [], true, true));
        AudioDeviceOperationResult? result = null;
        coordinator.Updated += update => result = update.Result;
        Assert.IsNull(await coordinator.PreviewAsync(target, 1, this));
        Assert.AreEqual(AudioDeviceOperationStatus.Unsupported, result?.Status);
        Assert.AreEqual(0, platform.Applied.Count);
    }

    [TestMethod]
    public async Task ForegroundTargetStaysFixedUntilWheelCompletes()
    {
        var platform = new FakeOperations();
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var foreground = new FakeForeground { Current = new("A", "app", "A", [], true, true) };
        var interaction = new AudioInteractionService(null!, null!, null!, null!, coordinator, foreground);
        var first = await interaction.ResolveTargetAsync(AudioDeviceDirection.Output, AudioDeviceSwitchTarget.ForegroundApplication, this, true);
        await coordinator.PreviewAsync(first!, 1, this);
        foreground.Current = foreground.Current with { Identity = "B" };
        var during = await interaction.ResolveTargetAsync(AudioDeviceDirection.Output, AudioDeviceSwitchTarget.ForegroundApplication, this, true);
        Assert.AreEqual("A", during?.Application?.Identity);
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var after = await interaction.ResolveTargetAsync(AudioDeviceDirection.Output, AudioDeviceSwitchTarget.ForegroundApplication, this, true);
        Assert.AreEqual("B", after?.Application?.Identity);
    }

    [TestMethod]
    public async Task ConcurrentIdentityReadsShareSnapshotAndReleasedOwnerDoesNotRetainIt()
    {
        var platform = new FakeOperations();
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        var foreground = new FakeForeground
        {
            Current = new("A", "app", "A", [], true, true),
            Gate = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var interaction = new AudioInteractionService(null!, null!, null!, null!, coordinator, foreground);
        var first = interaction.ResolveTargetAsync(AudioDeviceDirection.Input, AudioDeviceSwitchTarget.ForegroundApplication, this, true);
        foreground.Current = foreground.Current with { Identity = "B" };
        var second = interaction.ResolveTargetAsync(AudioDeviceDirection.Input, AudioDeviceSwitchTarget.ForegroundApplication, this, true);
        Assert.AreEqual(1, foreground.Reads);
        foreground.Gate.SetResult();
        Assert.AreEqual("A", (await first)?.Application?.Identity);
        Assert.AreEqual("A", (await second)?.Application?.Identity);
        foreground.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = interaction.ResolveTargetAsync(AudioDeviceDirection.Input, AudioDeviceSwitchTarget.ForegroundApplication, this, true);
        interaction.CancelDeviceOwner(this);
        foreground.Gate.SetResult();
        Assert.IsNull(await pending);
    }

    [TestMethod]
    public async Task NativeDefaultNotificationDoesNotCancelOtherDirection()
    {
        var platform = new FakeOperations { NotifyOnApply = true };
        using var coordinator = new AudioDeviceSwitchCoordinator(platform);
        await coordinator.PreviewAsync(new(AudioDeviceDirection.Output), 1, this);
        await coordinator.PreviewAsync(new(AudioDeviceDirection.Input), 1, this);
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEquivalent(new[] { "Output:system:B", "Input:system:B" }, platform.Applied.ToArray());
    }

    [TestMethod]
    public async Task InputTargetCapturesForegroundWithoutExperiment()
    {
        using var coordinator = new AudioDeviceSwitchCoordinator(new FakeOperations());
        var foreground = new FakeForeground { Current = new("A", "app", "A", [], true, true) };
        var interaction = new AudioInteractionService(null!, null!, null!, null!, coordinator, foreground);
        var input = await interaction.ResolveTargetAsync(AudioDeviceDirection.Input, AudioDeviceSwitchTarget.ForegroundApplication, this, true);
        Assert.AreEqual(AudioDeviceDirection.Input, input?.Direction);
        Assert.AreEqual("A", input?.Application?.Identity);
    }

    private sealed class FakeForeground : IForegroundAudioApplicationResolver
    {
        public ApplicationAudioSnapshot Current { get; set; } = null!;
        public TaskCompletionSource? Gate { get; set; }
        public int Reads { get; private set; }
        public async Task<ApplicationAudioSnapshot?> CaptureAsync(CancellationToken token = default)
        {
            Reads++;
            var captured = Current;
            if (Gate is { } gate) await gate.Task.WaitAsync(token);
            return captured;
        }
    }

    private sealed class FakeOperations : IAudioDeviceSwitchOperations
    {
        public int Version { get; private set; }
        public int PolicyVersion { get; private set; }
        public event EventHandler? Changed;
        public bool HasDefault { get; set; } = true;
        public bool Supported { get; set; } = true;
        public bool NotifyOnApply { get; set; }
        public bool IsAllowed(AudioDeviceOption device) => true;
        public int DeviceCount { get; set; } = 3;
        public TaskCompletionSource? EnumerationGate { get; set; }
        public AudioDeviceOperationResult Result { get; set; } = new(AudioDeviceOperationStatus.Applied);
        // 两个方向的延迟可并发完成，记录必须与平台操作一样保证线程安全。
        public System.Collections.Concurrent.ConcurrentQueue<string> Applied { get; } = new();
        public void Invalidate(bool cancelPending = true) { Version++; if (cancelPending) PolicyVersion++; Changed?.Invoke(this, new AudioDeviceInvalidationEventArgs(cancelPending)); }
        public static AudioDeviceOption Device(string id, AudioDeviceDirection direction) => new(id, id, id, false) { Direction = direction };
        public async Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, CancellationToken token)
        {
            if (EnumerationGate is { } gate) await gate.Task.WaitAsync(token);
            return new[] { "A", "B", "C" }.Take(DeviceCount).Select((id, index) => Device(id, direction) with { IsDefault = HasDefault && index == 0 }).ToArray();
        }
        public Task<ApplicationAudioRoute> ReadRouteAsync(ApplicationAudioSnapshot app, AudioDeviceDirection direction, CancellationToken token) =>
            Task.FromResult(new ApplicationAudioRoute(null, false, Supported));
        public Task<AudioDeviceOperationResult> ApplyAsync(AudioDeviceTarget target, AudioDeviceOption? device, CancellationToken token, Func<bool> isCurrent)
        {
            if (token.IsCancellationRequested || !isCurrent()) return Task.FromResult(new AudioDeviceOperationResult(AudioDeviceOperationStatus.Cancelled));
            Applied.Enqueue(target.Key + ":" + (device?.PolicyId ?? "default"));
            if (NotifyOnApply) Invalidate(false);
            return Task.FromResult(Result);
        }
    }
}
