using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls.Primitives;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AFMediaBar.Classes.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 让 Fluent 窗口和 WPF 弹出菜单共用同一种材质，并在 DWM 生命周期变化后恢复材质。
/// Keeps Fluent windows and WPF popup menus on one selected backdrop and restores it after DWM lifecycle changes.
/// </summary>
public sealed class WindowAppearanceService : IDisposable
{
    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;
    private const int WmDwmCompositionChanged = 0x031E;
    private const int WmDwmColorizationColorChanged = 0x0320;
    private const int WmDpiChanged = 0x02E0;
    private const int WmDpiChangedAfterParent = 0x02E3;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int DwmMicaEffect = 1029;
    private const int DwmBackdropNone = 1;
    private const int DwmBackdropMica = 2;
    private const int DwmBackdropAcrylic = 3;
    private const int DwmCornerRound = 2;

    private readonly HashSet<FluentWindow> _windows = [];
    private readonly Dictionary<FluentWindow, HwndSource> _windowSources = [];
    private readonly HashSet<ContextMenu> _menus = [];
    private readonly Dictionary<ContextMenu, Window> _menuOwners = [];
    private bool _applyQueued;
    private bool _disposed;

    public WindowAppearanceService()
    {
        SettingsManager.AppearanceSettingsChanged += OnAppearanceSettingsChanged;
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
    }

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
            menu.Dispatcher.BeginInvoke(() => ApplyMenu(menu), DispatcherPriority.Loaded);
        }
    }

    private void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfMenuItem item)
        {
            return;
        }

        PrepareSubmenuVisual(item);
        item.Dispatcher.BeginInvoke(() => ApplySubmenu(item), DispatcherPriority.Loaded);
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

    private static void ApplyWindow(FluentWindow window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source || source.Handle == nint.Zero)
        {
            return;
        }

        var mode = ResolveEffectiveMode(SettingsManager.Current.Appearance.BackdropMode);
        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        if (mode == ApplicationBackdropMode.FluentSolid)
        {
            ResetNativeBackdrop(source.Handle);
            SetFrame(source.Handle, extended: false);
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
        }
        else
        {
            window.Background = Brushes.Transparent;
            ApplyNativeBackdrop(source, mode, dark);
        }

        SetDwmAttribute(source.Handle, DwmUseImmersiveDarkMode, dark ? 1 : 0);
        SetDwmAttribute(source.Handle, DwmWindowCornerPreference, DwmCornerRound);
    }

    private static void ApplyMenu(ContextMenu menu)
    {
        if (PresentationSource.FromVisual(menu) is not HwndSource source || source.Handle == nint.Zero)
        {
            return;
        }

        ApplyPopup(source, menu);
    }

    private static void ApplySubmenu(WpfMenuItem item)
    {
        if (item.Template.FindName("SubmenuBorder", item) is not Border border ||
            PresentationSource.FromVisual(border) is not HwndSource source || source.Handle == nint.Zero)
        {
            return;
        }

        ApplyPopup(source, border);
    }

    private static void ApplyPopup(HwndSource source, FrameworkElement surface)
    {
        var mode = ResolveEffectiveMode(SettingsManager.Current.Appearance.BackdropMode);
        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        var backgroundProperty = surface is Border
            ? Border.BackgroundProperty
            : Control.BackgroundProperty;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;

        if (mode == ApplicationBackdropMode.FluentSolid)
        {
            ResetNativeBackdrop(source.Handle);
            SetFrame(source.Handle, extended: false);
            surface.SetResourceReference(backgroundProperty, "AppMenuBackgroundBrush");
        }
        else
        {
            surface.SetValue(backgroundProperty, Brushes.Transparent);
            ApplyNativeBackdrop(source, mode, dark);
        }

        SetDwmAttribute(source.Handle, DwmUseImmersiveDarkMode, dark ? 1 : 0);
        SetDwmAttribute(source.Handle, DwmWindowCornerPreference, DwmCornerRound);
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

    private static void ApplyNativeBackdrop(HwndSource source, ApplicationBackdropMode mode, bool dark)
    {
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        ResetNativeBackdrop(source.Handle);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            SetFrame(source.Handle, extended: true);
            SetDwmAttribute(source.Handle, DwmSystemBackdropType,
                mode == ApplicationBackdropMode.Mica ? DwmBackdropMica : DwmBackdropAcrylic);
            return;
        }

        if (mode == ApplicationBackdropMode.Mica)
        {
            SetFrame(source.Handle, extended: true);
            SetDwmAttribute(source.Handle, DwmMicaEffect, 1);
            return;
        }

        SetFrame(source.Handle, extended: false);
        ApplyLegacyAcrylic(source.Handle, dark);
    }

    private static void ResetNativeBackdrop(nint handle)
    {
        SetDwmAttribute(handle, DwmSystemBackdropType, DwmBackdropNone);
        SetDwmAttribute(handle, DwmMicaEffect, 0);
        ApplyAccentPolicy(handle, AccentState.Disabled, 0);
    }

    private static void SetFrame(nint handle, bool extended)
    {
        var margins = extended
            ? new DwmMargins(-1, -1, -1, -1)
            : new DwmMargins(0, 0, 0, 0);
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);
    }

    private static void SetDwmAttribute(nint handle, int attribute, int value) =>
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    private static void ApplyLegacyAcrylic(nint handle, bool dark)
    {
        var tint = dark ? unchecked((int)0xCC202020) : unchecked((int)0xCCF9F9F9);
        ApplyAccentPolicy(handle, AccentState.EnableAcrylicBlurBehind, tint);
    }

    private static unsafe void ApplyAccentPolicy(nint handle, AccentState state, int gradientColor)
    {
        var policy = new AccentPolicy
        {
            State = state,
            GradientColor = gradientColor
        };
        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.AccentPolicy,
            Data = (nint)(&policy),
            SizeOfData = sizeof(AccentPolicy)
        };
        _ = SetWindowCompositionAttribute(handle, ref data);
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmMargins
    {
        public DwmMargins(int left, int right, int top, int bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    private enum AccentState
    {
        Disabled = 0,
        EnableAcrylicBlurBehind = 4
    }

    private enum WindowCompositionAttribute
    {
        AccentPolicy = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState State;
        public int Flags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint handle, ref DwmMargins margins);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(nint handle, ref WindowCompositionAttributeData data);
}
