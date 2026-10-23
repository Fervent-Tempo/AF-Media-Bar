// 回归：下载入口的"检查是否忙 + 置忙"必须原子，托盘与设置页两个入口不能各启动一轮下载。
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>下载入口的并发窗口回归：放行判断与置忙必须在同一把锁内完成。</summary>
[TestClass]
[DoNotParallelize]
public sealed class UpdateDownloadStartTests
{
    [TestMethod]
    public void SecondStartDownloadIsRejectedWhileTheFirstRoundOwnsTheState()
    {
        StaTest.Run(async dispatcher =>
        {
            using var fixture = new Fixture(dispatcher);
            var downloadingPublished = 0;
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Service.UpdateStateChanged += state =>
            {
                if (state.Phase == UpdatePhase.Downloading) Interlocked.Increment(ref downloadingPublished);
                if (state.Phase == UpdatePhase.Failed) failed.TrySetResult();
            };

            fixture.Service.StartDownload();

            // 旧实现只读取 IsBusy 就返回，下载循环由线程池稍后才置忙，入口处看到的仍是 Available。
            // The old implementation only read IsBusy and returned; the loop claimed the phase later on the thread
            // pool, so this entry point still observed Available.
            Assert.AreEqual(UpdatePhase.Downloading, fixture.Service.CurrentState.Phase,
                "The entry point must claim the busy phase before the background loop starts.");

            // 第二次进入（另一个入口）必须被同一次置忙挡住。
            // The second entry (the other surface) must be rejected by that same claim.
            fixture.Service.StartDownload();

            await failed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await dispatcher.InvokeAsync(() => { });

            Assert.AreEqual(1, Volatile.Read(ref downloadingPublished), "Only one download round may be started.");
        });
    }

    private sealed class Fixture : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "AFMediaBar-download-" + Guid.NewGuid().ToString("N"));
        internal UpdatePackageStore Store { get; }
        internal UpdateService Service { get; }
        private readonly InstallCoordinatorMutex _mutex = new();

        internal Fixture(Dispatcher dispatcher)
        {
            System.IO.Directory.CreateDirectory(Directory);
            Store = new UpdatePackageStore(Directory);
            Service = new UpdateService(new UpdateManifestClient(), new UpdatePackageDownloader(Store), Store,
                new InstalledApplicationProbe(), _mutex, dispatcher, () => new UpdateInstallDecision(true, false, null),
                (_, _, _) => throw new InvalidOperationException("Installer launch is not expected in this test."));

            var manifest = new UpdateManifest("9.9.9", null, null, [], null, null, null, false,
                [new UpdatePackageAsset(UnreachableUrl(), null, new string('A', 64))], null, null);
            SetField("_state", new UpdateState(UpdatePhase.Available, "1.0", manifest, 0d, null, null, false, false, null));
            SetField("_plan", (IReadOnlyList<UpdateDownloadSource>)[new UpdateDownloadSource(manifest.Packages[0].Url, "local", false)]);
        }

        private void SetField(string name, object value) =>
            typeof(UpdateService).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Service, value);

        // 取一个刚释放的本地端口：连接会被立刻拒绝，下载循环无需访问网络即可走到失败分支。
        // Take a just-released local port: the connection is refused immediately, so the loop reaches its failure
        // branch without touching the network.
        private static string UnreachableUrl()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return $"http://127.0.0.1:{port}/AFMediaBar-Setup-9.9.9.exe";
        }

        public void Dispose()
        {
            Service.Dispose();
            _mutex.Dispose();
            System.IO.Directory.Delete(Directory, true);
        }
    }
}
