// 侧栏分类子项的展开/折叠动画；窗口持有它并在关闭时释放，动画只改呈现，不读写导航状态。
// Owns the sidebar categories' expand/collapse clocks; the settings window disposes it on close.
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;
using Wpf.Ui.Controls;
using NavigationViewItem = Wpf.Ui.Controls.NavigationViewItem;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 为设置侧栏分类项的高度、透明度和箭头补充展开/折叠动画，并遵守当前系统动效级别。
/// Animates the settings sidebar category height, opacity, and chevron while following the current motion policy.
/// </summary>
public sealed class SettingsSidebarExpansionAnimator : IDisposable
{
    private const double ExpandedAngle = 180d;
    private const string PresenterPart = "MenuItemsPresenter";
    private const string ChevronPart = "ChevronIcon";
    private static readonly TimeSpan _expandDuration = TimeSpan.FromMilliseconds(180);
    private static readonly DependencyPropertyDescriptor? _expandedDescriptor =
        DependencyPropertyDescriptor.FromProperty(NavigationViewItem.IsExpandedProperty, typeof(NavigationViewItem));

    private readonly NavigationView _navigation;
    private readonly Dictionary<NavigationViewItem, ItemSubscription> _items = new();
    private bool _disposed;

    /// <summary>挂接导航级事件；菜单项本身由 <see cref="Sync"/> 挂接。</summary>
    public SettingsSidebarExpansionAnimator(NavigationView navigation)
    {
        _navigation = navigation;
        navigation.PaneOpened += OnPaneChanged;
        navigation.PaneClosed += OnPaneChanged;
        navigation.Loaded += OnNavigationLoaded;
        navigation.Unloaded += OnNavigationUnloaded;
    }

    /// <summary>
    /// 菜单项重建后重新挂接订阅，并按当前展开状态直接落地，避免上下文切换补播动画。
    /// Re-attaches after the menu items are rebuilt and settles the current state without replaying motion.
    /// </summary>
    public void Sync()
    {
        if (_disposed) return;
        foreach (var item in _items.Keys.ToArray()) Detach(item);
        foreach (var item in Enumerate(_navigation.MenuItems.OfType<NavigationViewItem>())
                     .Concat(Enumerate(_navigation.FooterMenuItems.OfType<NavigationViewItem>())))
        {
            if (item.MenuItems.Count == 0) continue;
            Attach(item);
            Apply(item, animate: false);
        }
    }

