// 为保留的 WPF 回归提供有界消息泵；调用方拥有窗口，计时器在退出消息泵时停止。
using System.Windows.Threading;

namespace AFMediaBar.Layout.Tests;

internal static class SettingsLiveVisualChecks
{
    internal static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }
}
