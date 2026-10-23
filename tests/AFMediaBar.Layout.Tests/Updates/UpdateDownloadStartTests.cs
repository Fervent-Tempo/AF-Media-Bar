// 回归：下载入口的"检查是否忙 + 置忙"必须原子，托盘与设置页两个入口不能各启动一轮下载。
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
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
            await using var fixture = new Fixture(dispatcher);
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Service.UpdateStateChanged += state =>
            {
                if (state.Phase == UpdatePhase.Failed) failed.TrySetResult();
            };

            fixture.Service.StartDownload();

            // 回环服务器保持响应在途，避免后台结束后合法的状态变化使中间阶段断言失效。
            Assert.AreEqual(UpdatePhase.Downloading, fixture.Service.CurrentState.Phase,
                "The entry point must claim the busy phase before the background loop starts.");

            fixture.Service.StartDownload();

            await fixture.RequestReceived.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(UpdatePhase.Downloading, fixture.Service.CurrentState.Phase);
            fixture.ReleaseResponse();
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.AreEqual(1, fixture.RequestCount, "Only one download request may reach the server.");
        }, TimeSpan.FromSeconds(35));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "AFMediaBar-download-" + Guid.NewGuid().ToString("N"));
        internal UpdatePackageStore Store { get; }
        internal UpdateService Service { get; }
        internal Task RequestReceived => _requestReceived.Task;
        internal int RequestCount => Volatile.Read(ref _requestCount);
        private readonly InstallCoordinatorMutex _mutex = new();
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _requestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _serverTask;
        private int _requestCount;

        internal Fixture(Dispatcher dispatcher)
        {
            System.IO.Directory.CreateDirectory(Directory);
            Store = new UpdatePackageStore(Directory);
            Service = new UpdateService(new UpdateManifestClient(), new UpdatePackageDownloader(Store), Store,
                new InstalledApplicationProbe(), _mutex, dispatcher, () => new UpdateInstallDecision(true, false, null),
                (_, _, _) => throw new InvalidOperationException("Installer launch is not expected in this test."));

            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = ServeAsync();
            var manifest = new UpdateManifest("9.9.9", null, null, [], null, null, null, false,
                [new UpdatePackageAsset($"http://127.0.0.1:{port}/AFMediaBar-Setup-9.9.9.exe", null, new string('A', 64))], null, null);
            SetField("_state", new UpdateState(UpdatePhase.Available, "1.0", manifest, 0d, null, null, false, false, null));
            SetField("_plan", (IReadOnlyList<UpdateDownloadSource>)[new UpdateDownloadSource(manifest.Packages[0].Url, "local", false)]);
        }

        private void SetField(string name, object value) =>
            typeof(UpdateService).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Service, value);

        internal void ReleaseResponse() => _releaseResponse.TrySetResult();

        private async Task ServeAsync()
        {
            var responses = new List<Task>();
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    responses.Add(RespondAsync(client));
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            finally
            {
                await Task.WhenAll(responses);
            }
        }

        private async Task RespondAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                    Interlocked.Increment(ref _requestCount);
                    _requestReceived.TrySetResult();
                    await _releaseResponse.Task.WaitAsync(_stop.Token);
                    await stream.WriteAsync("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), _stop.Token);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            _stop.Cancel();
            try
            {
                await _serverTask;
            }
            finally
            {
                _listener.Stop();
                _stop.Dispose();
                _mutex.Dispose();
                System.IO.Directory.Delete(Directory, true);
            }
        }
    }
}
