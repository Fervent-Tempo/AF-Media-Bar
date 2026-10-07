// Owns the settings sidebar's shared selection surface and its clocks; the window disposes it on close.
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using Wpf.Ui.Controls;
using NavigationViewItem = Wpf.Ui.Controls.NavigationViewItem;

namespace AFMediaBar.Classes.Utils;

/// <summary>Highlights the active sidebar item and moves one shared selection surface continuously between destinations.</summary>
public sealed class SettingsSidebarSelectionAnimator : IDisposable
{
    private readonly NavigationView _navigation;
    private readonly Canvas _overlay;
    private readonly Border _surface;
    private readonly TranslateTransform _position = new();
    private readonly ScaleTransform _scale = new();
    private DispatcherOperation? _pending;
    private NavigationViewItem? _target;
    private Rect _targetBounds = Rect.Empty;
    private bool _disposed;
    private int _generation;

    /// <summary>Attaches presentation-only selection feedback to the navigation view and its non-interactive overlay.</summary>
    public SettingsSidebarSelectionAnimator(NavigationView navigation, Canvas overlay)
    {
        _navigation = navigation;
        _overlay = overlay;
        _surface = new Border { CornerRadius = new CornerRadius(7d), BorderThickness = new Thickness(1d), Visibility = Visibility.Collapsed };
        _surface.SetResourceReference(Border.BackgroundProperty, "AfAccentTintBrush");
        _surface.SetResourceReference(Border.BorderBrushProperty, "AfAccentBrush");
        var rail = new Border { Width = 4d, Margin = new Thickness(3d, 7d, 0d, 7d), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2d) };
        rail.SetResourceReference(Border.BackgroundProperty, "AfAccentBrush");
        _surface.Child = rail;
        var transform = new TransformGroup();
        transform.Children.Add(_scale);
        transform.Children.Add(_position);
        _surface.RenderTransform = transform;
        _overlay.Children.Add(_surface);
        navigation.SelectionChanged += OnSelectionChanged;
        navigation.PaneOpened += OnPaneChanged;
        navigation.PaneClosed += OnPaneChanged;
        navigation.Loaded += OnLoaded;
        navigation.LayoutUpdated += OnLayoutUpdated;
        navigation.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged), true);
        SystemParameters.StaticPropertyChanged += OnEnvironmentChanged;
        Schedule(animate: false);
    }

    private void OnSelectionChanged(NavigationView sender, RoutedEventArgs args) => Schedule(animate: true);
    private void OnPaneChanged(NavigationView sender, RoutedEventArgs args) => Schedule(animate: false);
    private void OnLoaded(object sender, RoutedEventArgs args) => Schedule(animate: false);
    private void OnLayoutUpdated(object? sender, EventArgs args) => Align(animate: false);
    private void OnScrollChanged(object sender, ScrollChangedEventArgs args) => Align(animate: false);
    private void OnEnvironmentChanged(object? sender, PropertyChangedEventArgs args) => Schedule(animate: false, force: true);

    private void Schedule(bool animate, bool force = false)
    {
        if (_disposed) return;
        _pending?.Abort();
        _pending = _navigation.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _pending = null;
            if (!_disposed) Align(animate, force);
        }));
    }

    private void Align(bool animate, bool force = false)
    {
        if (_disposed || !_navigation.IsLoaded) return;
        if (_navigation.SelectedItem is not NavigationViewItem item || !item.IsVisible || item.ActualWidth < 8d || item.ActualHeight < 8d)
        {
            Stop();
            _surface.Visibility = Visibility.Collapsed;
            _target = null;
            _targetBounds = Rect.Empty;
            return;
        }
        Point origin;
        try { origin = item.TranslatePoint(new Point(), _overlay); }
        catch (InvalidOperationException) { return; }
        var bounds = new Rect(origin.X + 2d, origin.Y + 2d, item.ActualWidth - 4d, item.ActualHeight - 4d);
        DependencyObject? ancestor = VisualTreeHelper.GetParent(item);
        while (ancestor is not null && ancestor is not ScrollViewer && !ReferenceEquals(ancestor, _navigation))
            ancestor = VisualTreeHelper.GetParent(ancestor);
        if (ancestor is ScrollViewer viewport)
        {
            var viewportOrigin = viewport.TranslatePoint(new Point(), _overlay);
            _overlay.Clip = new RectangleGeometry(new Rect(viewportOrigin, viewport.RenderSize));
        }
        else _overlay.Clip = null;
        if (!force && ReferenceEquals(item, _target) && bounds == _targetBounds) return;
        // LayoutUpdated can precede the deferred selection callback; changing item still deserves a transition.
        animate |= _target is not null && !ReferenceEquals(item, _target);
        var previousX = _position.X;
        var previousY = _position.Y;
        var previousWidth = _surface.Width * _scale.ScaleX;
        var previousHeight = _surface.Height * _scale.ScaleY;
        var wasVisible = _surface.Visibility == Visibility.Visible;
        Stop();
        _target = item;
        _targetBounds = bounds;
        foreach (var candidate in _navigation.MenuItems.OfType<NavigationViewItem>().Concat(_navigation.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            candidate.SetResourceReference(Control.ForegroundProperty, ReferenceEquals(candidate, item) ? "AfAccentBrush" : "TextFillColorPrimaryBrush");
            candidate.SetResourceReference(Control.FontWeightProperty, ReferenceEquals(candidate, item) ? "AppTextStrongFontWeight" : "AppTextFontWeight");
            // Icons are separate elements and do not inherit the item's local foreground in the library template.
            candidate.Icon?.SetResourceReference(IconElement.ForegroundProperty,
                ReferenceEquals(candidate, item) ? "AfAccentBrush" : "TextFillColorPrimaryBrush");
        }
        _surface.Width = bounds.Width;
        _surface.Height = bounds.Height;
        _surface.Visibility = Visibility.Visible;
        _position.X = bounds.X;
        _position.Y = bounds.Y;
        _scale.ScaleX = 1d;
        _scale.ScaleY = 1d;
        var motion = MotionPolicy.ResolveCurrent();
        if (!animate || !wasVisible || motion.Mode != MotionMode.Full || !double.IsFinite(previousWidth) || previousWidth <= 0d) return;
        var duration = TimeSpan.FromMilliseconds(280);
        var spline = _navigation.TryFindResource("AfSplineEaseInOut") as KeySpline ?? new KeySpline(0.76d, 0d, 0.24d, 1d);
        _position.BeginAnimation(TranslateTransform.XProperty, Create(previousX, bounds.X, duration, spline), HandoffBehavior.SnapshotAndReplace);
        _position.BeginAnimation(TranslateTransform.YProperty, Create(previousY, bounds.Y, duration, spline), HandoffBehavior.SnapshotAndReplace);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Create(previousWidth / bounds.Width, 1d, duration, spline), HandoffBehavior.SnapshotAndReplace);
        var end = Create(previousHeight / bounds.Height, 1d, duration, spline);
        var generation = _generation;
        end.Completed += (_, _) => { if (generation == _generation) Stop(); };
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, end, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimationUsingKeyFrames Create(double from, double to, TimeSpan duration, KeySpline spline)
    {
        var result = new DoubleAnimationUsingKeyFrames { Duration = duration, BeginTime = TimeSpan.Zero, FillBehavior = FillBehavior.Stop };
        result.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        result.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
        return result;
    }

    private void Stop()
    {
        _generation++;
        _position.BeginAnimation(TranslateTransform.XProperty, null);
        _position.BeginAnimation(TranslateTransform.YProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    /// <summary>Releases navigation, environment, and layout subscriptions and removes the owned overlay.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pending?.Abort();
        _pending = null;
        _navigation.SelectionChanged -= OnSelectionChanged;
        _navigation.PaneOpened -= OnPaneChanged;
        _navigation.PaneClosed -= OnPaneChanged;
        _navigation.Loaded -= OnLoaded;
        _navigation.LayoutUpdated -= OnLayoutUpdated;
        _navigation.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SystemParameters.StaticPropertyChanged -= OnEnvironmentChanged;
        Stop();
        _overlay.Children.Remove(_surface);
    }
}
