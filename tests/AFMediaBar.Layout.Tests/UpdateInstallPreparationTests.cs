// 验证真实本地验包、取消与交接状态机；安装环境和进程启动被替换，绝不启动真实安装程序。
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>安装包后台校验、取消与一次性交接的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class UpdateInstallPreparationTests
{
    [TestMethod]
    public void StartupVerificationKeepsDispatcherResponsiveAndLaunchesOnlyOnce()
    {
        StaTest.Run(async dispatcher =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var fixture = new Fixture(dispatcher, () =>
            {
                Assert.IsFalse(dispatcher.CheckAccess());
                entered.Set();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                return Allowed;
            });
            var installing = fixture.Service.TryLaunchPendingInstallOnStartupAsync();
            try
            {
                Assert.IsTrue(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
                Assert.IsFalse(installing.IsCompleted);
                var responsive = false;
                await dispatcher.InvokeAsync(() => responsive = true);
                Assert.IsTrue(responsive);
                Assert.IsFalse(await fixture.Service.TryLaunchPendingInstallOnStartupAsync());
            }
            finally { release.Set(); }
            Assert.IsTrue(await installing);
            Assert.AreEqual(1, fixture.Launches);
            Assert.IsFalse(await fixture.Service.TryLaunchPendingInstallOnStartupAsync());
            Assert.IsNull(fixture.Store.ReadPendingRecord());
            StringAssert.Contains(fixture.Arguments!, "/AUTORELAUNCH=1");
        });
    }

    [TestMethod]
    public void HashMismatchIsRejectedEvenWhenSizeAndTimestampStillMatch()
    {
        StaTest.Run(async dispatcher =>
        {
            using var fixture = new Fixture(dispatcher);
            var bytes = File.ReadAllBytes(fixture.Record.Path); bytes[0] ^= 1;
            File.WriteAllBytes(fixture.Record.Path, bytes);
            File.SetLastWriteTimeUtc(fixture.Record.Path, fixture.Record.ModifiedUtc.UtcDateTime);
            Assert.IsFalse(await fixture.Service.TryLaunchPendingInstallOnStartupAsync());
            Assert.AreEqual(0, fixture.Launches);
            Assert.IsFalse(File.Exists(fixture.Record.Path));
            Assert.IsNull(fixture.Store.ReadPendingRecord());
        });
    }

    [TestMethod]
    public void ExitOnlyConsumesPreparedPackageAndRechecksInstallConditions()
    {
        StaTest.Run(async dispatcher =>
        {
            var allowed = true;
            using var fixture = new Fixture(dispatcher, () => allowed ? Allowed : new(false, false, "blocked"));
            fixture.MakeReady();
            Assert.IsFalse(fixture.Service.TryLaunchPendingInstallOnExit(), "Normal exit must not inspect/install an unprepared package.");
            var restarts = 0;
            fixture.Service.RestartRequested += (_, _) => restarts++;
            Assert.IsTrue(await fixture.Service.RequestInstallAndExitAsync());
            Assert.AreEqual(1, restarts);
            Assert.ThrowsException<IOException>(() => File.WriteAllText(fixture.Record.Path, "changed"));
            Assert.ThrowsException<IOException>(() => File.Delete(fixture.Record.Path));
            Assert.IsFalse(await fixture.Service.RequestInstallAndExitAsync());
            Assert.IsFalse(fixture.Service.CurrentState.IsReady);
            allowed = false;
            Assert.IsFalse(fixture.Service.TryLaunchPendingInstallOnExit());
            Assert.AreEqual(0, fixture.Launches);
            Assert.IsNotNull(fixture.Store.ReadPendingRecord());
            // 交接被拒绝后租约已释放。
            using var writable = File.Open(fixture.Record.Path, FileMode.Open, FileAccess.Write, FileShare.None);
        });
    }

    [TestMethod]
    public void PreparedExitLaunchRetainsReadLeaseUntilProcessStartAndConsumesRecordOnce()
    {
        StaTest.Run(async dispatcher =>
        {
            using var fixture = new Fixture(dispatcher);
            fixture.MakeReady();
            fixture.Service.RestartRequested += (_, _) => { };
            Assert.IsTrue(await fixture.Service.RequestInstallAndExitAsync());
            Assert.IsTrue(fixture.Service.TryLaunchPendingInstallOnExit());
            Assert.IsFalse(fixture.Service.TryLaunchPendingInstallOnExit());
            Assert.AreEqual(1, fixture.Launches);
            Assert.IsNull(fixture.Store.ReadPendingRecord());
        });
    }

    [TestMethod]
    public void CancelAfterHashPreservesReadyPackageAndDoesNotRequestExit()
    {
        StaTest.Run(async dispatcher =>
        {
            Fixture? fixture = null;
            var checks = 0;
            using (fixture = new Fixture(dispatcher, () =>
            {
                if (Interlocked.Increment(ref checks) == 2) fixture!.Service.CancelDownload();
                return Allowed;
            }))
            {
                fixture.MakeReady();
                var restarts = 0;
                fixture.Service.RestartRequested += (_, _) => restarts++;
                Assert.IsFalse(await fixture.Service.RequestInstallAndExitAsync());
                Assert.AreEqual(0, restarts);
                Assert.AreEqual(0, fixture.Launches);
                Assert.IsTrue(fixture.Service.CurrentState.IsReady);
                Assert.AreEqual(fixture.Record, fixture.Store.ReadPendingRecord());
                using var writable = File.Open(fixture.Record.Path, FileMode.Open, FileAccess.Write, FileShare.None);
            }
        });
    }

    [TestMethod]
    public void DisposeWhilePreparingDropsLateResultsAndKeepsRecord()
    {
        StaTest.Run(async dispatcher =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var fixture = new Fixture(dispatcher, () =>
            {
                entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5))); return Allowed;
            });
            var task = fixture.Service.TryLaunchPendingInstallOnStartupAsync();
            try
            {
                Assert.IsTrue(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
                fixture.Service.Dispose(); fixture.Service.Dispose();
            }
            finally { release.Set(); }
            Assert.IsFalse(await task);
            Assert.AreEqual(0, fixture.Launches);
            Assert.AreEqual(fixture.Record, fixture.Store.ReadPendingRecord());
        });
    }

    [TestMethod]
    public void ChangedPendingRecordCannotBeOverwrittenOrInstalledByLateVerification()
    {
        StaTest.Run(async dispatcher =>
        {
            Fixture? fixture = null;
            var checks = 0;
            using (fixture = new Fixture(dispatcher, () =>
            {
                if (Interlocked.Increment(ref checks) == 1)
                    fixture!.Store.WritePendingRecord(fixture.Record with { Version = "1000.0" });
                return Allowed;
            }))
            {
                Assert.IsFalse(await fixture.Service.TryLaunchPendingInstallOnStartupAsync());
                Assert.AreEqual("1000.0", fixture.Store.ReadPendingRecord()!.Version);
                Assert.AreEqual(0, fixture.Launches);
            }
        });
    }

    [TestMethod]
    public void DisappearingPendingRecordReportsFailureWithoutRequestingExit()
    {
        StaTest.Run(async dispatcher =>
        {
            using var fixture = new Fixture(dispatcher);
            fixture.MakeReady();
            fixture.Store.ClearPendingRecord();
            var restarts = 0;
            fixture.Service.RestartRequested += (_, _) => restarts++;
            Assert.IsFalse(await fixture.Service.RequestInstallAndExitAsync());
            Assert.AreEqual(UpdatePhase.Failed, fixture.Service.CurrentState.Phase);
            Assert.IsFalse(string.IsNullOrWhiteSpace(fixture.Service.CurrentState.FailureReason));
            Assert.AreEqual(0, restarts);
            Assert.AreEqual(0, fixture.Launches);
        });
    }

    [TestMethod]
    public void UntrustedPathsStaleVersionsAndPortableCopiesNeverLaunch()
    {
        StaTest.Run(async dispatcher =>
        {
            using var fixture = new Fixture(dispatcher);
            var outside = Path.Combine(fixture.Directory, "..", "outside.exe");
            fixture.Store.WritePendingRecord(fixture.Record with { Path = outside });
            Assert.IsFalse(await fixture.Service.TryLaunchPendingInstallOnStartupAsync());
            fixture.Store.WritePendingRecord(fixture.Record with { Version = "0.0" });
            Assert.IsFalse(await fixture.Service.TryLaunchPendingInstallOnStartupAsync());
            Assert.AreEqual(0, fixture.Launches);
            using var portable = new Fixture(dispatcher, () => new(false, false, UpdateInstallPlanPolicy.PortableBlockedReason));
            Assert.IsFalse(await portable.Service.TryLaunchPendingInstallOnStartupAsync());
            Assert.AreEqual(0, portable.Launches);
            Assert.IsNotNull(portable.Store.ReadPendingRecord());
        });
    }

    private static UpdateInstallDecision Allowed => new(true, false, null);

    private sealed class Fixture : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "AFMediaBar-install-" + Guid.NewGuid().ToString("N"));
        internal UpdatePackageStore Store { get; }
        internal UpdateService Service { get; }
        internal UpdatePendingFileRecord Record { get; }
        internal int Launches;
        internal string? Arguments;
        private readonly InstallCoordinatorMutex _mutex = new();

        internal Fixture(Dispatcher dispatcher, Func<UpdateInstallDecision>? decision = null)
        {
            System.IO.Directory.CreateDirectory(Directory);
            Store = new UpdatePackageStore(Directory);
            var path = Path.Combine(Directory, "AFMediaBar-Setup-test.exe");
            var bytes = Enumerable.Range(0, 200000).Select(i => (byte)i).ToArray();
            File.WriteAllBytes(path, bytes);
            Record = new(path, "999.0", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length,
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
            Store.WritePendingRecord(Record);
            Service = new(new UpdateManifestClient(), new UpdatePackageDownloader(Store), Store,
                new InstalledApplicationProbe(), _mutex, dispatcher, decision ?? (() => Allowed), (file, arguments, _) =>
                {
                    Assert.IsFalse(dispatcher.CheckAccess(), "Process launch must not block the dispatcher.");
                    Assert.IsNull(Store.ReadPendingRecord());
                    Assert.ThrowsException<IOException>(() => File.WriteAllText(file, "replaced-after-hash"));
                    Arguments = arguments;
                    Interlocked.Increment(ref Launches);
                    return true;
                });
        }

        internal void MakeReady()
        {
            var ready = typeof(UpdateService).GetMethod("CreateReadyState", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(Service, [Record]);
            typeof(UpdateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Service, ready);
        }

        public void Dispose()
        {
            Service.Dispose(); _mutex.Dispose();
            System.IO.Directory.Delete(Directory, true);
        }
    }
}
