// 验证更新目录只清理已知残留：保留待安装包，删除 .part/旧安装包/过期日志，不碰其他文件。
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class UpdatePackageStoreCleanupTests
{
    [TestMethod]
    public void CleanupKeepsPendingInstallerRemovesPartFilesAndOldLogsWithoutTouchingOtherFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-update-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new UpdatePackageStore(directory);
            var keep = Path.Combine(directory, "AFMediaBar-Setup-v1.2.0-win-x64.exe");
            var old = Path.Combine(directory, "AFMediaBar-Setup-v1.1.0-win-x64.exe");
            var part = keep + ".part";
            var freshLog = Path.Combine(directory, "install-v1.2.0.log");
            var oldLog = Path.Combine(directory, "install-v1.1.0.log");
            var unrelated = Path.Combine(directory, "notes.txt");
            var pending = store.PendingRecordPath;

            File.WriteAllText(keep, "pending");
            File.WriteAllText(old, "old");
            File.WriteAllText(part, "unfinished");
            File.WriteAllText(freshLog, "fresh");
            File.WriteAllText(oldLog, "old log");
            File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddDays(-(UpdatePackageStore.LogRetentionDays + 1)));
            File.WriteAllText(unrelated, "keep me");
            File.WriteAllText(pending, "{\"version\":\"1.2.0\"}");

            store.CleanupObsolete(keep);

            Assert.IsTrue(File.Exists(keep), "待安装包必须保留。");
            Assert.IsFalse(File.Exists(old), "非待安装的旧安装包应删除。");
            Assert.IsFalse(File.Exists(part), "未完成的 .part 必须删除，避免下次误用。");
            Assert.IsTrue(File.Exists(freshLog), "保留期内的安装日志不能删。");
            Assert.IsFalse(File.Exists(oldLog), "超过保留期的安装日志应删除。");
            Assert.IsTrue(File.Exists(unrelated), "清理只处理已知模式，不能碰其他文件。");
            Assert.AreEqual("{\"version\":\"1.2.0\"}", File.ReadAllText(pending), "清理不能改写待安装记录。");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
