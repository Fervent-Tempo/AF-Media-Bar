// 在可控独立子进程中验证终止边界，父测试只拥有并清理自己启动的进程。
using System.Diagnostics;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>退出请求合并、容器释放和独立保护线程回归。</summary>
[TestClass]
public sealed class ApplicationExitTests
{
    [TestMethod]
    public void RequestsMergeAndContainerDisposalDoesNotRemoveTheBackstop()
    {
        using var fired = new ManualResetEventSlim(false);
        using var exit = new ApplicationExitCoordinator(code => { Assert.AreEqual(1, code); fired.Set(); return true; }, TimeSpan.FromMilliseconds(200));
        var requests = new List<int>();
        exit.ExitRequested += requests.Add;
        using (var provider = new ServiceCollection().AddSingleton<IApplicationExitService>(exit).BuildServiceProvider())
        {
            var service = provider.GetRequiredService<IApplicationExitService>();
            service.RequestExit(7);
            service.RequestExit(0);
            service.ArmExitProtection();
        }
        CollectionAssert.AreEqual(new[] { 7 }, requests);
        Assert.IsTrue(fired.Wait(TimeSpan.FromSeconds(5)), "释放 Host 容器不能提前撤掉退出保护。");
    }

    [TestMethod]
    public void FailedNativeTerminationKeepsTheBackstopActive()
    {
        using var retried = new ManualResetEventSlim(false);
        var attempts = 0;
        using var exit = new ApplicationExitCoordinator(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1) return false;
            retried.Set();
            return true;
        }, TimeSpan.FromMilliseconds(100));
        exit.RequestExit();
        Assert.IsTrue(retried.Wait(TimeSpan.FromSeconds(5)), "原生失败不能撤掉退出保护。");
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public void DisposedCoordinatorDoesNotStartAnotherExit()
    {
        var exit = new ApplicationExitCoordinator(_ => throw new AssertFailedException("Canceled watchdog must not fire"));
        exit.ExitRequested += _ => Assert.Fail("Disposed request must not publish");
        exit.ArmExitProtection();
        exit.Dispose();
        exit.Dispose();
        exit.RequestExit();
    }

    [DataTestMethod]
    [DataRow("normal", 0)]
    [DataRow("closing", 1)]
    [DataRow("process-exit", 1)]
    public async Task NormalExitReturnsAndHungExitTerminatesOnlyTheTestChild(string scenario, int expectedCode)
    {
        var eventName = @"Local\AFMediaBar.Exit.Test." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var assembly = typeof(ApplicationExitTests).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.ChangeExtension(assembly, ".runtimeconfig.json"));
        start.ArgumentList.Add("--depsfile");
        start.ArgumentList.Add(Path.ChangeExtension(assembly, ".deps.json"));
        start.ArgumentList.Add(typeof(AFMediaBar.Exit.TestHost.Program).Assembly.Location);
        start.ArgumentList.Add(scenario);
        start.ArgumentList.Add(eventName);
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            Assert.IsTrue(await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(30))), "退出测试子进程没有到达目标阶段。");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(expectedCode, child.ExitCode, await output + await errors);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await output;
            await errors;
        }
    }
}
