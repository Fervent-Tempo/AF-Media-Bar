// 日志文件被占用时，记录调用不能抛出，原文件不能被破坏，失败计数必须可观察。
using System.Diagnostics;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AppLogWriteFailureTests
{
    [TestMethod]
    public void LockedLogFileDoesNotThrowOrCorruptExistingContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-log-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "app.log");
        File.WriteAllText(path, "existing");
        try
        {
            using (var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var log = new AppLogService(directory))
            {
                log.Info("Test", "first");
                log.Error("Test", "second", new InvalidOperationException("boom"));

                var deadline = Stopwatch.StartNew();
                while (log.WriteFailures == 0 && deadline.Elapsed < TimeSpan.FromSeconds(5))
                    Thread.Sleep(20);

                Assert.IsTrue(log.WriteFailures > 0, "被占用的日志文件应记录为写入失败。");
                Assert.AreEqual("existing", File.ReadAllText(path), "写入失败不能覆盖或截断已有日志。");
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
