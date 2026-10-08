// 为 WPF 生命周期回归提供独立 STA 消息循环；每次测试结束关闭 Dispatcher 并回收线程。
using System.Windows.Threading;

namespace AFMediaBar.Layout.Tests;

internal static class StaTest
{
    internal static void Run(Func<Dispatcher, Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(dispatcher); completion.TrySetResult(); }
                catch (Exception exception) { completion.TrySetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            completion.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }
        finally
        {
            if (!thread.Join(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("STA dispatcher did not stop.");
        }
    }
}
