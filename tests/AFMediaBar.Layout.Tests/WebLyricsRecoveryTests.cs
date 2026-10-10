// 用真实异常堆栈、STA 队列和未初始化控件验证恢复边界，不耗尽内存或启动浏览器。
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Components;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>图形故障分类、会话停用和恢复生命周期回归。</summary>
[TestClass]
public sealed class WebLyricsRecoveryTests
{
    private const string DisplayStack = "   at System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(Int32 errorCode)\n" +
        "   at Microsoft.Web.WebView2.Wpf.Direct3DHelper.CreateD3D9Device(IntPtr mainwindow)\n" +
        "   at Microsoft.Web.WebView2.Wpf.GraphicsItemD3DImage.RecoverAfterDisplayChange()";

    [TestMethod]
    public void ResourceFailureRequiresBothKnownPathAndNativeError()
    {
        Assert.AreEqual(WebLyricsRecoveryAction.DisableForSession, Classify(new OutOfMemoryException(), DisplayStack));
        Assert.AreEqual(WebLyricsRecoveryAction.DisableForSession, Classify(new COMException("gpu", unchecked((int)0x8876017C)), DisplayStack));
        Assert.AreEqual(WebLyricsRecoveryAction.None, Classify(new OutOfMemoryException(), "at App.Allocate()"));
        Assert.AreEqual(WebLyricsRecoveryAction.None, Classify(new COMException("invalid", unchecked((int)0x80070057)), DisplayStack));
        Assert.AreEqual(WebLyricsRecoveryAction.None, Classify(new OutOfMemoryException(), DisplayStack.Replace("RecoverAfterDisplayChange", "UpdateSize")));
        Assert.AreEqual(WebLyricsRecoveryAction.Rebuild, Classify(new NullReferenceException(),
            "at Microsoft.Web.WebView2.Wpf.Direct3DHelper.CreateD3D11Texture()\nat Microsoft.Web.WebView2.Wpf.GraphicsItemD3DImage.SetGraphicItem()"));
        Assert.IsFalse(WebView2GraphicsFaultPolicy.CanRecover(3));
    }

    private static WebLyricsRecoveryAction Classify(Exception exception, string stack)
    {
        ExceptionDispatchInfo.SetRemoteStackTrace(exception, stack);
        return WebView2GraphicsFaultPolicy.Classify(exception);
    }

    [TestMethod]
    public void SessionRequiresAnOwnerAndSurvivesReplacingThatOwner()
    {
        var session = new WebLyricsRecoverySession();
        Assert.IsFalse(session.Request(WebLyricsRecoveryAction.DisableForSession));
        Assert.IsFalse(session.IsDisabled);
        Func<WebLyricsRecoveryAction, bool> owner = _ => true;
        session.RecoveryRequested += owner;
        Assert.IsTrue(session.Request(WebLyricsRecoveryAction.DisableForSession));
        session.RecoveryRequested -= owner;
        Assert.IsTrue(session.IsDisabled);
        Assert.AreEqual(LyricsWebViewLifetimeAction.None, LyricsWebViewLifetimePolicy.Resolve(true, true, session.IsDisabled, true, 2, false));
        var notices = 0;
        session.Degraded += () => notices++;
        session.ReportDegraded(); session.ReportDegraded();
        Assert.AreEqual(1, notices);
    }

    [TestMethod]
    public void QueueMergesFaultsAndDiscardsCanceledOrStaleWork()
    {
        StaTest.Run(async dispatcher =>
        {
            var queue = new WebLyricsRecoveryQueue(dispatcher);
            var applied = new List<WebLyricsRecoveryAction>();
            queue.TryQueue(WebLyricsRecoveryAction.Rebuild, () => true, applied.Add);
            queue.TryQueue(WebLyricsRecoveryAction.DisableForSession, () => true, applied.Add);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            CollectionAssert.AreEqual(new[] { WebLyricsRecoveryAction.DisableForSession }, applied);
            var current = true;
            queue.TryQueue(WebLyricsRecoveryAction.Rebuild, () => current, applied.Add);
            current = false;
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            queue.TryQueue(WebLyricsRecoveryAction.Rebuild, () => true, applied.Add);
            queue.Cancel();
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.AreEqual(1, applied.Count);
        });
    }

    [TestMethod]
    public void ProcessFailureIsOneShotAndGpuExitDoesNotFailTheView()
    {
        StaTest.Run(_ =>
        {
            using var renderer = new LyricsWebViewRenderer(new WebView2CompositionControl());
            var failures = 0;
            renderer.Failed += (_, _) => failures++;
            renderer.HandleProcessFailure(CoreWebView2ProcessFailedKind.GpuProcessExited, CoreWebView2ProcessFailedReason.Unexpected, 1);
            renderer.PauseDelivery();
            for (var i = 0; i < 3; i++) renderer.HandleProcessFailure(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive, CoreWebView2ProcessFailedReason.Unresponsive, 259);
            Assert.AreEqual(0, failures);
            renderer.ResumeDelivery();
            renderer.HandleProcessFailure(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive, CoreWebView2ProcessFailedReason.Unresponsive, 259);
            Assert.AreEqual(0, failures);
            renderer.HandleProcessFailure(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive, CoreWebView2ProcessFailedReason.Unresponsive, 259);
            renderer.HandleProcessFailure(CoreWebView2ProcessFailedKind.BrowserProcessExited, CoreWebView2ProcessFailedReason.Unexpected, 1);
            Assert.AreEqual(1, failures);
            renderer.Dispose(); renderer.Dispose();
            renderer.HandleProcessFailure(CoreWebView2ProcessFailedKind.RenderProcessExited, CoreWebView2ProcessFailedReason.Unexpected, 1);
            Assert.AreEqual(1, failures);
            Assert.IsFalse(renderer.IsReady);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void DeveloperSignalUsesTheSameFailureThresholdAndHonorsPausedDelivery()
    {
        StaTest.Run(_ =>
        {
            using var renderer = new LyricsWebViewRenderer(new WebView2CompositionControl());
            var failures = 0;
            renderer.Failed += (_, _) => failures++;
            renderer.PauseDelivery();
            Assert.IsFalse(renderer.CanInjectDeveloperFailure(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive));
            renderer.ResumeDelivery();
            renderer.HandleProcessFailure(CoreWebView2ProcessFailedKind.GpuProcessExited, CoreWebView2ProcessFailedReason.Unexpected, 0, developerInjected: true);
            Assert.AreEqual(0, failures);
            renderer.HandleProcessFailure(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive, CoreWebView2ProcessFailedReason.Unresponsive, 0, developerInjected: true);
            Assert.AreEqual(0, failures);
            renderer.HandleProcessFailure(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive, CoreWebView2ProcessFailedReason.Unresponsive, 0, developerInjected: true);
            Assert.AreEqual(1, failures);
            Assert.IsFalse(renderer.CanInjectDeveloperFailure(CoreWebView2ProcessFailedKind.RenderProcessExited));
            return Task.CompletedTask;
        });
    }
}
