// 验证重启门禁和命令构造；不重启应用，只启动并清理测试自己创建的等待进程。
using System.Diagnostics;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services.Startup;
using AFMediaBar.Views.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>普通重启的交接、取消、参数和重复请求回归。</summary>
[TestClass]
public sealed class ApplicationRestartTests
{
    [TestMethod]
    public void ConfirmationRequestsMergeAndCancelBeforeOpeningAWindow()
    {
        StaTest.Run(async _ =>
        {
            // 取消发生在创建窗口前，外观服务不会被调用。
            using var confirmation = new RestartConfirmationService(null!);
            using var cancellation = new CancellationTokenSource();
            var first = confirmation.ConfirmAsync("Restart.Failed", cancellation.Token);
            Assert.AreSame(first, confirmation.ConfirmAsync("Restart.Failed"));
            cancellation.Cancel();
            Assert.IsFalse(await first);
            var next = confirmation.ConfirmAsync("Restart.Failed");
            confirmation.Dispose(); confirmation.Dispose();
            Assert.IsFalse(await next);
            Assert.IsFalse(await confirmation.ConfirmAsync("Restart.Failed"));
        });
    }

    [TestMethod]
    public void CommandPreservesOrdinaryArgumentsAndReplacesOldHandoff()
    {
        var handoff = new RestartHandoff(12, 42, @"Local\AFMediaBar.Restart." + Guid.NewGuid().ToString("N"));
        var start = RestartProcessHandoff.BuildStartInfo(@"C:\Program Files\dotnet\dotnet.exe", @"E:\Test App\AFMediaBar.dll",
            @"E:\Test App", ["arg with spaces", "--afmb-restart=old"], handoff);
        Assert.AreEqual(@"E:\Test App\AFMediaBar.dll", start.ArgumentList[0]);
        Assert.AreEqual("arg with spaces", start.ArgumentList[1]);
        Assert.AreEqual(3, start.ArgumentList.Count);
        Assert.IsTrue(RestartProcessHandoff.TryParse(start.ArgumentList.Skip(1).ToArray(), out var parsed));
        Assert.AreEqual(handoff, parsed);
        Assert.IsFalse(RestartProcessHandoff.TryParse([start.ArgumentList[2], start.ArgumentList[2]], out _));
        Assert.IsFalse(RestartProcessHandoff.TryParse(["--afmb-restart=1:2:other-event"], out _));
        var executable = RestartProcessHandoff.BuildStartInfo(@"E:\App\AFMediaBar.exe", "", @"E:\App", [], handoff);
        Assert.AreEqual(1, executable.ArgumentList.Count);
    }

    [TestMethod]
    public async Task OnlyConfirmedHandoffRequestsExitAndKeepsItsReservation()
    {
        var released = 0;
        var preparation = new TaskCompletionSource<RestartPreparation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new ApplicationRestartService(() => new Release(() => released++), _ => preparation.Task, null);
        var exits = 0;
        service.RestartRequested += (_, _) => exits++;
        var first = service.RequestRestartAsync();
        Assert.AreEqual(ApplicationRestartResult.Busy, await service.RequestRestartAsync());
        Assert.AreEqual(0, exits);
        preparation.SetResult(new RestartPreparation(() => { }, () => throw new AssertFailedException("Committed child must survive")));
        Assert.AreEqual(ApplicationRestartResult.Requested, await first);
        Assert.AreEqual(1, exits);
        Assert.AreEqual(0, released);
        Assert.AreEqual(ApplicationRestartResult.Busy, await service.RequestRestartAsync());
        service.Dispose();
        Assert.AreEqual(1, released);
    }

