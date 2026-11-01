// 仅订阅并呈现共享电量快照；控件不枚举设备、不查询 GATT，不拥有追踪服务。
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Models;
using System.Windows.Input;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private BluetoothBatteryService? _batterySource;
    private bool _batterySubscribed;
    private bool _batteryCanShow;
    private bool _batteryPressed;
    public event Action<TrayIconBounds>? BatteryOverviewRequested;
    private const double BatterySurfaceWidth = 66;
    private const double BatteryVerticalLength = 42;

    public void BindBatterySource(BluetoothBatteryService source)
    {
        DetachBatterySource();
        _batterySource = source;
        Loaded -= BatteryLoaded; Unloaded -= BatteryUnloaded;
        Loaded += BatteryLoaded; Unloaded += BatteryUnloaded;
        if (IsLoaded) BatteryLoaded(this, new RoutedEventArgs());
    }
    private void BatteryLoaded(object sender, RoutedEventArgs args)
    {
        if (_batterySource is null || _batterySubscribed) return;
        _batterySource.Changed += BatteryChanged;
        _batterySubscribed = true;
        BatteryChanged(this, EventArgs.Empty);
    }
    private void BatteryUnloaded(object sender, RoutedEventArgs args) => DetachBatterySource();
    private void DetachBatterySource()
    {
        if (_batterySubscribed && _batterySource is not null) _batterySource.Changed -= BatteryChanged;
        _batterySubscribed = false;
    }
    private void BatteryChanged(object? sender, EventArgs args)
    {
        var snapshot = _batterySource?.Snapshot;
        var visible = _batterySource?.ShouldShowWidget == true;
        TaskbarBatteryText.Text = snapshot?.Percent is { } percent ? percent + "%" + (snapshot.IsRealtime ? string.Empty : "*") : "—";
        TaskbarBatterySurface.ToolTip = snapshot is null ? Translations.Get("Battery.NoReading") :
            snapshot.DeviceName + " · " + Translations.Get(snapshot.IsConnected ? "Battery.Connected" : "Battery.Disconnected") + "\n" + snapshot.Details;
        if (_batteryCanShow == visible) return;
        _batteryCanShow = visible;
        if (IsLoaded)
        {
            ApplyTaskbarSectionGeometry(_isVertical ? MainBorder.Height : MainBorder.Width);
            RefreshDesiredSize();
        }
    }
    private void TaskbarBatterySurface_MouseEnter(object sender, System.Windows.Input.MouseEventArgs args) => AnimateComponentHover(TaskbarBatterySurface, true);
    private void TaskbarBatterySurface_MouseLeave(object sender, System.Windows.Input.MouseEventArgs args) => AnimateComponentHover(TaskbarBatterySurface, false);
    private void TaskbarBatterySurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    { _batteryPressed = true; TaskbarBatterySurface.Focus(); args.Handled = true; }
    private void TaskbarBatterySurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        var pressed = _batteryPressed; _batteryPressed = false;
        if (pressed && TaskbarBatterySurface.IsMouseOver && !_suppressComponentRelease && !_componentGestureActive) OpenBatteryOverview();
        args.Handled = true;
    }
    private void TaskbarBatterySurface_KeyDown(object sender, KeyEventArgs args)
    { if (args.Key is Key.Enter or Key.Space) { OpenBatteryOverview(); args.Handled = true; } }
    private void OpenBatteryOverview()
    {
        if (!IsLoaded) return;
        var start = TaskbarBatterySurface.PointToScreen(new Point());
        var end = TaskbarBatterySurface.PointToScreen(new Point(TaskbarBatterySurface.ActualWidth, TaskbarBatterySurface.ActualHeight));
        BatteryOverviewRequested?.Invoke(new((int)start.X, (int)start.Y, (int)end.X, (int)end.Y));
    }
    public bool IsBatteryComponentClick(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, TaskbarBatterySurface)) return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : null;
        }
        return false;
    }
}
