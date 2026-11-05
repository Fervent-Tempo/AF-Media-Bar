using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services;
using Wpf.Ui.Controls;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace AFMediaBar.Views.Windows;

/// <summary>菜单关闭原因；只有用户主动选择项或按 Escape 时才尝试恢复原前台窗口。
/// Why the menu closed; the previous foreground window is restored only after an explicit item or Escape dismissal.</summary>
public enum MenuDismissReason
{
    ItemInvoked,
    Escape,
    Deactivated,
    Programmatic
}

/// <summary>菜单关闭事件参数。/ Menu dismissal event arguments.</summary>
public sealed class MenuDismissedEventArgs(MenuDismissReason reason) : EventArgs
{
    public MenuDismissReason Reason { get; } = reason;
}

/// <summary>
/// 可复用的顶层右键菜单窗口：与其它菜单共用系统材质和统一动效，同时保持现有菜单项树。
/// Reusable top-level context-menu window sharing the system backdrop and motion with the other menus while keeping the existing item tree.
/// </summary>
public partial class AFMenuWindow : FluentWindow, IDisposable
{
    private readonly WindowAppearanceService _appearanceService;
    private readonly AnchoredWindowPresentation _windowPresentation;
    private readonly List<WpfMenuItem> _wiredItems = [];
    private readonly RoutedEventHandler _menuItemClickHandler;
    private readonly KeyEventHandler _menuItemKeyHandler;
    private readonly MouseButtonEventHandler _menuItemMouseUpHandler;
    private readonly MouseEventHandler _menuItemMouseEnterHandler;
    private readonly MouseEventHandler _menuItemMouseLeaveHandler;
    private readonly DispatcherTimer _submenuHoverTimer;
    private WpfMenuItem? _pendingHoverItem;
    private FrameworkElement? _menuContent;
    private Point? _anchor;
    private nint _previousForeground;
    private int _animationVersion;
    private int _presentationVersion;
    private bool _isHiding;
    private bool _isDisposed;
    private bool _allowClose;

    public event EventHandler<MenuDismissedEventArgs>? Dismissed;

    /// <summary>菜单当前是否可见且未在执行退出动画。/ Whether the menu is visible and not animating out.</summary>
    public bool IsMenuOpen => IsVisible && !_isHiding;

    public AFMenuWindow(WindowAppearanceService appearanceService)
    {
        _appearanceService = appearanceService;
        InitializeComponent();
        _windowPresentation = new(this);
        _menuItemClickHandler = OnMenuItemClick;
        _menuItemKeyHandler = OnMenuItemPreviewKeyDown;
        _menuItemMouseUpHandler = OnMenuItemPreviewMouseLeftButtonUp;
        _menuItemMouseEnterHandler = OnMenuItemMouseEnter;
        _menuItemMouseLeaveHandler = OnMenuItemMouseLeave;
        _submenuHoverTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _submenuHoverTimer.Tick += OnSubmenuHoverTimerTick;
        ApplyMotionEffects();
        appearanceService.AttachMenuWindow(this);
    }

