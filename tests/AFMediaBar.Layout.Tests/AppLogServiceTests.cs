using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>退出边界上的日志生命周期保护。/ Log lifecycle guards at the shutdown boundary.</summary>
[TestClass]
public sealed class AppLogServiceTests
{
    [TestMethod]
    public void WritesAfterDisposeAreIgnored()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var log = new AppLogService(directory);
            log.Dispose();

            log.Info("Test", "late info");
            log.Warn("Test", "late warning");
            log.Error("Test", "late error");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
