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
            Assert.AreEqual(json, File.ReadAllText(path), "加载不应改写用户默认快照。");
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
