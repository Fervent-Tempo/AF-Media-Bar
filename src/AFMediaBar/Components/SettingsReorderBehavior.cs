// 每个 ItemsControl 拥有一个排序状态；卸载、取消及提交统一释放预览、捕获、计时器和订阅。
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Resources;

namespace AFMediaBar.Components;

/// <summary>让设置列表通过手柄预览排序，仅在有效释放或键盘调整时发出一次移动命令。</summary>
public static class SettingsReorderBehavior
{
    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.RegisterAttached(
        "MoveCommand", typeof(ICommand), typeof(SettingsReorderBehavior), new PropertyMetadata(null, OnCommandChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(ReorderState), typeof(SettingsReorderBehavior), new PropertyMetadata(null));

    public static ICommand? GetMoveCommand(DependencyObject element) => (ICommand?)element.GetValue(MoveCommandProperty);
    public static void SetMoveCommand(DependencyObject element, ICommand? value) => element.SetValue(MoveCommandProperty, value);

    private static void OnCommandChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ItemsControl list) return;
        (list.GetValue(StateProperty) as ReorderState)?.Dispose();
        list.SetValue(StateProperty, args.NewValue is ICommand ? new ReorderState(list) : null);
    }

    private sealed class ReorderState : IDisposable
    {
        private readonly ItemsControl _list;
        private readonly DispatcherTimer _scrollTimer;
        private Window? _window;
        private ScrollViewer? _scroll;
        private SettingsReorderHandle? _handle;
        private object? _item;
        private object[] _snapshot = [];
        private FrameworkElement[] _rows = [];
        private Transform[] _originalTransforms = [];
        private TranslateTransform[] _translations = [];
        private double[] _tops = [];
        private double[] _extents = [];
        private double[] _targets = [];
        private Point _pressPoint;
        private double _pointerOffset;
        private double _originalOpacity;
        private int _source;
        private int _target;
        private int _minimum;
        private int _generation;
        private bool _attached;
        private bool _active;
        private AdornerLayer? _layer;
        private DragPreview? _preview;
        private long _lastScrollTick;
        private double _scrollVelocity;

        internal ReorderState(ItemsControl list)
        {
            _list = list;
            _scrollTimer = new DispatcherTimer(DispatcherPriority.Input, list.Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
            _scrollTimer.Tick += OnScrollTick;
            list.Loaded += OnLoaded;
            list.Unloaded += OnUnloaded;
            if (list.IsLoaded) Attach();
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => Attach();
        private void OnUnloaded(object sender, RoutedEventArgs args) => Detach();

        private void Attach()
        {
            if (_attached) return;
            _attached = true;
            _list.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnStarted));
            _list.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(OnDelta));
            _list.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnCompleted));
            _list.PreviewKeyDown += OnKeyDown;
            _list.SizeChanged += OnSizeChanged;
            _list.IsVisibleChanged += OnAvailabilityChanged;
            _list.IsEnabledChanged += OnAvailabilityChanged;
            ((INotifyCollectionChanged)_list.Items).CollectionChanged += OnItemsChanged;
            _list.ItemContainerGenerator.StatusChanged += OnContainersChanged;
            _window = Window.GetWindow(_list);
            if (_window is not null)
            {
                _window.Deactivated += OnDeactivated;
                _window.Closed += OnDeactivated;
            }
            RefreshPositions();
        }

        private void OnStarted(object sender, DragStartedEventArgs args)
        {
            if (args.OriginalSource is not SettingsReorderHandle { IsEnabled: true } handle) return;
            Start(handle, Mouse.GetPosition(_list));
            args.Handled = true;
        }

        private void Start(SettingsReorderHandle handle, Point pointer)
        {
            Cancel();
            _handle = handle;
            _item = handle.DataContext;
            _snapshot = _list.Items.Cast<object>().ToArray();
            _source = Array.IndexOf(_snapshot, _item);
            if (_source < 0) { Cancel(); return; }
            _pressPoint = pointer;
            _target = _source;
            handle.Focus();
        }

        private void OnDelta(object sender, DragDeltaEventArgs args)
        {
            MovePointer(Mouse.GetPosition(_list));
            args.Handled = true;
        }

        private void MovePointer(Point pointer)
        {
            if (_handle is null) return;
            if (!_handle.IsEnabled || !_handle.IsVisible || !_list.IsEnabled || !_list.IsVisible) { Cancel(); return; }
            if (!_active && Math.Abs(pointer.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance &&
                Math.Abs(pointer.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            if (!_active && !BeginPreview()) { Cancel(); return; }
            UpdatePreview(pointer);
        }

        private bool BeginPreview()
        {
            _layer = AdornerLayer.GetAdornerLayer(_list);
            _rows = Enumerable.Range(0, _snapshot.Length).Select(index => _list.ItemContainerGenerator.ContainerFromIndex(index)).OfType<FrameworkElement>().ToArray();
            if (_layer is null || _rows.Length != _snapshot.Length) return false;
            _minimum = Array.FindIndex(_rows, row => FindHandle(row) is { IsVisible: true, IsEnabled: true });
            if (_minimum < 0 || _source < _minimum) return false;
            _tops = _rows.Select(row => row.TranslatePoint(new Point(), _list).Y).ToArray();
            _extents = _rows.Select((row, index) => index + 1 < _rows.Length ? _tops[index + 1] - _tops[index] : row.ActualHeight).ToArray();
            if (_extents.Any(height => height <= 0)) return false;
            var source = _rows[_source];
            if (source.ActualWidth <= 0 || source.ActualHeight <= 0) return false;
            var dpi = VisualTreeHelper.GetDpi(source);
            var image = new RenderTargetBitmap((int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            image.Render(source);
            image.Freeze();
            _preview = new DragPreview(_list, image, source.RenderSize);
            _pointerOffset = _pressPoint.Y - _tops[_source];
            _originalTransforms = _rows.Select(row => row.RenderTransform).ToArray();
            _translations = _rows.Select(_ => new TranslateTransform()).ToArray();
            _targets = new double[_rows.Length];
            for (var index = 0; index < _rows.Length; index++)
                _rows[index].SetCurrentValue(UIElement.RenderTransformProperty, new TransformGroup { Children = { _originalTransforms[index], _translations[index] } });
            _originalOpacity = source.Opacity;
            source.SetCurrentValue(UIElement.OpacityProperty, 0d);
            _active = true;
            _layer.Add(_preview);
            _scroll = Ancestor<ScrollViewer>(_list);
            if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;
            return true;
        }

        private void UpdatePreview(Point pointer)
        {
            if (!_active || _preview is null) return;
            var top = pointer.Y - _pointerOffset;
            var target = SettingsReorderLayout.FindTarget(_tops, _extents, _source, top + _extents[_source] / 2d, _minimum);
            if (target != _target)
            {
                _target = target;
                var offsets = SettingsReorderLayout.GetOffsets(_tops, _extents, _source, _target);
                var motion = MotionPolicy.ResolveCurrent();
                for (var index = 0; index < _rows.Length; index++)
                {
                    if (index == _source || _targets[index] == offsets[index]) continue;
                    _targets[index] = offsets[index];
                    var translation = _translations[index];
                    var from = translation.Y;
                    translation.BeginAnimation(TranslateTransform.YProperty, null);
                    translation.Y = offsets[index];
                    if (motion.UseTransitions)
                        translation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, offsets[index],
                            TimeSpan.FromMilliseconds(Math.Min(160d, motion.FastDuration.TotalMilliseconds)))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                }
            }
            var viewport = VisibleBounds();
            _preview.Update(top, _scroll is null ? viewport : ScrollBounds());
            _scrollVelocity = 0;
            var scrollBounds = ScrollBounds();
            if (_scroll is not null && viewport.Contains(pointer) && scrollBounds.Contains(pointer))
            {
                if (pointer.Y < scrollBounds.Top + 32d) _scrollVelocity = -480d * Math.Clamp((scrollBounds.Top + 32d - pointer.Y) / 32d, 0d, 1d);
                else if (pointer.Y > scrollBounds.Bottom - 32d) _scrollVelocity = 480d * Math.Clamp((pointer.Y - scrollBounds.Bottom + 32d) / 32d, 0d, 1d);
                if ((_scrollVelocity < 0 && _scroll.VerticalOffset <= 0) || (_scrollVelocity > 0 && _scroll.VerticalOffset >= _scroll.ScrollableHeight))
                    _scrollVelocity = 0;
            }
            if (_scrollVelocity != 0 && !_scrollTimer.IsEnabled)
            {
                _lastScrollTick = Stopwatch.GetTimestamp();
                _scrollTimer.Start();
            }
            else if (_scrollVelocity == 0) _scrollTimer.Stop();
        }

        private Rect VisibleBounds()
        {
            var bounds = new Rect(_list.RenderSize);
            if (_scroll is null) return bounds;
            bounds.Intersect(ScrollBounds());
            return bounds;
        }

        private Rect ScrollBounds()
        {
            if (_scroll is null) return new Rect(_list.RenderSize);
            var viewport = _scroll.Template?.FindName("PART_ScrollContentPresenter", _scroll) as FrameworkElement ?? _scroll;
            return new Rect(viewport.TranslatePoint(new Point(), _list), viewport.RenderSize);
        }

        private void OnCompleted(object sender, DragCompletedEventArgs args)
        {
            Complete(Mouse.GetPosition(_list), args.Canceled);
            args.Handled = true;
        }

        private void Complete(Point pointer, bool canceled)
        {
            if (_handle is null) return;
            if (_active) UpdatePreview(pointer);
            var request = _active && !canceled && _handle.IsEnabled && _handle.IsVisible && VisibleBounds().Contains(pointer) && _item is not null
                ? new SettingsReorderRequest(_item, _target, _snapshot) : null;
            var key = _handle.ItemKey;
            Cancel();
            if (request is not null) Execute(request, key);
        }

        private void OnKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape && _handle is not null) { Cancel(); args.Handled = true; return; }
            if (Keyboard.FocusedElement is not SettingsReorderHandle handle || (Keyboard.Modifiers & ModifierKeys.Alt) == 0) return;
            var key = args.Key == Key.System ? args.SystemKey : args.Key;
            if (key is not (Key.Up or Key.Down)) return;
            MoveByKeyboard(handle, key == Key.Up ? -1 : 1);
            args.Handled = true;
        }

        private void MoveByKeyboard(SettingsReorderHandle handle, int offset)
        {
            if (!_attached || !_list.IsEnabled || !handle.IsEnabled || !handle.IsVisible) return;
            var items = _list.Items.Cast<object>().ToArray();
            var source = Array.IndexOf(items, handle.DataContext);
            if (source < 0) return;
            Cancel();
            Execute(new SettingsReorderRequest(items[source], source + offset, items), handle.ItemKey);
        }

        private void Execute(SettingsReorderRequest request, object? key)
        {
            var command = GetMoveCommand(_list);
            if (!_attached || command?.CanExecute(request) != true) return;
            command.Execute(request);
            var generation = _generation;
            _list.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!_attached || generation != _generation || !_list.IsVisible) return;
                _list.UpdateLayout();
                foreach (var row in Enumerable.Range(0, _list.Items.Count).Select(index => _list.ItemContainerGenerator.ContainerFromIndex(index)).OfType<FrameworkElement>())
                    if (FindHandle(row) is { } handle && Equals(handle.ItemKey, key)) { handle.Focus(); break; }
                RefreshPositions();
            }));
        }

        private void RefreshPositions()
        {
            for (var index = 0; index < _list.Items.Count; index++)
                if (_list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement row && FindHandle(row) is { } handle)
                    AutomationProperties.SetItemStatus(handle, Translations.Format("Settings.Reorder.Position", index + 1, _list.Items.Count));
        }

        private void OnScrollTick(object? sender, EventArgs args)
        {
            if (!_active || _scroll is null) { _scrollTimer.Stop(); return; }
            var elapsed = Math.Min(0.05d, Stopwatch.GetElapsedTime(_lastScrollTick).TotalSeconds);
            _lastScrollTick = Stopwatch.GetTimestamp();
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + _scrollVelocity * elapsed);
            UpdatePreview(Mouse.GetPosition(_list));
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs args) { if (_active) UpdatePreview(Mouse.GetPosition(_list)); }
        private void OnSizeChanged(object sender, SizeChangedEventArgs args) { if (_handle is not null) Cancel(); }
        private void OnAvailabilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!_list.IsVisible || !_list.IsEnabled) Cancel(); }
        private void OnContainersChanged(object? sender, EventArgs args)
        {
            if (_list.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated) RefreshPositions();
        }
        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) { Cancel(); RefreshPositions(); }
        private void OnDeactivated(object? sender, EventArgs args) => Cancel();

        private void Cancel()
        {
            _generation++;
            var handle = _handle;
            _handle = null;
            _scrollTimer.Stop();
            if (_scroll is not null) _scroll.ScrollChanged -= OnScrollChanged;
            _scroll = null;
            if (_preview is not null) _layer?.Remove(_preview);
            _preview = null;
            _layer = null;
            if (_active)
            {
                for (var index = 0; index < _rows.Length; index++)
                {
                    _translations[index].BeginAnimation(TranslateTransform.YProperty, null);
                    _rows[index].SetCurrentValue(UIElement.RenderTransformProperty, _originalTransforms[index]);
                }
                _rows[_source].SetCurrentValue(UIElement.OpacityProperty, _originalOpacity);
            }
            _active = false;
            _rows = [];
            _snapshot = [];
            _originalTransforms = [];
            _translations = [];
            _tops = [];
            _extents = [];
            _targets = [];
            _item = null;
            if (handle?.IsMouseCaptured == true) handle.ReleaseMouseCapture();
        }

        private void Detach()
        {
            Cancel();
            if (!_attached) return;
            _attached = false;
            _list.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnStarted));
            _list.RemoveHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(OnDelta));
            _list.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnCompleted));
            _list.PreviewKeyDown -= OnKeyDown;
            _list.SizeChanged -= OnSizeChanged;
            _list.IsVisibleChanged -= OnAvailabilityChanged;
            _list.IsEnabledChanged -= OnAvailabilityChanged;
            ((INotifyCollectionChanged)_list.Items).CollectionChanged -= OnItemsChanged;
            _list.ItemContainerGenerator.StatusChanged -= OnContainersChanged;
            if (_window is not null) { _window.Deactivated -= OnDeactivated; _window.Closed -= OnDeactivated; }
            _window = null;
        }

        public void Dispose()
        {
            Detach();
            _list.Loaded -= OnLoaded;
            _list.Unloaded -= OnUnloaded;
            _scrollTimer.Tick -= OnScrollTick;
        }
    }

    private sealed class DragPreview(UIElement owner, ImageSource image, Size size) : Adorner(owner)
    {
        private double _top;
        private Rect _clip;
        internal void Update(double top, Rect clip) { _top = top; _clip = clip; IsHitTestVisible = false; InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawing)
        {
            drawing.PushClip(new RectangleGeometry(_clip));
            var bounds = new Rect(new Point(0, _top), size);
            var background = TryFindResource("AfSettingsCardBrush") as Brush ?? SystemColors.WindowBrush;
            var accent = TryFindResource("AfAccentBrush") as Brush ?? SystemColors.HighlightBrush;
            drawing.DrawRoundedRectangle(background, new Pen(accent, 1), bounds, 6, 6);
            drawing.DrawImage(image, bounds);
            drawing.Pop();
        }
    }

    private static T? Ancestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T match) return match;
        return null;
    }

    private static SettingsReorderHandle? FindHandle(DependencyObject root)
    {
        if (root is SettingsReorderHandle handle) return handle;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindHandle(VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        return null;
    }
}
