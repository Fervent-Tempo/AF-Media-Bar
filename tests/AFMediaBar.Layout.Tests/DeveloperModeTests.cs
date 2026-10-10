// 验证开发者设置的持久化边界、用户默认快照和失效代际；恢复测试前的全局设置。
using System.Text.Json;
using System.Reflection;
using System.IO;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Diagnostics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass, DoNotParallelize]
public sealed class DeveloperModeTests
{
    [TestMethod]
    public void DeveloperSettingRoundTripsAndRejectsExplicitNull()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Assert.IsFalse(JsonSerializer.Deserialize<AppSettings>("{}", options)!.DeveloperModeEnabled);
        Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<AppSettings>("{\"developerModeEnabled\":null}", options));
        var source = new AppSettings { DeveloperModeEnabled = true };
        Assert.IsTrue(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source.Clone(), options), options)!.DeveloperModeEnabled);
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-Developer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var persistence = new SettingsPersistenceService(directory);
            var read = typeof(SettingsPersistenceService).GetMethod("ReadEnvelope", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{\"schemaVersion\":2,\"settings\":{}}");
            Assert.IsFalse(((AppSettings)read.Invoke(persistence, [path])!).DeveloperModeEnabled);
            File.WriteAllText(path, "{\"schemaVersion\":2,\"settings\":{\"developerModeEnabled\":null}}");
            Assert.IsInstanceOfType<JsonException>(Assert.ThrowsException<TargetInvocationException>(() => read.Invoke(persistence, [path])).InnerException);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void ResetAndDisableInvalidateOldRequestsWithoutOpeningToolsAutomatically()
    {
        var previous = SettingsManager.Current;
        var defaults = SettingsManager.UserDefaults;
        try
        {
            SettingsManager.Current = new AppSettings();
            SettingsManager.SetUserDefaults(null);
            using var mode = new DeveloperModeService();
            var opens = 0;
            mode.OpenRequested += (_, _) => opens++;
            mode.OpenTools();
            mode.SetEnabled(true);
            Assert.AreEqual(0, opens);
            var token = mode.SessionToken;
            var generation = mode.Generation;
            mode.OpenTools();
            Assert.AreEqual(1, opens);
            SettingsManager.ResetAll();
            Assert.IsFalse(mode.IsEnabled);
            Assert.IsTrue(token.IsCancellationRequested);
            Assert.IsTrue(mode.Generation > generation);
            SettingsManager.SetUserDefaults(new AppSettings { DeveloperModeEnabled = true });
            SettingsManager.ResetAll();
            Assert.IsTrue(mode.IsEnabled);
            Assert.AreEqual(1, opens);
            mode.Dispose();
            mode.Dispose();
            Assert.IsFalse(mode.IsEnabled);
        }
        finally
        {
            SettingsManager.SetUserDefaults(defaults);
            SettingsManager.Current = previous;
        }
    }
}
