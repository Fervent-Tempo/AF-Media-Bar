// 非激活表面的三种材质只能近似，但必须保持可辨识的浓度和底色差异。
// The three non-activating materials are approximations, but their tint strength must stay distinguishable.
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class NonActivatingTintTests
{
    [TestMethod]
    public void MaterialsHaveDistinctTintStrengthAndOrder()
    {
        var acrylic = Alpha(WindowBackdropPolicy.ResolveNonActivatingTint(ApplicationBackdropMode.Acrylic, true, 60));
        var mica = Alpha(WindowBackdropPolicy.ResolveNonActivatingTint(ApplicationBackdropMode.Mica, true, 60));
        var micaAlt = Alpha(WindowBackdropPolicy.ResolveNonActivatingTint(ApplicationBackdropMode.MicaAlt, true, 60));

        Assert.AreEqual(153, acrylic);
        Assert.AreEqual(193, mica);
        Assert.AreEqual(223, micaAlt);
        Assert.IsTrue(acrylic < mica && mica < micaAlt, "Mica and Mica Alt must read as progressively heavier than Acrylic.");
    }

    [TestMethod]
    public void HundredPercentConcentrationIsOpaqueForAllMaterials()
    {
        foreach (var mode in Enum.GetValues<ApplicationBackdropMode>())
        {
            var alpha = Alpha(WindowBackdropPolicy.ResolveNonActivatingTint(mode, false, 100));
            Assert.AreEqual(byte.MaxValue, alpha, mode.ToString());
        }
    }

    private static int Alpha(int tint) => (tint >> 24) & 0xFF;
}
