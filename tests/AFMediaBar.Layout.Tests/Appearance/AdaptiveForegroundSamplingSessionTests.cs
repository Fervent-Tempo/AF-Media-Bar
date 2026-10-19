// 在自有 Dispatcher 中验证移动/挂起淘汰采样，不触发首机会取消异常或发布旧结果。
using System.Runtime.ExceptionServices;
using System.Windows;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AdaptiveForegroundSamplingSessionTests
{
    [TestMethod]
    public void InvalidatedSampleDoesNotThrowOrPublishAndLatestSampleStillApplies()
    {
        StaTest.Run(async dispatcher =>
        {
            var publications = 0;
            var canceled = 0;
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bounds = new Int32Rect(20, 20, 40, 100);
            void OnException(object? sender, FirstChanceExceptionEventArgs args)
            {
                if (args.Exception is OperationCanceledException &&
                    Environment.StackTrace.Contains(nameof(AdaptiveForegroundSamplingSession), StringComparison.Ordinal))
                    Interlocked.Increment(ref canceled);
            }
            AppDomain.CurrentDomain.FirstChanceException += OnException;
            using var sampler = new ScreenBackgroundSampler();
            using var session = new AdaptiveForegroundSamplingSession(sampler, dispatcher,
                () => bounds, decision =>
                {
                    if (decision is null) return;
                    publications++;
                    applied.TrySetResult();
                });
            try
            {
                session.RequestRefresh();
                session.Invalidate(clearDecision: false);
                // 先让失效采样完成；下一次请求必须仍能正常发布。
                await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                bounds = new Int32Rect(80, 20, 40, 100);
                session.RequestRefresh();
                await applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual(1, publications, "Only the latest bounds may publish.");
                Assert.AreEqual(0, canceled, "Normal sampling invalidation must not create first-chance cancellation exceptions.");
            }
            finally { AppDomain.CurrentDomain.FirstChanceException -= OnException; }
        });
    }
}