    /// <summary>
    /// 显示菜单内容。内容在窗口首次布局完成并定位后才可见，随后按动效策略淡入。
    /// Shows the menu content. It becomes visible only after the first layout and positioning pass, then fades in per the motion policy.
    /// </summary>
    public async void ShowMenu(FrameworkElement content, Point? screenAnchor, double width = double.NaN, bool keyboardOpened = false)
    {
        if (_isDisposed)
            return;

        if (IsVisible)
        {
            if (IsMenuOpen && ReferenceEquals(_menuContent, content))
            {
                // 已经在显示同一内容：只重新定位，不触发 Dismissed，避免边沿展开租约被提前释放。
                // Already showing the same content: only reposition, without raising Dismissed and releasing the edge lease early.
                _anchor = screenAnchor ?? GetCursorPoint();
                TryPosition(_anchor.Value);
                return;
            }
            DismissImmediately();
        }

        if (_menuContent is not null && !ReferenceEquals(_menuContent, content))
            _appearanceService.DetachMenuSubmenus(_menuContent);
        _menuContent = content;
        MenuContentPresenter.Content = content;
        Width = width;
        _anchor = screenAnchor ?? GetCursorPoint();
        _previousForeground = NativeMethods.GetForegroundWindow();
        _isHiding = false;
        var version = ++_presentationVersion;
        WireMenuContent(content);
        _appearanceService.AttachMenuSubmenus(content);
        ApplyMotionEffects();

        MenuRoot.BeginAnimation(UIElement.OpacityProperty, null);
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        var motion = MotionPolicy.ResolveCurrent();
        MenuRoot.Opacity = 0;
        MenuScale.ScaleX = MenuScale.ScaleY = motion.UseTransitions ? 0.98 : 1;

        if (!await TryShowAsync(_anchor.Value))
        {
            FinishHide(MenuDismissReason.Programmatic);
            return;
        }
        if (_isDisposed || version != _presentationVersion || !IsVisible)
            return;

        if (ShowActivated)
            Activate();

        if (!motion.UseTransitions)
        {
            MenuRoot.Opacity = 1;
        }
        else
        {
            var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };
            MenuRoot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
            MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
            MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
        }

