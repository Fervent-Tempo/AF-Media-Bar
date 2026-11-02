// 独立组件的无激活工具窗口；借用视觉容器和层级服务，由所属媒体栏统一关闭。
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Components;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Views.Windows;

/// <summary>仅承载一个组件或拖动预览，不创建媒体、音频或指标服务。</summary>
public sealed class ComponentFloatingWindow : Window, IDisposable
{
    private readonly DesktopWidgetZOrderService _zOrder;
    private readonly DetachedComponentVisual _visual;
    private IDisposable? _registration;
    private (FloatingBarLayer Layer, bool Overlay)? _registeredLayer;
    private nint _handle;
    private bool _disposed;
    private readonly AdaptiveForegroundSamplingSession _foreground;
    private Rect _bounds;

    public ComponentFloatingWindow(DetachedComponentVisual visual, DesktopWidgetZOrderService zOrder, ScreenBackgroundSampler sampler)
    {
        _visual = visual;
        _zOrder = zOrder;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Content = visual.View;
        WindowHelper.SetNoActivate(this);
        _foreground = new(sampler, Dispatcher, SampleRegions, decision => _visual.ForegroundChanged?.Invoke(decision));
    }

    public void Place(Rect physicalBounds, FloatingBarLayer layer, bool overlayTaskbar, bool visible)
    {
        if (_disposed) return;
        if (_bounds != physicalBounds) _foreground.Invalidate(clearDecision: false);
        _bounds = physicalBounds;
        _handle = new WindowInteropHelper(this).EnsureHandle();
        if (_handle == nint.Zero) throw new InvalidOperationException("Component window creation failed.");
        SetWindowPos(_handle, 0, (int)Math.Round(physicalBounds.X), (int)Math.Round(physicalBounds.Y),
            Math.Max(1, (int)Math.Ceiling(physicalBounds.Width)), Math.Max(1, (int)Math.Ceiling(physicalBounds.Height)),
            SWP_NOACTIVATE | SWP_NOZORDER);
        if (_registration is null || _registeredLayer != (layer, overlayTaskbar))
        {
            _registration?.Dispose();
            _registeredLayer = (layer, overlayTaskbar);
            _registration = _zOrder.Attach(_handle, layer, () => !_disposed && IsVisible, overlayTaskbar);
        }
        _visual.IsPlaced = visible;
        if (visible) { if (!IsVisible) Show(); } else Hide();
        if (visible && _visual.HasContent) _foreground.RequestRefresh();
    }

    private IReadOnlyList<Int32Rect> SampleRegions()
    {
        if (_disposed || !IsVisible || !_visual.HasContent || _bounds.IsEmpty || _handle == nint.Zero || SystemParameters.HighContrast ||
            SettingsManager.Current.Appearance.PlayerForegroundMode != PlayerForegroundMode.Automatic) return [];
        var area = MonitorUtil.GetMonitor(_handle).monitorArea;
        var strips = new[] { new Rect(_bounds.Left, _bounds.Top - 4, _bounds.Width, 2),
            new Rect(_bounds.Left, _bounds.Bottom + 2, _bounds.Width, 2) };
        return strips.Select(rectangle => Rect.Intersect(rectangle, area)).Where(rectangle => !rectangle.IsEmpty && rectangle.Width >= 1 && rectangle.Height >= 1)
            .Select(rectangle => new Int32Rect((int)Math.Round(rectangle.X), (int)Math.Round(rectangle.Y),
                (int)Math.Floor(rectangle.Width), (int)Math.Floor(rectangle.Height))).ToArray();
    }

    public void RefreshForeground() { if (!_disposed && IsVisible && _visual.HasContent) _foreground.RequestRefresh(); }

    public void Suspend()
    {
        if (_disposed) return;
        _visual.IsPlaced = false;
        _foreground.Invalidate(clearDecision: false);
        _visual.View.ContextMenu?.SetCurrentValue(System.Windows.Controls.ContextMenu.IsOpenProperty, false);
        Hide();
        _registration?.Dispose(); _registration = null; _registeredLayer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Suspend(); _disposed = true;
        _foreground.Dispose();
        Content = null;
        Close();
    }
}
