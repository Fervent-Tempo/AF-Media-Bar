// 注入 DWM 失败验证材质必要调用的结果传播；不切换桌面主题或系统材质设置。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using System.Reflection;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>必要材质调用失败和不透明主题回退的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowBackdropFailureTests
{
    [TestMethod]
    public void FailedMaterialRestoresOpaqueThemeSurface()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current.Appearance;
            using var icons = new AppIconService();
            var adapter = new NativeWindowBackdropAdapter(22621, (_, _, _) => false, (_, _) => true);
            using var service = new WindowAppearanceService(adapter, icons);
            var window = new FluentWindow
            {
                Left = -32000, Top = -32000, Width = 1, Height = 1,
                ShowInTaskbar = false, ShowActivated = false
            };
            try
            {
                SettingsManager.Current.Appearance = previous with { BackdropMode = ApplicationBackdropMode.Mica };
                window.Resources["ApplicationBackgroundBrush"] = Brushes.DeepPink;
                // EnsureHandle 的隐藏窗口尚无视觉根；Show 在屏幕外连接视觉树后才会走外观应用。
                window.Show();
                typeof(WindowAppearanceService).GetMethod("ApplyWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(service, [window]);
                Assert.AreEqual(255, ((SolidColorBrush)window.Background).Color.A);
                Assert.AreEqual(Colors.DeepPink, ((SolidColorBrush)window.Background).Color);
            }
            finally { window.Close(); SettingsManager.Current.Appearance = previous; }
            return Task.CompletedTask;
        });
    }
    [DataTestMethod]
    [DataRow(22000)] [DataRow(22621)]
    public void DwmMaterialFailureIsReturnedToSolidFallback(int build)
    {
        var adapter = new NativeWindowBackdropAdapter(build, (_, attribute, value) =>
            !(attribute == 38 && value == 2) && !(attribute == 1029 && value == 1), (_, _) => true);
        Assert.IsFalse(adapter.ApplyBackdrop((nint)1, ApplicationBackdropMode.Mica, 0));
    }

    [DataTestMethod]
    [DataRow(22000)] [DataRow(22621)]
    public void FailedFrameExtensionDoesNotReportSuccessfulMaterial(int build)
    {
        var materialCalls = 0;
        var adapter = new NativeWindowBackdropAdapter(build, (_, attribute, value) =>
        {
            if ((attribute == 38 && value == 2) || (attribute == 1029 && value == 1)) materialCalls++;
            return true;
        }, (_, _) => false);
        Assert.IsFalse(adapter.ApplyBackdrop((nint)1, ApplicationBackdropMode.Mica, 0));
        Assert.AreEqual(0, materialCalls);
    }

    [TestMethod]
    public void CosmeticAttributeFailureDoesNotRejectSuccessfulMaterial()
    {
        var adapter = new NativeWindowBackdropAdapter(22621, (_, attribute, _) => attribute == 38, (_, _) => true);
        Assert.IsTrue(adapter.ApplyBackdrop((nint)1, ApplicationBackdropMode.Mica, 0));
        adapter.SetThemeAttributes((nint)1, true);
        Assert.IsFalse(adapter.SetFrame(nint.Zero, false));
    }
}
