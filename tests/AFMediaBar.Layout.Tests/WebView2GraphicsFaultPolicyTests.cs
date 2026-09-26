using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// WebView2 图形层故障识别的测试：只拦"合成控件在尺寸变化中的空引用"，其余异常保持原崩溃路径。
/// Tests for identifying the WebView2 graphics fault: only the composition control's resize-path null reference is
/// interceptable; everything else keeps the original crash path.
/// </summary>
[TestClass]
public sealed class WebView2GraphicsFaultPolicyTests
{
    // 真机日志里两次闪退的堆栈形状（截取关键帧）。
    // The stack shape of the two real crashes in the field log (key frames excerpted).
    private const string FieldStack =
        """
           at Microsoft.Web.WebView2.Wpf.Direct3DHelper.CreateD3D11Texture(ID3D11Device device, Texture2DDescription texturedesc, ID3D11Texture2D& texture2d)
           at Microsoft.Web.WebView2.Wpf.GraphicsItemD3DImage.SetGraphicItem(Int32 width, Int32 height)
           at System.Windows.RoutedEventArgs.InvokeHandler(Delegate handler, Object target)
           at AFMediaBar.Views.Windows.TaskbarWindow.ApplyDesiredSizeRequest(MediaBarSizeRequest request, LayoutOrientation orientation)
        """;

    [TestMethod]
    public void TheRealWebView2GraphicsCrashIsRecognized()
    {
        Assert.IsTrue(WebView2GraphicsFaultPolicy.IsGraphicsFault(FieldStack));
    }

    [TestMethod]
    public void OtherFailuresAreNotIntercepted()
    {
        // 空引用但不在 WebView2 控件里：不是这一类故障，保持原崩溃路径。
        // A null reference outside the WebView2 control is not this fault and keeps the original crash path.
        Assert.IsFalse(WebView2GraphicsFaultPolicy.IsGraphicsFault(
            "   at AFMediaBar.SomeService.DoWork()"));
        // WebView2 的其它异常同样不拦。
        // Other WebView2 exceptions are not intercepted either.
        Assert.IsFalse(WebView2GraphicsFaultPolicy.IsGraphicsFault(
            "   at Microsoft.Web.WebView2.Wpf.WebView2CompositionControl.OnRender(DrawingContext dc)"));
        Assert.IsFalse(WebView2GraphicsFaultPolicy.IsGraphicsFault((string?)null));
        Assert.IsFalse(WebView2GraphicsFaultPolicy.IsGraphicsFault(string.Empty));
    }

    [TestMethod]
    public void StacklessOrOtherExceptionsAreNotIntercepted()
    {
        var nullReference = new NullReferenceException("x");
        Assert.IsFalse(WebView2GraphicsFaultPolicy.IsGraphicsFault(nullReference)); // 没有堆栈时不拦 / no stack, not classified

        var other = new InvalidOperationException("x");
        Assert.IsFalse(WebView2GraphicsFaultPolicy.IsGraphicsFault(other));
    }

    [TestMethod]
    public void TheRebuildBudgetIsFinite()
    {
        Assert.IsTrue(WebView2GraphicsFaultPolicy.CanRecover(0));
        Assert.IsTrue(WebView2GraphicsFaultPolicy.CanRecover(WebView2GraphicsFaultPolicy.MaximumRecoveries - 1));
        Assert.IsFalse(WebView2GraphicsFaultPolicy.CanRecover(WebView2GraphicsFaultPolicy.MaximumRecoveries));
        Assert.IsFalse(WebView2GraphicsFaultPolicy.CanRecover(99));
    }
}
