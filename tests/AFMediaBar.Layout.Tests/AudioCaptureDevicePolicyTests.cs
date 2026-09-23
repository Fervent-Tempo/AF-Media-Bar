using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 频谱采集目标设备的选择：先比可听等级，同档内保持当前端点、其次默认端点、最后取最响的端点；
/// 全部不可听时沿用当前端点，没有则回退默认端点。
/// Target-device selection for the spectrum capture: the highest audibility rank wins, and within that rank the current endpoint is
/// kept first, then the default one, then the loudest; with nothing audible the current endpoint is kept, or the default falls back.
/// </summary>
[TestClass]
public sealed class AudioCaptureDevicePolicyTests
{
    private const string Current = "current";
    private const string Default = "default";
    private const string Other = "other";

    private static AudioEndpointAudibility Application(string id, float peak = 0.1f) =>
        new(id, AudioCaptureDevicePolicy.RankApplication, peak);

    private static AudioEndpointAudibility SystemOnly(string id, float peak = 0.1f) =>
        new(id, AudioCaptureDevicePolicy.RankSystemOnly, peak);

    [TestMethod]
    public void AudibleCurrentEndpointIsKeptEvenWhenAnotherIsLouder()
    {
        var target = AudioCaptureDevicePolicy.SelectTarget(
            Current,
            Default,
            [Application(Current, 0.05f), Application(Other, 0.5f)]);

        Assert.AreEqual(Current, target);
    }

    [TestMethod]
    public void AudibleDefaultWinsWhenTheCurrentEndpointIsNotAudible()
    {
        // 当前采集落在默认端点上（重启后的常见状态）：默认端点在出声时立即继续用它。
        // The capture happens to sit on the default endpoint (the common state right after a reboot): as soon as the default is audible
        // it stays the target.
        var target = AudioCaptureDevicePolicy.SelectTarget(
            Current,
            Default,
            [Application(Default, 0.05f), Application(Other, 0.5f)]);

        Assert.AreEqual(Default, target);
    }

    [TestMethod]
    public void LoudestEndpointWinsWhenNeitherCurrentNorDefaultIsAudible()
    {
        var target = AudioCaptureDevicePolicy.SelectTarget(
            Current,
            Default,
            [Application(Other, 0.2f), Application("quiet", 0.01f)]);

        Assert.AreEqual(Other, target);
    }

    [TestMethod]
    public void AnApplicationStreamOutranksTheSystemMixerEvenWhenQuieter()
    {
        // 只有系统混音进程出声的端点通常是虚拟声卡送到物理端点的回声；有应用出声的端点优先，哪怕它更轻。
        // An endpoint where only the system mixer is audible is usually the echo a virtual driver sends to a physical endpoint; an
        // endpoint with an application playing wins even when it is quieter.
        var target = AudioCaptureDevicePolicy.SelectTarget(
            Current,
            Default,
            [SystemOnly(Default, 0.5f), Application(Other, 0.01f)]);

        Assert.AreEqual(Other, target);
    }

    [TestMethod]
    public void WithinOneRankTheLoudestEndpointWins()
    {
        var target = AudioCaptureDevicePolicy.SelectTarget(
            Current,
            Default,
            [Application(Other, 0.01f), Application("loud", 0.4f)]);

        Assert.AreEqual("loud", target);
    }

    [TestMethod]
    public void NothingAudibleKeepsTheCurrentEndpointOrFallsBackToTheDefault()
    {
        Assert.AreEqual(
            Current,
            AudioCaptureDevicePolicy.SelectTarget(Current, Default, []));
        Assert.AreEqual(
            Default,
            AudioCaptureDevicePolicy.SelectTarget(null, Default, []));
        // 候选列表里即使有"静音条目"（等级 0）也不参与选择。
        // Even a silent entry (rank 0) in the candidate list takes no part in the choice.
        Assert.AreEqual(
            Current,
            AudioCaptureDevicePolicy.SelectTarget(
                Current,
                Default,
                [new AudioEndpointAudibility(Other, AudioCaptureDevicePolicy.RankSilent, 0f)]));
    }

    [TestMethod]
    public void DeviceIdentifiersCompareCaseInsensitively()
    {
        var target = AudioCaptureDevicePolicy.SelectTarget(
            "{0.0.0.00000000}.{ABC}",
            Default,
            [Application("{0.0.0.00000000}.{abc}", 0.05f)]);

        Assert.AreEqual("{0.0.0.00000000}.{ABC}", target);
    }

    [TestMethod]
    public void NullFallbackEndpointsStillResolveToTheAudibleOne()
    {
        Assert.AreEqual(
            Other,
            AudioCaptureDevicePolicy.SelectTarget(null, null, [Application(Other, 0.1f)]));
        Assert.IsNull(AudioCaptureDevicePolicy.SelectTarget(null, null, []));
    }

    [TestMethod]
    public void ScanIntervalFollowsThePruneLevelAndPausesEnumeration()
    {
        Assert.AreEqual(AudioCaptureDevicePolicy.NormalScanInterval, AudioCaptureDevicePolicy.ResolveScanInterval(MemoryPruneLevel.None));
        Assert.AreEqual(AudioCaptureDevicePolicy.IdleScanInterval, AudioCaptureDevicePolicy.ResolveScanInterval(MemoryPruneLevel.Idle));
        Assert.AreEqual(AudioCaptureDevicePolicy.PausedScanInterval, AudioCaptureDevicePolicy.ResolveScanInterval(MemoryPruneLevel.DisplayOff));
        Assert.AreEqual(AudioCaptureDevicePolicy.PausedScanInterval, AudioCaptureDevicePolicy.ResolveScanInterval(MemoryPruneLevel.Suspended));

        // 有采样需求时正常与空闲档位允许枚举；无需求、息屏与睡眠都不枚举。
        // Normal and idle allow enumeration only with sample demand; no demand, display-off and suspend do not enumerate.
        Assert.IsTrue(AudioCaptureDevicePolicy.ShouldScan(MemoryPruneLevel.None, true));
        Assert.IsTrue(AudioCaptureDevicePolicy.ShouldScan(MemoryPruneLevel.Idle, true));
        Assert.IsFalse(AudioCaptureDevicePolicy.ShouldScan(MemoryPruneLevel.DisplayOff, true));
        Assert.IsFalse(AudioCaptureDevicePolicy.ShouldScan(MemoryPruneLevel.Suspended, true));
        Assert.IsFalse(AudioCaptureDevicePolicy.ShouldScan(MemoryPruneLevel.None, false));
        Assert.IsFalse(AudioCaptureDevicePolicy.ShouldScan(MemoryPruneLevel.Idle, false));
    }
}