    [TestMethod]
    public async Task FailureCancellationAndInstallBusyNeverRequestExit()
    {
        var released = 0;
        using var failed = new ApplicationRestartService(() => new Release(() => released++), _ => Task.FromResult<RestartPreparation?>(null), null);
        var exits = 0;
        failed.RestartRequested += (_, _) => exits++;
        Assert.AreEqual(ApplicationRestartResult.Failed, await failed.RequestRestartAsync());
        Assert.AreEqual(1, released);
        using var busy = new ApplicationRestartService(() => null, _ => throw new AssertFailedException("Must not launch"), null);
        busy.RestartRequested += (_, _) => exits++;
        Assert.AreEqual(ApplicationRestartResult.Busy, await busy.RequestRestartAsync());
        using var cancellation = new CancellationTokenSource();
        using var canceled = new ApplicationRestartService(() => new Release(() => released++), async token =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        }, null);
        canceled.RestartRequested += (_, _) => exits++;
        Assert.AreEqual(ApplicationRestartResult.Canceled, await canceled.RequestRestartAsync(cancellation.Token));
        Assert.AreEqual(2, released);
        Assert.AreEqual(0, exits);
    }

    [TestMethod]
    public async Task CancellationAfterReadyAbortsTheUncommittedPreparation()
    {
        using var cancellation = new CancellationTokenSource();
        var aborted = 0;
        using var service = new ApplicationRestartService(() => new Release(() => { }), _ =>
        {
            cancellation.Cancel();
            return Task.FromResult<RestartPreparation?>(new RestartPreparation(() => { }, () => { aborted++; return Task.CompletedTask; }));
        }, null);
        service.RestartRequested += (_, _) => Assert.Fail("Canceled request must not exit");
        Assert.AreEqual(ApplicationRestartResult.Canceled, await service.RequestRestartAsync(cancellation.Token));
        Assert.AreEqual(1, aborted);
    }

    [TestMethod]
    public async Task HandshakeSuccessAndTimeoutOwnOnlyTheirTestChildren()
    {
        var releaseName = @"Local\AFMediaBar.Restart.Test." + Guid.NewGuid().ToString("N");
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, releaseName);
        foreach (var signalReady in new[] { true, false })
        {
            var eventName = @"Local\AFMediaBar.Restart." + Guid.NewGuid().ToString("N");
            var script = (signalReady ? "$ready = [Threading.EventWaitHandle]::OpenExisting('" + eventName + "'); $null = $ready.Set(); $ready.Dispose(); " : "") +
                "$release = [Threading.EventWaitHandle]::OpenExisting('" + releaseName + "'); $null = $release.WaitOne(15000); $release.Dispose()";
            Process? child = null;
            Process? observation = null;
            try
            {
                var preparation = await RestartProcessHandoff.PrepareAsync(BuildPowerShellStart(script), eventName, TimeSpan.FromSeconds(5), default,
                    start => { child = Process.Start(start); observation = Process.GetProcessById(child!.Id); return child; });
                Assert.IsNotNull(observation);
                if (signalReady)
                {
                    Assert.IsNotNull(preparation);
                    Assert.IsFalse(observation.HasExited);
                    // 未提交的成功握手也必须在请求被放弃时终止等待实例。
                    await preparation.DisposeAsync();
                }
                else Assert.IsNull(preparation);
                Assert.IsTrue(observation.HasExited);
            }
            finally
            {
                if (observation is not null)
                {
                    if (!observation.HasExited) observation.Kill();
                    await observation.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    observation.Dispose();
                }
                child?.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task ParentWaitAcknowledgesThenWaitsForTheExactProcessAndHandlesTimeout()
    {
        var eventName = @"Local\AFMediaBar.Restart." + Guid.NewGuid().ToString("N");
        var releaseName = @"Local\AFMediaBar.Restart.Test." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        using var committed = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".Commit");
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, releaseName);
        using var parent = StartWaitingProcess(releaseName);
        try
        {
            var handoff = new RestartHandoff(parent.Id, parent.StartTime.ToUniversalTime().Ticks, eventName);
            Assert.IsFalse(await RestartProcessHandoff.WaitForParentAsync(handoff with { ParentStartTicks = 1 }, TimeSpan.FromSeconds(5), default));
            var waiting = RestartProcessHandoff.WaitForParentAsync(handoff, TimeSpan.FromSeconds(10), default);
            Assert.IsTrue(await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(5))));
            Assert.IsFalse(waiting.IsCompleted);
            Assert.IsFalse(await RestartProcessHandoff.WaitForParentAsync(handoff, TimeSpan.FromMilliseconds(100), default));
            committed.Set();
            release.Set();
            Assert.IsTrue(await waiting);
        }
        finally
        {
            release.Set();
            if (!parent.HasExited) parent.Kill();
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    internal static Process StartWaitingProcess(string releaseName)
        => Process.Start(BuildPowerShellStart("$signal = [Threading.EventWaitHandle]::OpenExisting('" + releaseName + "'); $null = $signal.WaitOne(15000); $signal.Dispose()"))!;

    [TestMethod]
    public async Task ParentExitWithoutCommitCannotAuthorizeRestart()
    {
        var eventName = @"Local\AFMediaBar.Restart." + Guid.NewGuid().ToString("N");
        var releaseName = @"Local\AFMediaBar.Restart.Test." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        using var committed = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".Commit");
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, releaseName);
        using var parent = StartWaitingProcess(releaseName);
        try
        {
            var waiting = RestartProcessHandoff.WaitForParentAsync(new(parent.Id, parent.StartTime.ToUniversalTime().Ticks, eventName), TimeSpan.FromSeconds(10), default);
            Assert.IsTrue(await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(5))));
            release.Set();
            Assert.IsFalse(await waiting);
        }
        finally
        {
            release.Set();
            if (!parent.HasExited) parent.Kill();
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static ProcessStartInfo BuildPowerShellStart(string script)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        return start;
    }

    private sealed class Release(Action release) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) release(); }
    }
}
