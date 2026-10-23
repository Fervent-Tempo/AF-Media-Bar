// 防止黑白交界反复换色，以及重新合并组件后把文字、频谱或按钮采入前景判断。
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class ForegroundSamplingStabilityTests
{
    [TestMethod]
    public void MixedBackgroundKeepsExistingColorWhileUniformBackgroundCanChangeIt()
    {
        foreach (var lightCount in new[] { 40, 48, 52, 60 })
        {
            var samples = Enumerable.Repeat(Colors.White, lightCount).Concat(Enumerable.Repeat(Colors.Black, 100 - lightCount)).ToArray();
            foreach (var lightText in new[] { true, false })
            {
                var previous = new PlayerForegroundDecision(lightText);
                Assert.AreEqual(previous, PlayerForegroundPolicy.Resolve(samples, previous));
            }
        }
        Assert.IsFalse(PlayerForegroundPolicy.Resolve([Colors.White], new(true))!.Value.UsesLightText);
        Assert.IsTrue(PlayerForegroundPolicy.Resolve([Colors.Black], new(false))!.Value.UsesLightText);
        Assert.IsNotNull(PlayerForegroundPolicy.Resolve([Colors.White, Colors.Black]));
    }

    [TestMethod]
    public void OppositeCandidateNeedsConfirmationAndResetRejectsPreviousCandidate()
    {
        var confirmation = new ForegroundDecisionConfirmation();
        var light = new PlayerForegroundDecision(true);
        var dark = new PlayerForegroundDecision(false);
        Assert.AreEqual(light, confirmation.Observe(light, null));
        Assert.AreEqual(light, confirmation.Observe(dark, light));
        Assert.IsTrue(confirmation.HasPending);
        Assert.AreEqual(light, confirmation.Observe(light, light));
        Assert.IsFalse(confirmation.HasPending);
        Assert.AreEqual(light, confirmation.Observe(dark, light));
        confirmation.Reset();
        Assert.AreEqual(light, confirmation.Observe(dark, light), "移动或挂起后的旧候选不能算作确认。");
        Assert.AreEqual(dark, confirmation.Observe(dark, light));
        Assert.IsFalse(confirmation.HasPending);
        Assert.AreEqual(dark, confirmation.Observe(light, dark));
        confirmation.Observe(null, dark);
        Assert.AreEqual(dark, confirmation.Observe(light, dark), "无效样本应中断连续确认。");
    }

    [TestMethod]
    public void SamplingBandsStaySeparateAndAvoidTextInBothOrientations()
    {
        var cases = new[]
        {
            (Host: new Int32Rect(-400, 100, 440, 66), Surface: new Int32Rect(-360, 110, 320, 46), Vertical: false),
            (Host: new Int32Rect(-100, -700, 66, 440), Surface: new Int32Rect(-90, -660, 46, 320), Vertical: true)
        };
        foreach (var item in cases)
        {
            var bands = ForegroundSampleRegionPolicy.Create(item.Surface, item.Host, item.Vertical, 1.5);
            Assert.AreEqual(2, bands.Count);
            var host = ToRect(item.Host);
            var text = ToRect(item.Surface);
            foreach (var band in bands)
            {
                Assert.IsTrue(host.Contains(ToRect(band)));
                Assert.IsFalse(text.IntersectsWith(ToRect(band)), "不能把文字或悬停按钮本身喂回采样。");
            }
            Assert.IsFalse(ToRect(bands[0]).IntersectsWith(ToRect(bands[1])));
        }
    }

    private static Rect ToRect(Int32Rect bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
}
