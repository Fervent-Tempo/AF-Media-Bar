// 验证频谱几何：面积轮廓、单柱退化、光点/峰值帽边界与柱宽变化。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Presentation;

[TestClass]
public sealed class SpectrumPresentationPolicyTests
{
    private static readonly float[] Bands = Enumerable.Range(0, 14).Select(index => 0.15f + 0.05f * index).ToArray();

    [TestMethod]
    public void AreaOutlineStartsAndEndsOnBaseline()
    {
        const double height = 26;
        var outline = SpectrumPresentationPolicy.CreateAreaOutline(Bands, 100, height);

        Assert.AreEqual(SpectrumPresentationPolicy.CalculateAreaPointCount(Bands.Length), outline.Length);
        Assert.AreEqual(0, outline[0].X, 0.0001);
        Assert.AreEqual(height, outline[0].Y, 0.0001);
        Assert.AreEqual(height, outline[^1].Y, 0.0001);
        Assert.IsTrue(outline.All(point => point.Y >= -0.0001 && point.Y <= height + 0.0001));
        Assert.IsTrue(outline.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)));
    }

    [TestMethod]
    public void AreaSingleBandDegeneratesToRectangle()
    {
        var outline = SpectrumPresentationPolicy.CreateAreaOutline([0.5f], 100, 26);

        Assert.AreEqual(4, outline.Length);
        Assert.AreEqual(26, outline[0].Y, 0.0001);
        Assert.AreEqual(13, outline[1].Y, 0.0001);
        Assert.AreEqual(13, outline[2].Y, 0.0001);
        Assert.AreEqual(26, outline[3].Y, 0.0001);
    }

    [TestMethod]
    public void WaveformOutlineStaysSymmetricAndInsideContent()
    {
        const double height = 30;
        var outline = SpectrumPresentationPolicy.CreateWaveformOutline(Bands, 100, height);

        Assert.AreEqual(SpectrumPresentationPolicy.CalculateWaveformPointCount(Bands.Length), outline.Length);
        Assert.IsTrue(outline.All(point => point.Y >= -0.0001 && point.Y <= height + 0.0001));
        for (var index = 0; index < outline.Length / 2; index++)
        {
            var top = outline[index];
            var bottom = outline[outline.Length - 1 - index];
            Assert.AreEqual(top.X, bottom.X, 0.0001);
            Assert.AreEqual(height, top.Y + bottom.Y, 0.0001);
        }
    }

    [TestMethod]
    public void DotAndPeakCapStayInsideContent()
    {
        const double height = 26;
        foreach (var level in new[] { 0f, 0.01f, 0.5f, 1f })
        {
            var dotTop = SpectrumPresentationPolicy.ResolveDotCenterYDip(
                level, 100, height, SpectrumPresentationPolicy.DotDiameterDip) -
                SpectrumPresentationPolicy.DotDiameterDip / 2;
            Assert.IsTrue(dotTop >= -0.0001 && dotTop + SpectrumPresentationPolicy.DotDiameterDip <= height + 0.0001);

            var capTop = SpectrumPresentationPolicy.ResolvePeakCapCenterYDip(
                level, 100, height, SpectrumPresentationPolicy.PeakCapHeightDip, symmetric: false, bottom: false);
            Assert.IsTrue(capTop >= -0.0001 && capTop + SpectrumPresentationPolicy.PeakCapHeightDip <= height + 0.0001);
        }
    }

    [TestMethod]
    [DataRow(9, 35d)]
    [DataRow(14, 55d)]
    [DataRow(24, 95d)]
    [DataRow(32, 127d)]
    public void ContentWidthFollowsBarPitch(int bandCount, double expected)
    {
        Assert.AreEqual(expected, SpectrumPresentationPolicy.CalculateContentWidthDip(bandCount), 0.0001);
        Assert.AreEqual(expected, SpectrumPresentationPolicy.CalculateBandContentWidth(bandCount), 0.0001);
        Assert.AreEqual(expected + 2, SpectrumPresentationPolicy.CalculateSurfaceWidthDip(bandCount), 0.0001);
    }

    [TestMethod]
    public void CompactBandCountKeepsUnclampedWidth()
    {
        Assert.AreEqual(3, SpectrumPresentationPolicy.CalculateBandContentWidth(1), 0.0001);
        Assert.AreEqual(35, SpectrumPresentationPolicy.CalculateContentWidthDip(1), 0.0001);
    }

    [TestMethod]
    public void NewLimitsAcceptThirtyTwoBandsAndThirtySixDip()
    {
        var normalized = (SpectrumComponentSettings.Default with
        {
            BandCount = 99,
            ContentHeightDip = 99
        }).Normalize();

        Assert.AreEqual(32, normalized.BandCount);
        Assert.AreEqual(36, normalized.ContentHeightDip);
        Assert.AreEqual(127, SpectrumPresentationPolicy.CalculateContentWidthDip(normalized.BandCount), 0.0001);
    }
}
