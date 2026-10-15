// 验证事务切换、失败回滚和取消后的旧回调，不创建 Explorer 或音频资源。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class BarRuntimeCoordinatorTests
{
    [TestMethod]
    public async Task RunningStateChangesOnlyAfterEveryCandidateActivates()
    {
        using var runtime = new BarRuntimeCoordinator();
        var old = new Host();
        Assert.IsTrue(await runtime.SwitchAsync(WindowMode.Taskbar, _ => Hosts(old)));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = new Host { Activation = token => ready.Task.WaitAsync(token) };
        var transition = runtime.SwitchAsync(WindowMode.FloatingBar, _ => Hosts(next));
        Assert.IsTrue(runtime.IsSwitching);
        Assert.AreEqual(WindowMode.Taskbar, runtime.RunningMode);
        Assert.IsTrue(old.Suspended);
        Assert.AreEqual(0, old.Disposals);
        ready.SetResult();
        Assert.IsTrue(await transition);
        Assert.AreEqual(WindowMode.FloatingBar, runtime.RunningMode);
        Assert.AreSame(next, runtime.Hosts.Single());
        Assert.AreEqual(1, old.Disposals);
    }

    [TestMethod]
    public async Task PartialActivationFailureDisposesCandidatesAndResumesOldHost()
    {
        using var runtime = new BarRuntimeCoordinator();
        var old = new Host();
        await runtime.SwitchAsync(WindowMode.Taskbar, _ => Hosts(old));
        var first = new Host();
        var broken = new Host { Activation = _ => throw new InvalidOperationException("activation") };
        Assert.IsFalse(await runtime.SwitchAsync(WindowMode.FloatingBar, _ => Hosts(first, broken)));
        Assert.AreEqual(WindowMode.Taskbar, runtime.RunningMode);
        Assert.AreSame(old, runtime.Hosts.Single());
        Assert.IsFalse(old.Suspended);
        Assert.AreEqual(1, old.Resumes);
        Assert.AreEqual(0, old.Disposals);
        Assert.AreEqual(1, first.Disposals);
        Assert.AreEqual(1, broken.Disposals);
        Assert.AreEqual("activation", runtime.LastFailure);
    }

    [TestMethod]
    public async Task SupersededActivationCannotPublishOrRetireOldHost()
    {
        using var runtime = new BarRuntimeCoordinator();
        var old = new Host();
        await runtime.SwitchAsync(WindowMode.Taskbar, _ => Hosts(old));
        var first = new Host { Activation = token => Task.Delay(Timeout.InfiniteTimeSpan, token) };
        var pending = runtime.SwitchAsync(WindowMode.FloatingBar, _ => Hosts(first));
        var second = new Host();
        var latest = runtime.SwitchAsync(WindowMode.Taskbar, _ => Hosts(second), force: true);
        Assert.IsFalse(await pending);
        Assert.IsTrue(await latest);
        Assert.AreSame(second, runtime.Hosts.Single());
        Assert.AreEqual(1, first.Disposals);
        Assert.AreEqual(1, old.Disposals);
        Assert.AreEqual(1, old.Resumes);
    }

    [TestMethod]
    public async Task ExitCancelsPendingActivationWithoutLatePublication()
    {
        var runtime = new BarRuntimeCoordinator();
        var old = new Host();
        await runtime.SwitchAsync(WindowMode.Taskbar, _ => Hosts(old));
        var next = new Host { Activation = token => Task.Delay(Timeout.InfiniteTimeSpan, token) };
        var pending = runtime.SwitchAsync(WindowMode.FloatingBar, _ => Hosts(next));
        var publications = 0;
        runtime.Changed += (_, _) => publications++;
        runtime.Dispose();
        runtime.Dispose();
        Assert.IsFalse(await pending);
        Assert.AreEqual(0, publications);
        Assert.AreEqual(1, old.Disposals);
        Assert.AreEqual(1, next.Disposals);
        Assert.AreEqual(0, runtime.Hosts.Count);
    }

    private static Task<IReadOnlyList<IBarRuntimeHost>> Hosts(params IBarRuntimeHost[] hosts) =>
        Task.FromResult<IReadOnlyList<IBarRuntimeHost>>(hosts);

    private sealed class Host : IBarRuntimeHost
    {
        public Func<CancellationToken, Task> Activation { get; init; } = _ => Task.CompletedTask;
        public int Disposals { get; private set; }
        public int Resumes { get; private set; }
        public bool Suspended { get; private set; }
        public Task ActivateAsync(CancellationToken token) => Activation(token);
        public void Suspend() => Suspended = true;
        public void Resume() { Suspended = false; Resumes++; }
        public void Dispose() { Disposals++; }
    }
}