    private static IEnumerable<NavigationViewItem> Enumerate(IEnumerable<NavigationViewItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Enumerate(item.MenuItems.OfType<NavigationViewItem>())) yield return child;
        }
    }

    private void Attach(NavigationViewItem item)
    {
        if (_items.ContainsKey(item)) return;
        var subscription = new ItemSubscription();
        if (_expandedDescriptor is not null)
        {
            subscription.Changed = (_, _) => Apply(item, animate: true);
            _expandedDescriptor.AddValueChanged(item, subscription.Changed);
        }
        subscription.Loaded = (_, _) => Apply(item, animate: false);
        subscription.Unloaded = (_, _) => Reset(item);
        item.Loaded += subscription.Loaded;
        item.Unloaded += subscription.Unloaded;
        _items[item] = subscription;
    }

    private void Detach(NavigationViewItem item)
    {
        if (!_items.Remove(item, out var subscription)) return;
        if (_expandedDescriptor is not null && subscription.Changed is not null)
            _expandedDescriptor.RemoveValueChanged(item, subscription.Changed);
        if (subscription.Loaded is not null) item.Loaded -= subscription.Loaded;
        if (subscription.Unloaded is not null) item.Unloaded -= subscription.Unloaded;
        // 代际加一让旧动画的 Completed 失效，再清理可能挂在该项上的时钟与本地值。
        subscription.Generation++;
        Restore(item);
    }

    private void Apply(NavigationViewItem item, bool animate)
    {
        if (_disposed || !_items.TryGetValue(item, out var subscription)) return;
        if (!TryResolve(item, out var presenter, out var chevron)) return;
        var generation = ++subscription.Generation;
        var expanded = item.IsExpanded;
        var paneOpen = _navigation.IsPaneOpen;
        var motion = MotionPolicy.ResolveCurrent();
        if (!animate || !paneOpen || motion.Mode == MotionMode.Instant)
        {
            Settle(presenter, chevron, expanded && paneOpen, paneHidden: !paneOpen);
            return;
        }

        // 折叠从当前帧接续；展开先恢复自然高度才能量到目标高度。
        var fromHeight = CurrentHeight(presenter);
        var fromOpacity = presenter.Visibility == Visibility.Visible ? presenter.Opacity : 0d;
        var fromAngle = chevron?.Angle ?? 0d;
        presenter.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
        presenter.ClearValue(FrameworkElement.MaxHeightProperty);
        presenter.Visibility = Visibility.Visible;
        var targetHeight = expanded ? NaturalHeight(item, presenter) : 0d;
        if (expanded && targetHeight <= 0d)
        {
            Settle(presenter, chevron, expanded: true, paneHidden: false);
            return;
        }
        if (!expanded && fromHeight <= 0d)
        {
            Settle(presenter, chevron, expanded: false, paneHidden: false);
            return;
        }

        var duration = motion.Mode == MotionMode.Full ? _expandDuration : motion.StandardDuration;
        var spline = ResolveSpline();
        if (motion.Mode == MotionMode.Full)
        {
            var height = Create(fromHeight, targetHeight, duration, spline);
            height.Completed += (_, _) => Finish(item, generation, presenter, chevron, expanded);
            presenter.BeginAnimation(FrameworkElement.MaxHeightProperty, height, HandoffBehavior.SnapshotAndReplace);
            presenter.BeginAnimation(UIElement.OpacityProperty,
                Create(fromOpacity, expanded ? 1d : 0d, duration, spline), HandoffBehavior.SnapshotAndReplace);
            if (chevron is not null)
                chevron.BeginAnimation(RotateTransform.AngleProperty,
                    Create(fromAngle, expanded ? ExpandedAngle : 0d, duration, spline), HandoffBehavior.SnapshotAndReplace);
            return;
        }

        // Reduced：高度与箭头直接落地，只保留透明度过渡。
        presenter.MaxHeight = targetHeight;
        if (chevron is not null) chevron.Angle = expanded ? ExpandedAngle : 0d;
        var fade = Create(fromOpacity, expanded ? 1d : 0d, duration, spline);
        fade.Completed += (_, _) => Finish(item, generation, presenter, chevron, expanded);
        presenter.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    private void Finish(NavigationViewItem item, int generation, FrameworkElement presenter, RotateTransform? chevron, bool expanded)
    {
        if (_disposed || !_items.TryGetValue(item, out var subscription) || subscription.Generation != generation) return;
        subscription.Generation++;
        Settle(presenter, chevron, expanded, paneHidden: false);
    }

    private double NaturalHeight(NavigationViewItem item, FrameworkElement presenter)
    {
        _navigation.UpdateLayout();
        if (double.IsFinite(presenter.ActualHeight) && presenter.ActualHeight > 0d) return presenter.ActualHeight;
        var width = item.ActualWidth > 0d && !double.IsNaN(item.ActualWidth)
            ? Math.Max(0d, item.ActualWidth - presenter.Margin.Left)
            : double.PositiveInfinity;
        presenter.Measure(new Size(width, double.PositiveInfinity));
        var desired = presenter.DesiredSize.Height;
        return double.IsFinite(desired) && desired > 0d ? desired : 0d;
    }

    private static double CurrentHeight(FrameworkElement presenter)
    {
        var maxHeight = presenter.MaxHeight;
        if (double.IsFinite(maxHeight)) return Math.Max(0d, maxHeight);
        return presenter.Visibility == Visibility.Visible ? Math.Max(0d, presenter.ActualHeight) : 0d;
    }

    private static void Settle(FrameworkElement presenter, RotateTransform? chevron, bool expanded, bool paneHidden)
    {
        presenter.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
        presenter.BeginAnimation(UIElement.OpacityProperty, null);
        presenter.ClearValue(FrameworkElement.MaxHeightProperty);
        presenter.ClearValue(UIElement.OpacityProperty);
        if (chevron is not null)
        {
            chevron.BeginAnimation(RotateTransform.AngleProperty, null);
            chevron.Angle = expanded && !paneHidden ? ExpandedAngle : 0d;
        }
        if (paneHidden)
        {
            // 收起侧栏时交回模板触发器：子项由 IsPaneOpen 触发器隐藏，不能留下本地可见值。
            presenter.ClearValue(UIElement.VisibilityProperty);
            return;
        }
        presenter.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reset(NavigationViewItem item)
    {
        if (_items.TryGetValue(item, out var subscription)) subscription.Generation++;
        Restore(item);
    }

    private static void Restore(NavigationViewItem item)
    {
        if (TryResolve(item, out var presenter, out var chevron)) Settle(presenter, chevron, expanded: false, paneHidden: true);
    }

    private static bool TryResolve(NavigationViewItem item, out FrameworkElement presenter, out RotateTransform? chevron)
    {
        presenter = null!;
        chevron = null;
        var template = item.Template;
        if (template is null) return false;
        FrameworkElement? found;
        try { found = template.FindName(PresenterPart, item) as FrameworkElement; }
        catch (InvalidOperationException) { return false; }
        if (found is null) return false;
        presenter = found;
        FrameworkElement? icon;
        try { icon = template.FindName(ChevronPart, item) as FrameworkElement; }
        catch (InvalidOperationException) { return false; }
        chevron = ResolveChevron(icon);
        return true;
    }

    private static RotateTransform? ResolveChevron(FrameworkElement? icon)
    {
        if (icon?.RenderTransform is not RotateTransform transform) return null;
        if (!transform.IsFrozen) return transform;
        // 模板里的 Freezable 会被 WPF 冻结；动画需要自己持有可写实例，初值与模板保持一致。
        var owned = new RotateTransform(transform.Angle);
        icon.RenderTransform = owned;
        return owned;
    }

    private KeySpline ResolveSpline() =>
        _navigation.TryFindResource("AfSplineEaseInOut") as KeySpline ?? new KeySpline(0.76d, 0d, 0.24d, 1d);

    private static DoubleAnimationUsingKeyFrames Create(double from, double to, TimeSpan duration, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.Zero, Duration = duration, FillBehavior = FillBehavior.HoldEnd
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
        return animation;
    }

    private void OnPaneChanged(NavigationView sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        if (!_navigation.IsPaneOpen)
        {
            foreach (var item in _items.Keys.ToArray()) Reset(item);
            return;
        }
        Sync();
    }

    private void OnNavigationLoaded(object sender, RoutedEventArgs args) => Sync();
    private void OnNavigationUnloaded(object sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        foreach (var item in _items.Keys.ToArray()) Reset(item);
    }

    /// <summary>释放导航事件、菜单项订阅与全部动画时钟；重复调用无效。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _navigation.PaneOpened -= OnPaneChanged;
        _navigation.PaneClosed -= OnPaneChanged;
        _navigation.Loaded -= OnNavigationLoaded;
        _navigation.Unloaded -= OnNavigationUnloaded;
        foreach (var item in _items.Keys.ToArray()) Detach(item);
    }

    private sealed class ItemSubscription
    {
        public EventHandler? Changed { get; set; }
        public RoutedEventHandler? Loaded { get; set; }
        public RoutedEventHandler? Unloaded { get; set; }
        public int Generation { get; set; }
    }
}
