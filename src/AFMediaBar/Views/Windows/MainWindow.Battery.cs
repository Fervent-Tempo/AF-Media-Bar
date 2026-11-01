// 隐藏宿主持有可复用电量浮窗；窗口只呈现共享视图模型，不执行平台查询。
using AFMediaBar.Classes.Models;
using System.Windows.Threading;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

public partial class MainWindow
{
    private TaskbarCompactFlyoutWindow? _batteryFlyout;
    private IDisposable? _batteryEdgeLease;
    private int _batteryOpenVersion;
    private void OpenBatteryOverviewFromSettings()
    {
        if (!GetCursorPos(out var point)) return;
        ShowBatteryOverview(new(point.X, point.Y, point.X + 1, point.Y + 1));
    }
    private void ShowBatteryOverview(TrayIconBounds anchor, IDisposable? edgeLease = null)
    {
        var version = ++_batteryOpenVersion;
        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosing || TaskbarEnvironmentRecovering || !_batteryOverview.IsEnabled || version != _batteryOpenVersion)
            { edgeLease?.Dispose(); return; }
            if (_batteryFlyout?.IsShowing(TaskbarCompactFlyoutMode.Battery) == true)
            { _batteryFlyout.Dismiss(); edgeLease?.Dispose(); return; }
            _fullPanels.CloseAll();
            if (_batteryFlyout is null)
            {
                _batteryFlyout = new TaskbarCompactFlyoutWindow(_appearanceService);
                _batteryFlyout.IsVisibleChanged += (_, _) =>
                { if (_batteryFlyout?.IsVisible != true) Interlocked.Exchange(ref _batteryEdgeLease, null)?.Dispose(); };
            }
            Interlocked.Exchange(ref _batteryEdgeLease, edgeLease)?.Dispose();
            _batteryFlyout.ShowBatteryOverview(_batteryOverview, anchor);
        }, DispatcherPriority.Background);
    }
    private void CloseBatteryOverview()
    {
        _batteryOpenVersion++;
        _batteryFlyout?.Dismiss();
        _batteryOverview.Deactivate();
        Interlocked.Exchange(ref _batteryEdgeLease, null)?.Dispose();
    }
}
