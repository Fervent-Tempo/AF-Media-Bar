using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using AFMediaBar.Classes.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 为普通 Fluent 窗口应用所选材质；WPF 弹出菜单固定使用不透明 Fluent 纯色，
/// 并在 DWM 生命周期变化后恢复窗口和菜单外观。
/// Applies the selected backdrop to regular Fluent windows; WPF popup menus
/// remain opaque Fluent-solid surfaces and are restored after DWM lifecycle changes.
/// </summary>
public sealed class WindowAppearanceService : IDisposable
{
    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;
    private const int WmDwmCompositionChanged = 0x031E;
    private const int WmDwmColorizationColorChanged = 0x0320;
    private const int WmDpiChanged = 0x02E0;
    private const int WmDpiChangedAfterParent = 0x02E3;
    private readonly NativeWindowBackdropAdapter _nativeBackdropAdapter;
    private readonly HashSet<FluentWindow> _windows = [];
    private readonly Dictionary<FluentWindow, HwndSource> _windowSources = [];
    private readonly HashSet<ContextMenu> _menus = [];
    private readonly Dictionary<ContextMenu, Window> _menuOwners = [];
    private bool _applyQueued;
    private bool _disposed;

    /// <summary>
    /// 创建窗口外观协调服务并订阅外观与主题变化。
    /// Creates the window appearance coordinator and subscribes to appearance and theme changes.
    /// </summary>
    public WindowAppearanceService(NativeWindowBackdropAdapter nativeBackdropAdapter)
    {
        _nativeBackdropAdapter = nativeBackdropAdapter;
        SettingsManager.AppearanceSettingsChanged += OnAppearanceSettingsChanged;
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
    }

    /// <summary>
    /// 注册一个需要统一应用材质和主题属性的 Fluent 窗口。
    /// Registers a Fluent window for unified backdrop and theme-attribute application.
    /// </summary>
    /// <param name="window">要注册的窗口。/ Window to register.</param>
    public void Attach(FluentWindow window)
    {
        if (_disposed || !_windows.Add(window))
        {
            return;
        }

        window.SourceInitialized += OnWindowSourceInitialized;
        window.Loaded += OnWindowLoaded;
        window.IsVisibleChanged += OnWindowIsVisibleChanged;
        window.Closed += OnWindowClosed;

        if (new WindowInteropHelper(window).Handle != nint.Zero)
        {
            RegisterWindowSource(window);
        }
    }

