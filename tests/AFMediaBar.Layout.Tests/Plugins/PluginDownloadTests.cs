// 覆盖镜像损坏、错误文件、取消竞态与路径隔离；只替换 HTTP，哈希和落盘使用真实实现。
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Plugins;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Plugins;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class PluginDownloadTests
{
    [TestMethod]
    public async Task FailedAndCorruptMirrorsFallBackToOfficialAndExistingFilesAreReverified()
    {
        await using var fixture = new Fixture((request, _) => Task.FromResult(request.RequestUri!.Host switch
        {
            "mirror.invalid" => Response(request, HttpStatusCode.ServiceUnavailable, []),
            "broken.invalid" => Response(request, HttpStatusCode.OK, [9, 9, 9, 9]),
            _ => Response(request, HttpStatusCode.OK, Fixture.Bytes)
        }));
        Assert.IsTrue(fixture.Start());
        await fixture.FinishAsync();
        var completed = fixture.State;
        Assert.AreEqual(PluginDownloadPhase.Completed, completed.Phase);
        Assert.AreEqual("github.com", completed.SourceHost);
        CollectionAssert.AreEqual(Fixture.Bytes, await File.ReadAllBytesAsync(completed.FilePath!));
        CollectionAssert.AreEqual(new[] { "mirror.invalid", "broken.invalid", "github.com" }, fixture.Requests.ToArray());
        Assert.AreEqual(0, Directory.GetFiles(fixture.Root, "*.partial", SearchOption.AllDirectories).Length);

        Assert.IsTrue(fixture.Start());
        await fixture.FinishAsync();
        Assert.AreEqual(3, fixture.Requests.Count, "A verified local file needs no network request.");

        await File.WriteAllBytesAsync(completed.FilePath!, [0, 0, 0, 0]);
        Assert.IsTrue(fixture.Start());
        await fixture.FinishAsync();
        Assert.AreEqual(6, fixture.Requests.Count, "A same-size local corruption must force another download.");
        CollectionAssert.AreEqual(Fixture.Bytes, await File.ReadAllBytesAsync(completed.FilePath!));
    }

    [TestMethod]
    public async Task AllCorruptResponsesLeaveNoCompletedOrTemporaryFile()
    {
        await using var fixture = new Fixture((request, _) => Task.FromResult(Response(request, HttpStatusCode.OK, [0, 0, 0, 0])));
        Assert.IsTrue(fixture.Start());
        await fixture.FinishAsync();
        Assert.AreEqual(PluginDownloadPhase.Failed, fixture.State.Phase);
        Assert.AreEqual("Plugins.Error.Integrity", fixture.State.FailureKey);
        Assert.AreEqual(0, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task DuplicateStartIsRejectedAndCanceledLateResponseDoesNotTryAnotherSource()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new Fixture(async (request, _) =>
        {
            entered.TrySetResult();
            // 故意忽略取消，验证 HTTP 晚到结果不能落盘或启动下一来源。
            await release.Task;
            return Response(request, HttpStatusCode.OK, Fixture.Bytes);
        });
        try
        {
            Assert.IsTrue(fixture.Start());
            Assert.IsFalse(fixture.Start());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Service.Cancel();
            release.TrySetResult();
            await fixture.FinishAsync();
            Assert.AreEqual(PluginDownloadPhase.Canceled, fixture.State.Phase);
            Assert.IsFalse(fixture.Service.IsBusy);
            Assert.AreEqual(1, fixture.Requests.Count);
            Assert.AreEqual(0, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Length);
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    public async Task CancelDuringStreamingRemovesPartialFileAndStopsFallback()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new Fixture((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StreamContent(new PausedStream(reading))
        }));
        Assert.IsTrue(fixture.Start());
        try
        {
            await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, Directory.GetFiles(fixture.Root, "*.partial", SearchOption.AllDirectories).Length);
        }
        finally { fixture.Service.Cancel(); }
        await fixture.FinishAsync();
        Assert.AreEqual(PluginDownloadPhase.Canceled, fixture.State.Phase);
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.AreEqual(0, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task StoppingIsIdempotentAndSuppressesLateNotifications()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new Fixture(async (request, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return Response(request, HttpStatusCode.OK, Fixture.Bytes);
        });
        var notifications = 0;
        fixture.Service.StateChanged += () => Interlocked.Increment(ref notifications);
        try
        {
            Assert.IsTrue(fixture.Start());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var stop = fixture.Service.StopAsync(CancellationToken.None);
            var before = Volatile.Read(ref notifications);
            Assert.IsFalse(fixture.Start());
            release.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Service.StopAsync(CancellationToken.None);
            fixture.Service.Dispose();
            fixture.Service.Dispose();
            Assert.AreEqual(before, Volatile.Read(ref notifications));
            Assert.AreEqual(1, fixture.Requests.Count);
            Assert.AreEqual(0, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Length);
        }
        finally { release.TrySetResult(); }
    }

    [DataTestMethod]
    [DataRow("../escape", "v1", "plugin.dll")]
    [DataRow("valid", "..", "plugin.dll")]
    [DataRow("valid", "v1", "..\\outside.dll")]
    [DataRow("valid", "v1", "file.dll:stream")]
    [DataRow("valid", "v1", "NUL.dll")]
    public async Task InvalidAttachmentPathsAreRejectedBeforeAnyIo(string id, string version, string fileName)
    {
        await using var fixture = new Fixture((_, _) => throw new AssertFailedException("Invalid paths must not reach HTTP."));
        var asset = fixture.Asset with { FileName = fileName };
        var plugin = fixture.Plugin with { Id = id, Version = version, Assets = [asset] };
        Assert.ThrowsException<ArgumentException>(() => fixture.Service.TryStartDownload(plugin, asset));
        Assert.IsFalse(Directory.Exists(fixture.Root));
    }

    [TestMethod]
    public void DisposedPageIgnoresQueuedResultsAndReopenedPageReadsApplicationDownloadState()
    {
        StaTest.Run(async dispatcher =>
        {
            // 保留目录项的身份，仅以本地夹具替换附件字节与摘要，避免测试依赖真实上游下载。
            var original = PluginCatalog.Entries.Single(plugin => plugin.Id == "foobar");
            var descriptor = original with { Assets = original.Assets.Reverse().ToArray() };
            await using var fixture = new Fixture((request, _) => Task.FromResult(Response(request, HttpStatusCode.OK, Fixture.Bytes)), descriptor);
            using var localization = new LocalizationService();
            var model = new PluginCenterViewModel(fixture.Service, localization, dispatcher);
            model.Activate();
            var changes = 0;
            model.PropertyChanged += (_, _) => changes++;
            Assert.IsTrue(fixture.Start());
            model.Deactivate();
            model.Dispose();
            var before = changes;
            await fixture.FinishAsync();
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(before, changes, "Queued callbacks must not touch a disposed page.");
            Assert.AreEqual(PluginDownloadPhase.Completed, fixture.State.Phase, "Leaving a page must not cancel application work.");
            using var reopened = new PluginCenterViewModel(fixture.Service, localization, dispatcher);
            reopened.Activate();
            var completed = reopened.Plugins.Single(card => card.Descriptor.Id == "foobar");
            Assert.AreEqual("x64", completed.SelectedAsset?.Architecture);
            Assert.IsTrue(completed.CanReveal);
        });
    }

    [TestMethod]
    public void ReopenedPageRestoresActiveArchitectureAndCanCancelTheOngoingDownload()
    {
        StaTest.Run(async dispatcher =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = PluginCatalog.Entries.Single(plugin => plugin.Id == "foobar");
            await using var fixture = new Fixture(async (request, _) =>
            {
                entered.TrySetResult();
                await release.Task;
                return Response(request, HttpStatusCode.OK, Fixture.Bytes);
            }, original with { Assets = original.Assets.Reverse().ToArray() });
            try
            {
                Assert.IsTrue(fixture.Start());
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                using var localization = new LocalizationService();
                using var reopened = new PluginCenterViewModel(fixture.Service, localization, dispatcher);
                reopened.Activate();
                var card = reopened.Plugins.Single(item => item.Descriptor.Id == "foobar");
                Assert.AreEqual("x64", card.SelectedAsset?.Architecture);
                Assert.IsTrue(card.HasProgress);
                Assert.IsTrue(card.CancelCommand.CanExecute(null));
                card.CancelCommand.Execute(null);
                release.TrySetResult();
                await fixture.FinishAsync();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(card.HasProgress);
                Assert.IsTrue(card.DownloadCommand.CanExecute(null));
                Assert.AreEqual(PluginDownloadPhase.Canceled, fixture.State.Phase);
            }
            finally { release.TrySetResult(); }
        });
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, HttpStatusCode code, byte[] bytes) =>
        new(code) { RequestMessage = request, Content = new ByteArrayContent(bytes) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }

    private sealed class PausedStream(TaskCompletionSource reading) : Stream
    {
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_first)
            {
                _first = false;
                new byte[] { 1, 2 }.CopyTo(buffer);
                return 2;
            }
            reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal static readonly byte[] Bytes = [1, 2, 3, 4];
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "AFMB-Plugins-" + Guid.NewGuid().ToString("N"));
        internal List<string> Requests { get; } = [];
        internal PluginDownloadService Service { get; }
        internal PluginDescriptor Plugin { get; }
        internal PluginAsset Asset { get; }
        internal PluginDownloadState State => Service.GetState(Plugin, Asset);
        private readonly HttpClient _client;

        internal Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, PluginDescriptor? descriptor = null)
        {
            Asset = new("x86", descriptor?.Assets[0].FileName ?? "plugin.dll", "https://github.com/example/plugin/releases/download/v1/plugin.dll",
                Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)));
            Plugin = descriptor is null
                ? new("test", "Test plugin", "Test player", "Test author", "v1", "https://github.com/example/plugin", false, [Asset])
                : descriptor with { Assets = [Asset] };
            _client = new HttpClient(new Handler((request, token) =>
            {
                Requests.Add(request.RequestUri!.Host);
                return respond(request, token);
            }));
            Service = new(directoryPath: Root, client: _client,
                sourcePlan: asset => ["https://mirror.invalid/plugin.dll", "https://broken.invalid/plugin.dll", asset.Url]);
        }

        internal bool Start() => Service.TryStartDownload(Plugin, Asset);
        internal Task FinishAsync() => Service.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            Service.Dispose();
            _client.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
