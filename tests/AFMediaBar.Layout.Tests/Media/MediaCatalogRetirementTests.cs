// 使用未启动的真实托管管理器验证代际过滤；不连接 SMTC，不以此证明 WinRT 原生释放安全。
using System.Reflection;
using System.Runtime.CompilerServices;
using AFMediaBar.Classes.Services.Media.Smtc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsMediaController;

namespace AFMediaBar.Layout.Tests;

/// <summary>旧媒体管理器持有读租约和迟到事件时的退役行为。</summary>
[TestClass]
public sealed class MediaCatalogRetirementTests
{
    [TestMethod]
    public async Task ReplacementRejectsOldCallbacksWhileReadIsStillInFlight()
    {
        var catalog = (MediaSessionCatalog)RuntimeHelpers.GetUninitializedObject(typeof(MediaSessionCatalog));
        var old = new MediaSessionCatalog.ManagerRegistration(catalog, new MediaManager());
        var current = new MediaSessionCatalog.ManagerRegistration(catalog, new MediaManager());
        var retired = new List<MediaSessionCatalog.ManagerRegistration>();
        using var resources = new ReplaceableResource<MediaSessionCatalog.ManagerRegistration>(old, registration =>
        {
            registration.Detach();
            retired.Add(registration);
        });
        typeof(MediaSessionCatalog).GetField("_managers", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(catalog, resources);
        old.Attach();
        current.Attach();
        var observed = 0;
        catalog.AnySessionOpened += _ => observed++;
        var oldCallback = Opened(old.Manager);
        var newCallback = Opened(current.Manager);
        oldCallback(null!);
        Assert.AreEqual(1, observed);

        var finishRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = ReadAsync();
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.IsTrue(resources.TryReplace(current));
            Assert.AreEqual(0, retired.Count, "读取尚未结束，不能释放旧管理器。");
            oldCallback(null!);
            newCallback(null!);
            Assert.AreEqual(2, observed, "旧代迟到事件应被丢弃，新代事件仍须转发。");
        }
        finally
        {
            finishRead.TrySetResult();
            await read.WaitAsync(TimeSpan.FromSeconds(2));
            catalog.Dispose();
            catalog.Dispose();
        }
        CollectionAssert.AreEqual(new[] { old, current }, retired);
        oldCallback(null!);
        newCallback(null!);
        Assert.AreEqual(2, observed, "释放后的已捕获回调也不能继续发布。");

        async Task ReadAsync()
        {
            using var lease = resources.TryAcquire();
            readStarted.TrySetResult();
            await finishRead.Task;
        }
    }

    private static MediaManager.SessionChangeDelegate Opened(MediaManager manager) =>
        (MediaManager.SessionChangeDelegate)typeof(MediaManager)
            .GetField("OnAnySessionOpened", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
}
