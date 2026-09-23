using AFMediaBar.Classes.Services.Layout;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class DynamicIslandHoldPolicyTests
{
    [TestMethod]
    public void HoldVisiblyProgressesBeforeTheCommitThreshold()
    {
        var early = DynamicIslandHoldPolicy.GetPreviewExpansion(0.1, 0);
        var middle = DynamicIslandHoldPolicy.GetPreviewExpansion(0.2, 0);
        var late = DynamicIslandHoldPolicy.GetPreviewExpansion(0.35, 0);
        Assert.IsTrue(early > 0 && early < middle && middle < late && late < 1);
        Assert.IsFalse(DynamicIslandHoldPolicy.ShouldCommit(0.35));
        Assert.IsTrue(DynamicIslandGeometry.Calculate(1, middle).Height > 37);
    }

    [TestMethod]
    public void AbandonedHoldIsNeitherATapNorACommittedExpansion()
    {
        Assert.IsTrue(DynamicIslandHoldPolicy.IsTap(0.08));
        Assert.IsFalse(DynamicIslandHoldPolicy.IsTap(0.25));
        Assert.IsFalse(DynamicIslandHoldPolicy.ShouldCommit(0.25));
        Assert.IsFalse(DynamicIslandHoldPolicy.ShouldCommit(0.399));
        Assert.IsTrue(DynamicIslandHoldPolicy.ShouldCommit(0.4));
        Assert.IsFalse(DynamicIslandHoldPolicy.IsTap(0.4));
    }

    [TestMethod]
    public void ReholdingDuringCollapseStartsFromTheCurrentShape()
    {
        var start = 0.62;
        Assert.AreEqual(start, DynamicIslandHoldPolicy.GetPreviewExpansion(0, start), 1e-12);
        Assert.IsTrue(DynamicIslandHoldPolicy.GetPreviewExpansion(0.2, start) > start);
        Assert.AreEqual(0.97, DynamicIslandHoldPolicy.GetPreviewExpansion(0.2, 0.97), 1e-12);
    }

    [TestMethod]
    public void PreviewIsContinuousAndBoundedAcrossTheDeadline()
    {
        var before = DynamicIslandHoldPolicy.GetPreviewExpansion(0.4 - 1e-6, 0);
        var deadline = DynamicIslandHoldPolicy.GetPreviewExpansion(0.4, 0);
        Assert.AreEqual(before, deadline, 1e-8);
        Assert.IsTrue(deadline > 0.7 && deadline < 1);
        Assert.AreEqual(deadline, DynamicIslandHoldPolicy.GetPreviewExpansion(100, 0));
    }

    [TestMethod]
    public void InvalidClockValuesCannotCommitOrOpenTheSource()
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
        {
            Assert.IsFalse(DynamicIslandHoldPolicy.ShouldCommit(value));
            Assert.IsFalse(DynamicIslandHoldPolicy.IsTap(value));
            Assert.AreEqual(0, DynamicIslandHoldPolicy.GetProgress(value));
        }
        Assert.IsTrue(double.IsFinite(DynamicIslandHoldPolicy.GetPreviewExpansion(0.2, double.NaN)));
    }
}
