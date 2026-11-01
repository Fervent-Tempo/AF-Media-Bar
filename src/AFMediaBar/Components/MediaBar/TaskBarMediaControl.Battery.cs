// 只呈现共享快照；本控件拥有轮换计时器，卸载时停止，不执行硬件查询。
using AFMediaBar.Classes.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private BluetoothBatteryService? _batterySource;
    private bool _batterySubscribed;
    private bool _batteryCanShow;
    private readonly Dictionary<ComponentKey, Border> _batterySurfaces = [];
    private DispatcherTimer? _batteryCycleTimer;
    private int _batteryCycleIndex;
    private Border? _batteryPressedSurface;
    public event Action<TrayIconBounds>? BatteryOverviewRequested;
    private double BatterySurfaceWidth => PresentationSettings.BluetoothBatteryWidget.VisualStyle == BatteryVisualStyle.Graphic ? 34 : 78;
    private double BatteryVerticalLength => PresentationSettings.BluetoothBatteryWidget.VisualStyle == BatteryVisualStyle.Graphic ? 30 : 46;

    public void BindBatterySource(BluetoothBatteryService source)
    {
        DetachBatterySource(); _batterySource = source;
        Loaded -= BatteryLoaded; Unloaded -= BatteryUnloaded;
        Loaded += BatteryLoaded; Unloaded += BatteryUnloaded;
        if (IsLoaded) BatteryLoaded(this, new RoutedEventArgs());
    }
    private void BatteryLoaded(object sender, RoutedEventArgs args)
    {
        if (_batterySource is null || _batterySubscribed) return;
        _batterySource.Changed += BatteryChanged; _batterySubscribed = true;
        _batteryCycleTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, BatteryCycleTick, Dispatcher);
        BatteryChanged(this, EventArgs.Empty);
    }
    private void BatteryUnloaded(object sender, RoutedEventArgs args) => DetachBatterySource();
    private void DetachBatterySource()
    {
        if (_batterySubscribed && _batterySource is not null) _batterySource.Changed -= BatteryChanged;
        _batterySubscribed = false; _batteryCycleTimer?.Stop(); _batteryPressedSurface = null;
    }
    private void BatteryCycleTick(object? sender, EventArgs args)
    {
        if (!IsLoaded || !IsVisible || _isHostVisibilitySuspended || _backgroundPruneLevel != MemoryPruneLevel.None ||
            HasComponentGesture || _batterySurfaces.Values.Any(s => s.IsMouseOver || s.IsKeyboardFocusWithin)) return;
        _batteryCycleIndex++; ConfigureBattery();
    }
    private void BatteryChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => BatteryChanged(sender, args)); return; }
        ConfigureBattery();
        if (!IsLoaded) return;
        if (_isVertical) ApplyVerticalSectionGeometry(MainBorder.Height); else ApplyTaskbarSectionGeometry(MainBorder.Width);
        RefreshDesiredSize();
    }
    private void ConfigureBattery()
    {
        if (TaskbarBatterySurface is null) return;
        var settings = PresentationSettings.BluetoothBatteryWidget;
        var readings = _batterySource?.WidgetReadings ?? [];
        _batteryCanShow = readings.Count > 0;
        _batterySurfaces.TryAdd(new(TaskbarRestComponent.BluetoothBattery), TaskbarBatterySurface);
        var keys = settings.GetSelectedDevices().Select(d => new ComponentKey(TaskbarRestComponent.BluetoothBattery, d.Id)).ToHashSet();
        foreach (var key in _batterySurfaces.Keys.Where(k => k.DeviceId is not null && !keys.Contains(k)).ToArray())
        {
            var surface = _batterySurfaces[key];
            if (surface.IsKeyboardFocusWithin) Focus();
            ((Panel)surface.Parent).Children.Remove(surface); _batterySurfaces.Remove(key);
        }
        foreach (var key in keys)
        {
            if (_batterySurfaces.ContainsKey(key)) continue;
            var surface = new Border
            {
                Child = new BatteryPresenter(), CornerRadius = new CornerRadius(5), Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0, 1.25, 0, 0),
                Focusable = true, Visibility = Visibility.Collapsed, Tag = "MediaAction"
            };
            surface.SetResourceReference(FocusVisualStyleProperty, "AfFocusVisualStyle");
            surface.MouseLeftButtonDown += TaskbarBatterySurface_MouseLeftButtonDown;
            surface.MouseLeftButtonUp += TaskbarBatterySurface_MouseLeftButtonUp;
            surface.MouseEnter += TaskbarBatterySurface_MouseEnter; surface.MouseLeave += TaskbarBatterySurface_MouseLeave;
            surface.KeyDown += TaskbarBatterySurface_KeyDown;
            Panel.SetZIndex(surface, 4); ((Panel)TaskbarBatterySurface.Parent).Children.Add(surface);
            _batterySurfaces[key] = surface;
        }
        _batteryCycleIndex = readings.Count == 0 ? 0 : _batteryCycleIndex % readings.Count;
        foreach (var (key, surface) in _batterySurfaces)
        {
            var snapshot = key.DeviceId is null ? readings.ElementAtOrDefault(_batteryCycleIndex) :
                readings.FirstOrDefault(r => new ComponentKey(key.Component, r.DeviceId) == key);
            var presenter = (BatteryPresenter)surface.Child;
            presenter.SetBinding(ForegroundProperty, new Binding(nameof(TextBlock.Foreground)) { Source = SongTitle });
            presenter.VisualStyle = settings.VisualStyle; presenter.Vertical = _isVertical; presenter.Snapshot = snapshot;
            presenter.FillColor = settings.FillColor;
            surface.ToolTip = snapshot is null ? Translations.Get("Battery.NoReading") : snapshot.DeviceName + "\n" + snapshot.Details + "\n" +
                Translations.Get(snapshot.IsCharging switch { true => "Battery.Charging", false => "Battery.Discharging", null => "Battery.ChargingUnknown" });
            System.Windows.Automation.AutomationProperties.SetName(surface, snapshot is null ? Translations.Get("Battery.Overview") : snapshot.DeviceName + " · " + snapshot.Label);
        }
        if (_batteryCycleTimer is not null)
        {
            _batteryCycleTimer.Interval = TimeSpan.FromSeconds(settings.CycleIntervalSeconds);
            if (_batterySubscribed && IsVisible && !_isHostVisibilitySuspended && _backgroundPruneLevel == MemoryPruneLevel.None &&
                settings.PresentationMode == BatteryPresentationMode.Cycle && readings.Count > 1) _batteryCycleTimer.Start();
            else _batteryCycleTimer.Stop();
        }
    }
    private bool IsBatteryKeyVisible(ComponentKey key) => key.DeviceId is null ? _batteryCanShow :
        _batterySource?.WidgetReadings.Any(r => new ComponentKey(key.Component, r.DeviceId) == key) == true;
    private void PlaceBatterySurfaces(TaskbarRestLayout layout, double crossSize)
    {
        foreach (var (key, surface) in _batterySurfaces)
        {
            var placement = layout.Find(key);
            SetRestComponentVisible(surface, placement is not null);
            if (placement is not { } position) continue;
            surface.Width = _isVertical ? crossSize : position.Width;
            surface.Height = _isVertical ? position.Width : crossSize;
            surface.VerticalAlignment = _isVertical ? VerticalAlignment.Top : VerticalAlignment.Center;
            surface.Margin = _isVertical ? new Thickness(2, position.Left, 0, 0) : new Thickness(position.Left, 0, 0, 0);
        }
        _artworkHover.RefreshBatteryPush();
    }
    private void TaskbarBatterySurface_MouseEnter(object sender, MouseEventArgs args) => AnimateComponentHover((Border)sender, true);
    private void TaskbarBatterySurface_MouseLeave(object sender, MouseEventArgs args)
    { AnimateComponentHover((Border)sender, false); _batteryPressedSurface = null; }
    private void TaskbarBatterySurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    { _batteryPressedSurface = (Border)sender; _batteryPressedSurface.Focus(); args.Handled = true; }
    private void TaskbarBatterySurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        var pressed = _batteryPressedSurface; _batteryPressedSurface = null;
        if (ReferenceEquals(pressed, sender) && pressed!.IsMouseOver && !_suppressComponentRelease && !_componentGestureActive) OpenBatteryOverview(pressed);
        args.Handled = true;
    }
    private void TaskbarBatterySurface_KeyDown(object sender, KeyEventArgs args)
    { if (args.Key is Key.Enter or Key.Space) { OpenBatteryOverview((Border)sender); args.Handled = true; } }
    private void OpenBatteryOverview(Border surface)
    {
        if (!IsLoaded) return;
        var start = surface.PointToScreen(new Point());
        var end = surface.PointToScreen(new Point(surface.ActualWidth, surface.ActualHeight));
        BatteryOverviewRequested?.Invoke(new((int)start.X, (int)start.Y, (int)end.X, (int)end.Y));
    }
    public bool IsBatteryComponentClick(DependencyObject? source)
    {
        while (source is not null)
        {
            if (_batterySurfaces.Values.Any(surface => ReferenceEquals(source, surface))) return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : null;
        }
        return false;
    }
}
