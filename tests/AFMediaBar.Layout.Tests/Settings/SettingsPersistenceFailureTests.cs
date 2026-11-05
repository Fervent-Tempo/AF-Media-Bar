// 验证设置原子替换失败时旧文件与内存值都不被破坏，并清理未提交的临时文件。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>设置写入失败时不能丢旧配置，也不能留下临时文件。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsPersistenceFailureTests
{
    [TestMethod]
    public void FailedReplaceKeepsOldFileAndMemoryValueAndCleansTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-settings-failure-" + Guid.NewGuid().ToString("N"));
        var previous = SettingsManager.Current;
        Directory.CreateDirectory(directory);
        try
        {
            using var persistence = new SettingsPersistenceService(directory, TimeSpan.FromMinutes(10));
            persistence.Initialize();

            SettingsManager.Current.LyricsEnabled = false;
            persistence.Flush();
            var path = Path.Combine(directory, "settings.json");
            var original = File.ReadAllText(path);

            // 允许读、拒绝写/删除，模拟文件被占用或替换失败；不能改用只读目录，
            // 因为 Windows 对目录的只读属性并不阻止创建文件。
            using (var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                SettingsManager.Current.LyricsEnabled = true;
                persistence.Flush();

                Assert.AreEqual(original, File.ReadAllText(path), "替换失败时旧 settings.json 必须保持完整。");
                Assert.IsTrue(SettingsManager.Current.LyricsEnabled, "写入失败不能回滚内存中的用户设置。");
            }

            Assert.AreEqual(0, Directory.GetFiles(directory, "settings.*.tmp").Length,
                "写入失败后不能留下未提交的临时文件。");

            persistence.Flush();
            Assert.AreNotEqual(original, File.ReadAllText(path), "释放占用后下一次保存必须能成功。");
        }
        finally
        {
            SettingsManager.Current = previous;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
