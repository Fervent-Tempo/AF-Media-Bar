// 验证 #301 的通用判据：只有目标变化才重建；格式签名覆盖扩展格式的真实差异。
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Audio;

[TestClass]
public sealed class SpectrumCaptureReconcilePolicyTests
{
    [TestMethod]
    public void OnlyFollowedTargetChangeRebuilds()
    {
        Assert.IsTrue(SpectrumCaptureReconcilePolicy.ShouldRestartForResolution(
            SpectrumCaptureSource.CurrentDevice, SpectrumCaptureDeviceMode.FollowMediaSource, targetChanged: true));
        Assert.IsFalse(SpectrumCaptureReconcilePolicy.ShouldRestartForResolution(
            SpectrumCaptureSource.CurrentDevice, SpectrumCaptureDeviceMode.FollowMediaSource, targetChanged: false));
        Assert.IsFalse(SpectrumCaptureReconcilePolicy.ShouldRestartForResolution(
            SpectrumCaptureSource.CurrentDevice, SpectrumCaptureDeviceMode.SystemDefault, targetChanged: true));
        Assert.IsFalse(SpectrumCaptureReconcilePolicy.ShouldRestartForResolution(
            SpectrumCaptureSource.AllApplications, SpectrumCaptureDeviceMode.FollowMediaSource, targetChanged: true));
    }

    [TestMethod]
    public void CoreFormatsMatchNormalizesExtensibleEncoding()
    {
        var capture = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var extensibleFloat = new WaveFormatExtensible(48000, 32, 2, useIeeeFloat: true, 32, 3);
        var extensiblePcm = new WaveFormatExtensible(48000, 32, 2, useIeeeFloat: false, 32, 3);

        Assert.IsTrue(SpectrumCaptureReconcilePolicy.CoreFormatsMatch(capture, extensibleFloat));
        Assert.IsFalse(SpectrumCaptureReconcilePolicy.CoreFormatsMatch(capture, extensiblePcm));
        Assert.IsTrue(SpectrumCaptureReconcilePolicy.CoreFormatsMatch(
            new WaveFormat(44100, 16, 2), new WaveFormatExtensible(44100, 16, 2, useIeeeFloat: false, 16, 3)));
    }

    [TestMethod]
    public void FormatSignatureIncludesExtensibleSubFormatValidBitsAndChannelMask()
    {
        var baseline = SpectrumCaptureReconcilePolicy.CreateFormatSignature(
            new WaveFormatExtensible(48000, 32, 2, useIeeeFloat: true, 32, 3));
        var validBitsChanged = SpectrumCaptureReconcilePolicy.CreateFormatSignature(
            new WaveFormatExtensible(48000, 32, 2, useIeeeFloat: true, 24, 3));
        var channelMaskChanged = SpectrumCaptureReconcilePolicy.CreateFormatSignature(
            new WaveFormatExtensible(48000, 32, 2, useIeeeFloat: true, 32, 0x3F));
        var subFormatChanged = SpectrumCaptureReconcilePolicy.CreateFormatSignature(
            new WaveFormatExtensible(48000, 32, 2, useIeeeFloat: false, 32, 3));

        Assert.IsFalse(SpectrumCaptureReconcilePolicy.HasFormatChanged(baseline, baseline));
        Assert.IsTrue(SpectrumCaptureReconcilePolicy.HasFormatChanged(baseline, validBitsChanged));
        Assert.IsTrue(SpectrumCaptureReconcilePolicy.HasFormatChanged(baseline, channelMaskChanged));
        Assert.IsTrue(SpectrumCaptureReconcilePolicy.HasFormatChanged(baseline, subFormatChanged));
    }
}
