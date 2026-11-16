// 验证设置读取优化保留历史空值、文本编码和版本隔离；只读写测试拥有的临时目录。
using System.Text;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>设置快照读取的兼容、编码和无效文件隔离回归。</summary>
[TestClass]
public sealed class SettingsEnvelopeLoadingTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void LoadingPreservesNullableGeometryUnicodeAndExistingEncoding(bool nullGeometry, bool utf16)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-envelope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var json = $$$"""
                {"schemaVersion":{{{SettingsPersistenceService.CurrentSchemaVersion}}},"settings":{
                "layoutLengthScalePercent":{{{(nullGeometry ? "null" : "125")}}},
                "layoutThicknessScalePercent":{{{(nullGeometry ? "null" : "75")}}},
                "taskbarBarCrossAxisOffsetDip":{{{(nullGeometry ? "null" : "-3")}}},
                "dynamicIslandLeft":null,"dynamicIslandTop":null,
                "taskbarTargetMonitorDeviceId":"显示器🌈",
                "spectrumComponent":{"captureSource":"AllApplications"}
                }
                }
                """;
            var path = Path.Combine(directory, "user-defaults.json");
            File.WriteAllText(path, json, utf16 ? Encoding.Unicode : new UTF8Encoding(false));
            using var persistence = new SettingsPersistenceService(directory);
            var loaded = persistence.LoadUserDefaults();
            Assert.IsNotNull(loaded);
            var defaults = new AppSettings();
            Assert.AreEqual(nullGeometry ? defaults.LayoutLengthScalePercent : 125, loaded.LayoutLengthScalePercent);
            Assert.AreEqual(nullGeometry ? defaults.LayoutThicknessScalePercent : 75, loaded.LayoutThicknessScalePercent);
            Assert.AreEqual(nullGeometry ? defaults.TaskbarBarCrossAxisOffsetDip : -3, loaded.TaskbarBarCrossAxisOffsetDip);
            Assert.IsNull(loaded.DynamicIslandLeft);
            Assert.IsNull(loaded.DynamicIslandTop);
            Assert.IsNull(loaded.TaskbarTargetMonitorDeviceId);
            CollectionAssert.AreEqual(new[] { "显示器🌈" }, loaded.TaskbarTargetMonitorDeviceIds!.ToArray());
            Assert.AreEqual(SpectrumCaptureSource.AllApplications, loaded.SpectrumComponent.CaptureSource);
            Assert.AreEqual(SpectrumStyle.Area, loaded.SpectrumComponent.Style, "缺 style 字段时落到新的内置默认面积曲线。");
            Assert.AreEqual(SpectrumStyle.Area, new AppSettings().SpectrumComponent.Style, "内置默认样式应为面积曲线。");
            Assert.IsFalse(loaded.SpectrumComponent.ShowPeakMarkers, "缺 showPeakMarkers 字段时默认关闭峰值标记。");
            Assert.AreEqual(json, File.ReadAllText(path), "加载不应改写用户默认快照。");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("\"Dots\"", SpectrumStyle.Dots)]
    [DataRow("\"Area\"", SpectrumStyle.Area)]
    [DataRow("\"Bars\"", SpectrumStyle.Bars)]
    [DataRow("\"unknown\"", SpectrumStyle.Area)]
    [DataRow("999", SpectrumStyle.Area)]
    public void ExplicitOrInvalidStyleLoadsAsDeclared(string styleJson, SpectrumStyle expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-style-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var json = "{\"schemaVersion\":" + SettingsPersistenceService.CurrentSchemaVersion +
                       ",\"settings\":{\"spectrumComponent\":{\"style\":" + styleJson + "}}}";
            var path = Path.Combine(directory, "user-defaults.json");
            File.WriteAllText(path, json);
            using var persistence = new SettingsPersistenceService(directory);
            var loaded = persistence.LoadUserDefaults();
            Assert.IsNotNull(loaded);
            Assert.AreEqual(expected, loaded.SpectrumComponent.Style);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("not-json", false)]
    [DataRow("{\"schemaVersion\":999,\"settings\":{}}", true)]
    public void InvalidDefaultsAreRejectedAndOnlyUnsupportedVersionsAreQuarantined(string json, bool quarantined)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-envelope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "user-defaults.json");
            File.WriteAllText(path, json);
            using var persistence = new SettingsPersistenceService(directory);
            Assert.IsNull(persistence.LoadUserDefaults());
            Assert.AreEqual(!quarantined, File.Exists(path));
            Assert.AreEqual(1, Directory.GetFiles(directory).Length, "无效文件证据不能丢失。");
        }
        finally { Directory.Delete(directory, true); }
    }
}
