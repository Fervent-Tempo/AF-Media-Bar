// 只注册无副作用的独立接收窗口，不发送输入；验证注册所有权、停止与重复释放。
using System.Collections.Concurrent;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.InteropServices;

namespace AFMediaBar.Layout.Tests;

/// <summary>真实 Raw Input 接收线程及进程内注册的资源边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class RawMouseInputLifetimeTests
{
    [TestMethod]
    public async Task SecondReceiverDoesNotReplaceOwnerAndShutdownUnregisters()
    {
        if (RegisteredDevices().Any(item => item.UsagePage == 1 && item.Usage == 2))
            Assert.Inconclusive("测试进程已有鼠标 Raw Input 接收者，不能覆盖其注册。");
        var failures = new ConcurrentQueue<Exception>();
        using var first = new RawMouseInputSource(_ => { }, _ => { }, failures.Enqueue);
        using var second = new RawMouseInputSource(_ => { }, _ => { }, _ => { });
        try
        {
            try { await first.Start().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (Exception exception) { Assert.Inconclusive($"当前环境无法注册 Raw Input：{exception.Message}"); }
            Assert.IsTrue(first.Registered);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => second.Start().WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.IsFalse(second.Registered);
            Assert.IsTrue(await first.SynchronizeAsync().WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.AreEqual(1, RegisteredDevices().Count(item => item.UsagePage == 1 && item.Usage == 2));
        }
        finally
        {
            first.Dispose(); second.Dispose();
            await Task.WhenAll(first.Completion, second.Completion).WaitAsync(TimeSpan.FromSeconds(3));
        }
        Assert.IsFalse(first.Registered);
        Assert.IsFalse(await first.SynchronizeAsync());
        Assert.AreEqual(0, RegisteredDevices().Count(item => item.UsagePage == 1 && item.Usage == 2));
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public async Task DisposingBeforeStartDoesNotCreateAThreadOrRegistration()
    {
        using var source = new RawMouseInputSource(_ => { }, _ => { }, _ => { });
        source.Dispose(); source.Dispose();
        await source.Start().WaitAsync(TimeSpan.FromSeconds(1));
        await source.Completion.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(source.Registered);
        Assert.IsFalse(await source.SynchronizeAsync());
    }

    private static NativeMethods.RAWINPUTDEVICE[] RegisteredDevices()
    {
        uint count = 0;
        var size = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>();
        Assert.AreNotEqual(uint.MaxValue, NativeMethods.GetRegisteredRawInputDevices(null, ref count, size));
        var devices = new NativeMethods.RAWINPUTDEVICE[count];
        if (count != 0) Assert.AreNotEqual(uint.MaxValue, NativeMethods.GetRegisteredRawInputDevices(devices, ref count, size));
        return devices;
    }
}
