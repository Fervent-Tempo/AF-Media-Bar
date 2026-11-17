// 验证无有效封面主色不会生成伪色；测试直接覆盖主色算法本身，"从封面取色"入口用的是同一套算法。
using AFMediaBar.Classes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>透明封面与空直方图的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ArtworkDominantColorTests
{
    [DataTestMethod]
    [DataRow(3, false)] [DataRow(3, true)]
    public void TransparentPixelsHaveNoDominantColor(int count, bool dark)
    {
        Assert.AreEqual(0, DominantColorCalculator.Calculate(new byte[16], 2, 2, count, darkTheme: dark).Count);
        Assert.AreEqual(0, DominantColorCalculator.Calculate([], 0, 0, count).Count);
    }

    [DataTestMethod]
    [DataRow((byte)128)]
    public void EmptyWeightedHistogramHasNoInventedPeak(byte gray)
    {
        Assert.AreEqual(0, DominantColorCalculator.Calculate([gray, gray, gray, 255], 1, 1, 1).Count);
        Assert.AreEqual(1, DominantColorCalculator.Calculate([0, 0, 255, 255], 1, 1, 1).Count);
    }
}
