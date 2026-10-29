// 让页面布局和输入先于可延后的数据刷新；等待期间卸载的页面不能启动旧请求。
using System.Windows.Threading;

namespace AFMediaBar.Classes.Utils;

/// <summary>为设置页面的数据激活提供首轮呈现后的调度与卸载检查。</summary>
internal static class SettingsPageActivation
{
    internal static async Task<bool> WaitForPresentationAsync(FrameworkElement page, CancellationToken cancellationToken = default)
    {
        var unloaded = false;
        void OnUnloaded(object sender, RoutedEventArgs args)
        {
            unloaded = true;
            page.Unloaded -= OnUnloaded;
        }
        page.Unloaded += OnUnloaded;
        try
        {
            return await page.Dispatcher.InvokeAsync(() =>
            {
                page.Unloaded -= OnUnloaded;
                return !unloaded && page.IsLoaded && page.IsVisible && !cancellationToken.IsCancellationRequested;
            }, DispatcherPriority.Background);
        }
        catch (TaskCanceledException) { return false; }
    }
}
