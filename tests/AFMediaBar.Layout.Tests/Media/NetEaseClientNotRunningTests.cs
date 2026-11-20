// 用真实日志检查网易云进程未运行和映像验证失败的区别。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NetEaseClientNotRunningTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(42, true)]
    public void WarnsOnlyWhenADiscoveredProcessCannotBeVerified(int processId, bool expectWarning)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"afmb-netease-{Guid.NewGuid():N}");
        try
        {
            using var log = new AppLogService(directory);
            log.Info("Test", "日志已启用");
            using var reader = new NetEaseMemoryReader((_, _) => ([], processId == 0 ? null : processId));

            var (info, attachedProcessId) = reader.Read(MediaSnapshot.Disconnected);

            Assert.IsNull(info);
            Assert.AreEqual(0, attachedProcessId);
            log.Flush(TimeSpan.FromSeconds(5));
            var text = File.ReadAllText(log.FilePath);
            Assert.AreEqual(expectWarning, text.Contains("operation=netease:attach", StringComparison.Ordinal));
            if (expectWarning)
                StringAssert.Contains(text, $"pid={processId}");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