        // 鼠标入口不把焦点给第一项，避免出现与系统菜单不同的高亮；键盘入口直接落到第一项。
        if (keyboardOpened)
            _ = Dispatcher.BeginInvoke(FocusFirstItem, DispatcherPriority.Input);
    }

    private async Task<bool> TryShowAsync(Point anchor)
    {
        try
        {
            return await _windowPresentation.ShowAsync(anchor, () => TryPosition(anchor));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AFMenuWindow] Presentation failed: {exception}");
            return false;
        }
    }

    /// <summary>按原因播放退出动画并隐藏。/ Plays the exit animation and hides the menu for the given reason.</summary>
    public void Dismiss(MenuDismissReason reason = MenuDismissReason.Programmatic)
    {
        if (_isDisposed || !IsVisible || _isHiding)
            return;

        _isHiding = true;
        var version = ++_animationVersion;
        CloseSubmenus();
        var motion = MotionPolicy.ResolveCurrent();
        if (_windowPresentation.IsPreparing || !motion.UseTransitions)
        {
            FinishHide(reason);
            return;
        }

        var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseInOut };
        MenuRoot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, motion.ExitDuration) { EasingFunction = ease });
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, motion.ExitDuration) { EasingFunction = ease });
        var close = new DoubleAnimation(0.985, motion.ExitDuration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        close.Completed += (_, _) =>
        {
            if (_isDisposed || version != _animationVersion || !IsVisible)
                return;
            FinishHide(reason);
        };
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, close, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>立即隐藏，供宿主环境变化或退出使用。/ Hides immediately for host environment changes or exit.</summary>
    public void DismissImmediately()
    {
        if (!IsVisible)
            return;

        _animationVersion++;
        _isHiding = false;
        FinishHide(MenuDismissReason.Programmatic);
    }

    private void FinishHide(MenuDismissReason reason)
    {
        _submenuHoverTimer.Stop();
        _pendingHoverItem = null;
        MenuRoot.BeginAnimation(UIElement.OpacityProperty, null);
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        MenuRoot.Opacity = 1;
        MenuScale.ScaleX = MenuScale.ScaleY = 1;
        _isHiding = false;
        if (IsVisible)
            Hide();
        _windowPresentation.Cancel();
        CloseSubmenus();
        Dismissed?.Invoke(this, new MenuDismissedEventArgs(reason));
        if (reason is MenuDismissReason.ItemInvoked or MenuDismissReason.Escape)
            RestorePreviousForeground();
    }

    private bool TryPosition(Point physicalAnchor)
    {
        var point = new NativeMethods.POINT { X = (int)physicalAnchor.X, Y = (int)physicalAnchor.Y };
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0
            ? Math.Max(1, dpiX) / 96d
            : 1d;
        MaxWidth = Math.Max(1, (info.rcWork.Right - info.rcWork.Left) / scale);
        MaxHeight = Math.Max(1, (info.rcWork.Bottom - info.rcWork.Top) / scale - 8);
        UpdateLayout();
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return false;

        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var x = physicalAnchor.X;
        var y = physicalAnchor.Y;
        if (x + width > info.rcWork.Right)
            x -= width;
        if (y + height > info.rcWork.Bottom)
            y -= height;
        x = Math.Clamp(x, info.rcWork.Left, Math.Max(info.rcWork.Left, info.rcWork.Right - width));
        y = Math.Clamp(y, info.rcWork.Top, Math.Max(info.rcWork.Top, info.rcWork.Bottom - height));
        MenuRoot.RenderTransformOrigin = y < physicalAnchor.Y ? new Point(0.5, 1) : new Point(0.5, 0);

        var handle = new WindowInteropHelper(this).Handle;
        return NativeMethods.SetWindowPos(handle, -1, (int)x, (int)y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private static Point GetCursorPoint() =>
        NativeMethods.GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(0, 0);

    private void ApplyMotionEffects()
    {
        if (MotionPolicy.ResolveCurrent().UseDecorativeEffects)
            MenuRoot.SetResourceReference(Border.EffectProperty, "AfFlyoutShadowEffect");
        else
            MenuRoot.Effect = null;
    }

    private void Window_OnDeactivated(object? sender, EventArgs e)
    {
        if (_isDisposed || _isHiding || !IsVisible || _windowPresentation.IsPreparing)
            return;

        // 子菜单 Popup 不激活窗口，失焦只可能来自同进程切换或用户点到其它窗口。
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!_isDisposed && IsVisible && !_isHiding && !IsActive && !_windowPresentation.IsPreparing)
                Dismiss(MenuDismissReason.Deactivated);
        }, DispatcherPriority.ContextIdle);
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isHiding)
            return;

        if (e.Key == Key.Escape)
        {
            Dismiss(MenuDismissReason.Escape);
            e.Handled = true;
            return;
        }

        // 鼠标打开时焦点在窗口而非菜单项；第一次方向键/Enter 落到第一项。
        if (Keyboard.FocusedElement is not WpfMenuItem && e.Key is Key.Down or Key.Up or Key.Enter or Key.Space)
        {
            FocusFirstItem();
            e.Handled = true;
        }
    }

    private void OnMenuItemPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isDisposed || _isHiding || !IsVisible || sender is not WpfMenuItem item)
            return;
        // 预览事件会从子菜单项冒泡到父菜单项；只让当前焦点项处理一次。
        // The preview event bubbles from a submenu item to its parent; only the focused item handles it once.
        if (!ReferenceEquals(e.OriginalSource, item))
            return;

        switch (e.Key)
        {
            case Key.Escape:
                Dismiss(MenuDismissReason.Escape);
                e.Handled = true;
                break;
            case Key.Down:
                MoveFocus(item, 1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveFocus(item, -1);
                e.Handled = true;
                break;
            case Key.Right:
                OpenSubmenu(item);
                e.Handled = true;
                break;
            case Key.Left:
                CloseSubmenuOrMoveOut(item);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                ActivateItem(item);
                e.Handled = true;
                break;
        }
    }

    private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_isDisposed || _isHiding || !IsVisible || e.OriginalSource is not WpfMenuItem item)
            return;

        if (item.HasItems)
        {
            OpenSubmenu(item, focusFirst: false);
            e.Handled = true;
            return;
        }

        Dismiss(MenuDismissReason.ItemInvoked);
    }

    private void OnMenuItemMouseEnter(object sender, MouseEventArgs e)
    {
        if (_isDisposed || _isHiding || !IsVisible || sender is not WpfMenuItem { HasItems: true } item)
            return;
        if (item.IsSubmenuOpen)
        {
            _pendingHoverItem = null;
            _submenuHoverTimer.Stop();
            return;
        }

        _pendingHoverItem = item;
        _submenuHoverTimer.Stop();
        _submenuHoverTimer.Start();
    }

    private void OnMenuItemMouseLeave(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(_pendingHoverItem, sender))
            return;
        _pendingHoverItem = null;
        _submenuHoverTimer.Stop();
    }

    private void OnSubmenuHoverTimerTick(object? sender, EventArgs e)
    {
        _submenuHoverTimer.Stop();
        var item = _pendingHoverItem;
        _pendingHoverItem = null;
        if (item is null || _isDisposed || _isHiding || !IsVisible)
            return;
        OpenSubmenu(item, focusFirst: false);
    }

    private void OnMenuItemPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDisposed || _isHiding || !IsVisible || e.ChangedButton != MouseButton.Left)
            return;

        // StackPanel 中的 MenuItem 没有 MenuBase 父级；其内部点击不会替我们打开子菜单。
        // 在预览阶段拦截带子项的菜单头，避免内部点击把刚打开的子菜单再次切换掉。
        // A MenuItem inside a StackPanel has no MenuBase parent, so its internal click does not open the submenu for us.
        // Intercept headers during preview so the internal click cannot toggle it again.
        var item = FindMenuItem(e.OriginalSource as DependencyObject);
        if (item is null || !item.HasItems)
            return;

        OpenSubmenu(item, focusFirst: false);
        e.Handled = true;
    }

    private static WpfMenuItem? FindMenuItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is WpfMenuItem item)
                return item;
            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private void ActivateItem(WpfMenuItem item)
    {
        if (item.HasItems)
        {
            OpenSubmenu(item);
            return;
        }

        if (item.IsCheckable)
            item.IsChecked = !item.IsChecked;
        if (item.Command is { } command && command.CanExecute(item.CommandParameter))
            command.Execute(item.CommandParameter);
        item.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent, item));
    }

    private void OpenSubmenu(WpfMenuItem item, bool focusFirst = true)
    {
        if (!item.HasItems)
            return;

        foreach (var other in _wiredItems)
        {
            if (!ReferenceEquals(other, item) && other.HasItems && other.IsSubmenuOpen)
                other.IsSubmenuOpen = false;
        }

        _pendingHoverItem = null;
        _submenuHoverTimer.Stop();
        item.IsSubmenuOpen = true;
        if (focusFirst)
            _ = Dispatcher.BeginInvoke(() => FocusFirstChild(item), DispatcherPriority.Loaded);
    }

    private void CloseSubmenuOrMoveOut(WpfMenuItem item)
    {
        if (TryGetParentMenu(item, out var parent) && parent is { HasItems: true })
        {
            parent.IsSubmenuOpen = false;
            Keyboard.Focus(parent);
            return;
        }

        if (item.IsSubmenuOpen)
            item.IsSubmenuOpen = false;
    }

    private void MoveFocus(WpfMenuItem current, int delta)
    {
        var siblings = GetSiblings(current);
        var index = siblings.IndexOf(current);
        if (index < 0)
            return;

        for (var i = index + delta; i >= 0 && i < siblings.Count; i += delta)
        {
            var candidate = siblings[i];
            if (!candidate.IsVisible || !candidate.IsEnabled || candidate.Visibility != Visibility.Visible)
                continue;
            Keyboard.Focus(candidate);
            return;
        }
    }

    private List<WpfMenuItem> GetSiblings(WpfMenuItem item)
    {
        if (TryGetParentMenu(item, out var parent) && parent is not null)
            return parent.Items.OfType<WpfMenuItem>().ToList();
        return _menuContent is Panel panel ? panel.Children.OfType<WpfMenuItem>().ToList() : [];
    }

    private static bool TryGetParentMenu(WpfMenuItem item, out WpfMenuItem? parent)
    {
        parent = item.Parent as WpfMenuItem ?? LogicalTreeHelper.GetParent(item) as WpfMenuItem;
        return parent is not null;
    }

    private void FocusFirstItem()
    {
        if (_isDisposed || !IsVisible || _isHiding || _menuContent is not Panel panel)
            return;
        var first = panel.Children.OfType<WpfMenuItem>()
            .FirstOrDefault(item => item.IsVisible && item.IsEnabled && item.Visibility == Visibility.Visible);
        if (first is not null)
            Keyboard.Focus(first);
    }

    private void FocusFirstChild(WpfMenuItem parent)
    {
        if (_isDisposed || !IsVisible || _isHiding)
            return;
        var first = parent.Items.OfType<WpfMenuItem>()
            .FirstOrDefault(item => item.IsVisible && item.IsEnabled && item.Visibility == Visibility.Visible);
        if (first is not null)
            Keyboard.Focus(first);
    }

    private void WireMenuContent(FrameworkElement content)
    {
        UnwireMenuContent();
        foreach (var item in EnumerateMenuItems(content))
        {
            item.PreviewKeyDown += _menuItemKeyHandler;
            item.MouseEnter += _menuItemMouseEnterHandler;
            item.MouseLeave += _menuItemMouseLeaveHandler;
            _wiredItems.Add(item);
        }
        content.AddHandler(WpfMenuItem.ClickEvent, _menuItemClickHandler, handledEventsToo: true);
        content.AddHandler(Mouse.PreviewMouseUpEvent, _menuItemMouseUpHandler, handledEventsToo: true);
    }

    private void UnwireMenuContent()
    {
        _submenuHoverTimer.Stop();
        _pendingHoverItem = null;
        foreach (var item in _wiredItems)
        {
            item.PreviewKeyDown -= _menuItemKeyHandler;
            item.MouseEnter -= _menuItemMouseEnterHandler;
            item.MouseLeave -= _menuItemMouseLeaveHandler;
        }
        _wiredItems.Clear();
        _menuContent?.RemoveHandler(WpfMenuItem.ClickEvent, _menuItemClickHandler);
        _menuContent?.RemoveHandler(Mouse.PreviewMouseUpEvent, _menuItemMouseUpHandler);
    }

    private static IEnumerable<WpfMenuItem> EnumerateMenuItems(FrameworkElement content)
    {
        IEnumerable<object> children = content switch
        {
            Panel panel => panel.Children.Cast<object>(),
            ItemsControl itemsControl => itemsControl.Items.Cast<object>(),
            _ => []
        };
        foreach (var child in children)
        {
            if (child is not WpfMenuItem item)
                continue;
            yield return item;
            foreach (var descendant in EnumerateMenuItems(item))
                yield return descendant;
        }
    }

    private void CloseSubmenus()
    {
        foreach (var item in _wiredItems)
            item.IsSubmenuOpen = false;
    }

    private void RestorePreviousForeground()
    {
        var previous = _previousForeground;
        if (previous == nint.Zero)
            return;

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_isDisposed || IsVisible)
                return;
            // 菜单命令可能已经打开了设置等窗口；此时不能把焦点抢回原应用。
            if (Application.Current.Windows.OfType<Window>().Any(window => window.IsVisible && window.IsActive))
                return;
            _ = NativeMethods.SetForegroundWindow(previous);
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>永久关闭菜单窗口并释放呈现、子菜单与事件。/ Permanently closes the menu window and releases presentation, submenus, and events.</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _presentationVersion++;
        _animationVersion++;
        _submenuHoverTimer.Stop();
        _submenuHoverTimer.Tick -= OnSubmenuHoverTimerTick;
        _windowPresentation.Dispose();
        if (_menuContent is not null)
            _appearanceService.DetachMenuSubmenus(_menuContent);
        UnwireMenuContent();
        CloseSubmenus();
        MenuRoot.BeginAnimation(UIElement.OpacityProperty, null);
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        MenuContentPresenter.Content = null;
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && !_isDisposed && Application.Current?.Dispatcher.HasShutdownStarted != true)
        {
            e.Cancel = true;
            DismissImmediately();
        }
        base.OnClosing(e);
    }
}
