using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 媒体目录重建与退出时的资源交接：旧资源必须等最后一个读取租约结束后才释放。
/// Resource handoff during catalog replacement and shutdown: an old resource must outlive its final read lease.
/// </summary>
[TestClass]
public sealed class ReplaceableResourceTests
{
    [TestMethod]
    public void ReplacementAndShutdownWaitForTheirOwnReadersWithoutBlockingNewReaders()
    {
        var first = new object();
        var second = new object();
        var retired = new List<object>();
        var resources = new ReplaceableResource<object>(first, retired.Add);
        var firstLease = resources.TryAcquire();
        Assert.IsNotNull(firstLease);

        Assert.IsTrue(resources.TryReplace(second));
        CollectionAssert.AreEqual(Array.Empty<object>(), retired);
        Assert.AreSame(first, firstLease.Value);

        var secondLease = resources.TryAcquire();
        Assert.IsNotNull(secondLease);
        Assert.AreSame(second, secondLease.Value);

        firstLease.Dispose();
        CollectionAssert.AreEqual(new[] { first }, retired);
        firstLease.Dispose();
        CollectionAssert.AreEqual(new[] { first }, retired);

        resources.Dispose();
        Assert.IsNull(resources.TryAcquire());
        CollectionAssert.AreEqual(new[] { first }, retired);
        secondLease.Dispose();
        resources.Dispose();
        CollectionAssert.AreEqual(new[] { first, second }, retired);
    }

    [TestMethod]
    public async Task ConcurrentRetirementCannotDisposeAnInFlightReader()
    {
        var first = new object();
        var second = new object();
        var retired = new ConcurrentQueue<object>();
        using var resources = new ReplaceableResource<object>(first, retired.Enqueue);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Task.Run(async () =>
        {
            using var lease = resources.TryAcquire();
            Assert.IsNotNull(lease);
            entered.SetResult();
            await release.Task;
            Assert.AreSame(first, lease.Value);
        });

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(await Task.Run(() => resources.TryReplace(second)).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsFalse(retired.Contains(first));
            resources.Dispose();
            Assert.IsFalse(retired.Contains(first));
        }
        finally
        {
            release.TrySetResult();
            await reader.WaitAsync(TimeSpan.FromSeconds(5));
        }

        CollectionAssert.AreEquivalent(new[] { first, second }, retired.ToArray());
    }
}