    /// <summary>
    /// 注册一个由指定宿主窗口拥有的 ContextMenu，并管理其 Popup 外观。
    /// Registers a ContextMenu owned by the specified window and manages its Popup appearance.
    /// </summary>
    /// <param name="menu">要注册的菜单。/ Menu to register.</param>
    /// <param name="owner">菜单宿主窗口。/ Menu owner window.</param>
    public void Attach(ContextMenu menu, Window owner)
    {
        if (_disposed || !_menus.Add(menu))
        {
            return;
        }

        _menuOwners[menu] = owner;
        menu.Opened += OnMenuOpened;
        owner.Closed += OnMenuOwnerClosed;
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is FluentWindow window)
        {
            RegisterWindowSource(window);
            ApplyWindow(window);
        }
    }

    private void RegisterWindowSource(FluentWindow window)
    {
        if (_windowSources.ContainsKey(window) || PresentationSource.FromVisual(window) is not HwndSource source)
        {
            return;
        }

        source.AddHook(WindowProc);
        _windowSources.Add(window, source);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FluentWindow window)
        {
            ApplyWindow(window);
        }
    }

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FluentWindow window && e.NewValue is true)
        {
            window.Dispatcher.BeginInvoke(() => ApplyWindow(window), DispatcherPriority.Loaded);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not FluentWindow window)
        {
            return;
        }

        window.SourceInitialized -= OnWindowSourceInitialized;
        window.Loaded -= OnWindowLoaded;
        window.IsVisibleChanged -= OnWindowIsVisibleChanged;
        window.Closed -= OnWindowClosed;
        _windows.Remove(window);
        if (_windowSources.Remove(window, out var source))
        {
            source.RemoveHook(WindowProc);
        }
    }

    private void OnMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            WireSubmenuHandlers(menu);
            menu.Dispatcher.BeginInvoke(() =>
            {
                ApplyMenu(menu);
                ScheduleMenuZOrderReassert(menu);
            }, DispatcherPriority.Loaded);
        }
    }

    private void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfMenuItem item)
        {
            return;
        }

        PrepareSubmenuVisual(item);
        item.Dispatcher.BeginInvoke(() =>
        {
            ApplySubmenu(item);
            ScheduleSubmenuZOrderReassert(item);
        }, DispatcherPriority.Loaded);
    }

    private void OnMenuOwnerClosed(object? sender, EventArgs e)
    {
        if (sender is not Window owner)
        {
            return;
        }

        foreach (var menu in _menuOwners.Where(pair => ReferenceEquals(pair.Value, owner)).Select(pair => pair.Key).ToArray())
        {
            UnwireSubmenuHandlers(menu);
            menu.Opened -= OnMenuOpened;
            _menuOwners.Remove(menu);
            _menus.Remove(menu);
        }

        owner.Closed -= OnMenuOwnerClosed;
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is WmSettingChange or WmThemeChanged or WmDwmCompositionChanged or
            WmDwmColorizationColorChanged or WmDpiChanged or WmDpiChangedAfterParent)
        {
            QueueApplyAll();
        }

        return nint.Zero;
    }

    private void OnAppearanceSettingsChanged(object? sender, AppearanceSettingsChangedEventArgs e) => QueueApplyAll();

    private void OnApplicationThemeChanged(ApplicationTheme theme, System.Windows.Media.Color accent) => QueueApplyAll();

    private void QueueApplyAll()
    {
        if (_disposed || _applyQueued || Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        _applyQueued = true;
        dispatcher.BeginInvoke(() =>
        {
            _applyQueued = false;
            ApplyAll();
        }, DispatcherPriority.ContextIdle);
    }

    private void ApplyAll()
    {
        foreach (var window in _windows.ToArray())
        {
            ApplyWindow(window);
        }

        foreach (var menu in _menus.Where(menu => menu.IsOpen).ToArray())
        {
            ApplyMenu(menu);
        }
    }

    private void ApplyWindow(FluentWindow window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source || source.Handle == nint.Zero)
        {
            return;
        }

        var mode = ResolveEffectiveMode(SettingsManager.Current.Appearance.BackdropMode);
        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        if (mode == ApplicationBackdropMode.FluentSolid)
        {
            _nativeBackdropAdapter.ResetBackdrop(source.Handle);
            _nativeBackdropAdapter.SetFrame(source.Handle, extended: false);
            _nativeBackdropAdapter.SetNonClientColors(source.Handle, transparent: false);
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
        }
        else
        {
            window.Background = Brushes.Transparent;
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            _nativeBackdropAdapter.ApplyBackdrop(source.Handle, mode, dark);
            _nativeBackdropAdapter.SetNonClientColors(source.Handle, transparent: true);
        }

        _nativeBackdropAdapter.SetThemeAttributes(source.Handle, dark);
    }

    private void ScheduleMenuZOrderReassert(ContextMenu menu)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input, menu.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= tick;
            if (menu.IsOpen && PresentationSource.FromVisual(menu) is HwndSource source)
            {
                _nativeBackdropAdapter.PromotePopup(source.Handle);
            }
        };
        timer.Tick += tick;
        timer.Start();
    }

    private void ScheduleSubmenuZOrderReassert(WpfMenuItem item)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input, item.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= tick;
            if (item.IsSubmenuOpen && item.Template.FindName("SubmenuBorder", item) is Border border &&
                PresentationSource.FromVisual(border) is HwndSource source)
            {
                _nativeBackdropAdapter.PromotePopup(source.Handle);
            }
        };
        timer.Tick += tick;
        timer.Start();
    }

    private void ApplyMenu(ContextMenu menu)
    {
        if (PresentationSource.FromVisual(menu) is not HwndSource source || source.Handle == nint.Zero)
        {
            return;
        }

        ApplyPopup(source, menu);
    }

    private void ApplySubmenu(WpfMenuItem item)
    {
        if (item.Template.FindName("SubmenuBorder", item) is not Border border ||
            PresentationSource.FromVisual(border) is not HwndSource source || source.Handle == nint.Zero)
        {
            return;
        }

        ApplyPopup(source, border);
    }

    private void ApplyPopup(HwndSource source, FrameworkElement surface)
    {
        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        var backgroundProperty = surface is Border
            ? Border.BackgroundProperty
            : Control.BackgroundProperty;

        // Keep Popup HWNDs opaque and Fluent-solid. Applying native Mica or
        // Acrylic here makes transparent portions of the popup miss hit
        // testing, so clicks in blank menu areas can reach the window behind it.
        source.CompositionTarget.BackgroundColor = Colors.Transparent;

        _nativeBackdropAdapter.ResetBackdrop(source.Handle);
        _nativeBackdropAdapter.SetFrame(source.Handle, extended: false);
        _nativeBackdropAdapter.SetNonClientColors(source.Handle, transparent: false);
        surface.SetResourceReference(backgroundProperty, "AppMenuBackgroundBrush");

        _nativeBackdropAdapter.SetThemeAttributes(source.Handle, dark);
        _nativeBackdropAdapter.PromotePopup(source.Handle);
    }

    private static void PrepareSubmenuVisual(WpfMenuItem item)
    {
        if (item.Template.FindName("SubmenuBorder", item) is Border border)
        {
            border.Margin = new Thickness(0);
            border.Effect = null;
            border.SetResourceReference(Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        }

        if (item.Template.FindName("Popup", item) is Popup popup)
        {
            popup.VerticalOffset = -4;
        }
    }

    private void WireSubmenuHandlers(ItemsControl parent)
    {
        foreach (var item in parent.Items.OfType<WpfMenuItem>())
        {
            item.SubmenuOpened -= OnSubmenuOpened;
            item.SubmenuOpened += OnSubmenuOpened;
            WireSubmenuHandlers(item);
        }
    }

    private void UnwireSubmenuHandlers(ItemsControl parent)
    {
        foreach (var item in parent.Items.OfType<WpfMenuItem>())
        {
            item.SubmenuOpened -= OnSubmenuOpened;
            UnwireSubmenuHandlers(item);
        }
    }

    private static ApplicationBackdropMode ResolveEffectiveMode(ApplicationBackdropMode requested)
    {
        if (SystemParameters.HighContrast)
        {
            return ApplicationBackdropMode.FluentSolid;
        }

        if (requested == ApplicationBackdropMode.Mica && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return ApplicationBackdropMode.FluentSolid;
        }

        return requested;
    }

    /// <summary>
    /// 取消事件订阅并释放已注册窗口和菜单的外观钩子。
    /// Unsubscribes events and releases appearance hooks for registered windows and menus.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SettingsManager.AppearanceSettingsChanged -= OnAppearanceSettingsChanged;
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;

        foreach (var window in _windows.ToArray())
        {
            OnWindowClosed(window, EventArgs.Empty);
        }

        foreach (var pair in _menuOwners.ToArray())
        {
            UnwireSubmenuHandlers(pair.Key);
            pair.Key.Opened -= OnMenuOpened;
            pair.Value.Closed -= OnMenuOwnerClosed;
        }

        _menus.Clear();
        _menuOwners.Clear();
    }

}
