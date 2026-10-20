// 使用独立临时文件验证音量记录损坏与写入失败时的数据保留，不访问真实音频设备。
using AFMediaBar.Classes.Services.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Audio;

[TestClass]
public sealed class ApplicationVolumeMemoryStoreTests
{
    [TestMethod]
    public void CorruptMainFile_RecoversOfflineApplicationFromBackup_AndKeepsBackupAfterSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar.VolumeMemory.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ApplicationVolumeMemoryStore(directory);
            var application = Path.Combine(directory, "OfflinePlayer.exe");
            var silentApplication = Path.Combine(directory, "SilentPlayer.exe");
            store.Save(new Dictionary<string, int> { [application] = 40, [silentApplication] = 0 });
            store.Save(new Dictionary<string, int> { [application] = 60, [silentApplication] = 0 });
            var path = Path.Combine(directory, "application-volumes.json");
            File.WriteAllText(path, "{broken");

            var recoveredStore = new ApplicationVolumeMemoryStore(directory);
            var recovered = recoveredStore.Load();
            Assert.AreEqual(40, recovered[application.ToUpperInvariant()]);
            Assert.AreEqual(0, recovered[silentApplication]);
            Assert.IsFalse(File.Exists(application));

            recovered[application] = 80;
            recoveredStore.Save(recovered);
            Assert.AreEqual(80, new ApplicationVolumeMemoryStore(directory).Load()[application]);
            File.WriteAllText(path, "{broken again");
            Assert.AreEqual(40, new ApplicationVolumeMemoryStore(directory).Load()[application]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FailedReplace_KeepsPreviousVolume_AndRemovesTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AFMediaBar.VolumeMemory.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ApplicationVolumeMemoryStore(directory);
            var application = Path.Combine(directory, "Player.exe");
            store.Save(new Dictionary<string, int> { [application] = 40 });
            var path = Path.Combine(directory, "application-volumes.json");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsException<IOException>(() => store.Save(new Dictionary<string, int> { [application] = 60 }));
                Assert.AreEqual(40, new ApplicationVolumeMemoryStore(directory).Load()[application]);
                Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
            }
            store.Save(new Dictionary<string, int> { [application] = 60 });
            Assert.AreEqual(60, new ApplicationVolumeMemoryStore(directory).Load()[application]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
