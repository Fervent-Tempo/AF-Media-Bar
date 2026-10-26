// 验证字节上限、历史保留及跨会话追加，防止诊断文件无界增长或覆盖旧现场。
using System.Text;
using AFMediaBar.Classes.Services.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>滚动文件的容量和历史连续性。</summary>
[TestClass]
public sealed class RollingLogFileTests
{
    [TestMethod]
    public void RotationBoundsUtf8FilesAndKeepsNewestRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-rotation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "app.log");
            using (var file = new RollingLogFile(path, 256, 3))
            {
                for (var index = 0; index < 30; index++) file.Append($"entry-{index:D2} 音乐记录 " + new string('中', 30));
                file.Append(new string('中', 2000));
                file.Append("newest-entry");
            }
            var files = Directory.GetFiles(directory);
            Assert.AreEqual(3, files.Length);
            foreach (var file in files)
            {
                Assert.IsTrue(new FileInfo(file).Length <= 256);
                _ = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file));
            }
            StringAssert.Contains(File.ReadAllText(path), "newest-entry");
            using (var reopened = new RollingLogFile(path, 256, 3)) reopened.Append("next-session");
            StringAssert.Contains(File.ReadAllText(path), "newest-entry");
            StringAssert.Contains(File.ReadAllText(path), "next-session");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void FlushAllowsReadingAndSeparatesAnIncompletePreviousRecord()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar-rotation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "performance.jsonl");
            File.WriteAllText(path, "incomplete");
            using var file = new RollingLogFile(path, 256, 3);
            file.Append("new-record");
            file.Flush();
            CollectionAssert.AreEqual(new[] { "incomplete", "new-record" }, File.ReadAllLines(path));
            file.Append("after-flush");
            file.Flush();
            StringAssert.Contains(File.ReadAllText(path), "after-flush");
        }
        finally { Directory.Delete(directory, true); }
    }
}
