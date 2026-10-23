// 检查实际 Web 呈现消息，避免仅增加记录字段而遗漏序列化。
using System.Reflection;
using System.Text.Json;
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Web.WebView2.Wpf;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LyricsWebCompactProtocolTests
{
    private static JsonDocument ParseMessage(string script) =>
        JsonDocument.Parse(script[script.IndexOf('{')..(script.LastIndexOf('}') + 1)]);

    [TestMethod]
    public void ColorTransitionSupports220MillisecondsWithoutReplayingOnIdenticalStyle()
    {
        StaTest.Run(_ =>
        {
            using var renderer = new LyricsWebViewRenderer(new WebView2CompositionControl());
            var style = new LyricsWebStyle("Segoe UI", "Segoe UI", "Microsoft YaHei UI", 18, 400, 0, 0,
                "Center", "white", "gray", "gray", "gray", "white", "none");
            var pending = typeof(LyricsWebViewRenderer).GetField("_pendingStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;
            renderer.ApplyStyle(style, TimeSpan.FromMilliseconds(220));
            using (var first = ParseMessage((string)pending.GetValue(renderer)!))
                Assert.AreEqual(0, first.RootElement.GetProperty("payload").GetProperty("colorTransitionMilliseconds").GetDouble());

            // 只模拟文档就绪，不创建网络、播放器或 WebView2 浏览器进程。
            typeof(LyricsWebViewRenderer).GetField("_documentReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, true);
            var updated = style with { PrimaryColor = "black" };
            renderer.ApplyStyle(updated, TimeSpan.FromMilliseconds(220));
            var message = (string)pending.GetValue(renderer)!;
            using (var color = ParseMessage(message))
                Assert.AreEqual(220, color.RootElement.GetProperty("payload").GetProperty("colorTransitionMilliseconds").GetDouble());
            renderer.ApplyStyle(updated);
            Assert.AreEqual(message, pending.GetValue(renderer));
            renderer.ApplyStyle(updated, TimeSpan.Zero, finishColorTransition: true);
            using (var stopped = ParseMessage((string)pending.GetValue(renderer)!))
                Assert.AreEqual(0, stopped.RootElement.GetProperty("payload").GetProperty("colorTransitionMilliseconds").GetDouble());
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void CompactGeometryIsSentAndCanBeRestoredWithoutRecreatingRenderer()
    {
        StaTest.Run(_ =>
        {
            using var renderer = new LyricsWebViewRenderer(new WebView2CompositionControl());
            var style = new LyricsWebStyle("Segoe UI", "Segoe UI", "Microsoft YaHei UI", 18, 400, 0, 0,
                "Center", "white", "gray", "gray", "gray", "white", "none", Compact: true);
            var pending = typeof(LyricsWebViewRenderer).GetField("_pendingStyle", BindingFlags.Instance | BindingFlags.NonPublic)!;
            renderer.ApplyStyle(style);
            using (var message = ParseMessage((string)pending.GetValue(renderer)!))
                Assert.IsTrue(message.RootElement.GetProperty("payload").GetProperty("compact").GetBoolean());
            renderer.ApplyStyle(style with { Compact = false });
            using (var message = ParseMessage((string)pending.GetValue(renderer)!))
                Assert.IsFalse(message.RootElement.GetProperty("payload").GetProperty("compact").GetBoolean());
            return Task.CompletedTask;
        });
    }
}
