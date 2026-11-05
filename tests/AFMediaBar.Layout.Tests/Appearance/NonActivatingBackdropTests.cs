// 非激活材质只由适配器选择 Accent 状态；测试注入记录调用，不依赖真实桌面。
// The adapter alone chooses the Accent state for non-activating material; tests record calls without a real desktop.
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class NonActivatingBackdropTests
{
    [TestMethod]
    public void AcrylicUsesAcrylicBlurAndMicaUsesBlurBehind()
    {
        var states = new List<int>();
        var adapter = new NativeWindowBackdropAdapter(22621, (_, _, _) => true, (_, _) => true,
            (_, state, _) => { states.Add(state); return true; });

        Assert.IsTrue(adapter.ApplyNonActivatingBackdrop(1, ApplicationBackdropMode.Acrylic, unchecked((int)0x99FFFFFF)));
        Assert.AreEqual(4, states.Last());

        Assert.IsTrue(adapter.ApplyNonActivatingBackdrop(1, ApplicationBackdropMode.Mica, unchecked((int)0x99FFFFFF)));
        Assert.AreEqual(3, states.Last());

        Assert.IsTrue(adapter.ApplyNonActivatingBackdrop(1, ApplicationBackdropMode.MicaAlt, unchecked((int)0x99FFFFFF)));
        Assert.AreEqual(3, states.Last());
    }

    [TestMethod]
    public void SolidModeDoesNotUseAccent()
    {
        var states = new List<int>();
        var adapter = new NativeWindowBackdropAdapter(22621, (_, _, _) => true, (_, _) => true,
            (_, state, _) => { states.Add(state); return true; });

        Assert.IsFalse(adapter.ApplyNonActivatingBackdrop(1, ApplicationBackdropMode.FluentSolid, 0));
        Assert.AreEqual(0, states.Count);
    }

    [TestMethod]
    public void AccentFailureIsReportedToCaller()
    {
        var adapter = new NativeWindowBackdropAdapter(22621, (_, _, _) => true, (_, _) => true,
            (_, _, _) => false);

        Assert.IsFalse(adapter.ApplyNonActivatingBackdrop(1, ApplicationBackdropMode.Acrylic, unchecked((int)0x99FFFFFF)));
    }
}
