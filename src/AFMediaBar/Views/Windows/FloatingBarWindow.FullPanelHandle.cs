// 悬浮宿主持有外侧入口窗口；移动、挂起、关闭及菜单切换时取消入口与迟到的取色结果。
using System.Windows.Input;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Views.Windows;

public sealed partial class FloatingBarWindow
{
    private Func<ExternalFullPanelHandleWindow>? _externalFullPanelHandleFactory;
    private ExternalFullPanelHandleWindow? _externalFullPanelHandle;
    private bool _isFullPanelOpen;
    private bool _externalHandleRequiresReentry;

    private bool CanKeepExternalFullPanelHandle() => IsOperational && IsVisible && _lastSnapshot.IsConnected &&
        !IsEdgeConcealed && !_dragging && !_sizeTimer.IsEnabled && !_isFullPanelOpen && !_compactFlyout.IsVisible && PresentationSettings.TaskbarExperience.FullPanelEntryVisible;

    private void InitializeExternalFullPanelHandle(Func<ExternalFullPanelHandleWindow>? factory)
    {
        _externalFullPanelHandleFactory = factory;
        _mediaControl.MouseEnter += ExternalHandle_MediaMouseEnter;
        _mediaControl.MouseLeave += ExternalHandle_MediaMouseLeave;
    }

    private void ExternalHandle_MediaMouseEnter(object sender, MouseEventArgs e) => RevealExternalFullPanelHandle();

    private void RevealExternalFullPanelHandle()
    {
        if (_externalHandleRequiresReentry || !_mediaControl.IsMouseOver || !CanKeepExternalFullPanelHandle() || _externalFullPanelHandleFactory is null) return;
        try
        {
            var bar = GetPhysicalBounds();
            var side = FullPanelPlacementPolicy.ResolveSide(bar, Monitor.WorkArea, IsVerticalPresentation);
            var geometry = FullPanelPlacementPolicy.PlaceHandle(bar, bar, Monitor.WorkArea,
                _mediaControl.GetFullPanelEntryScreenCenter(), side, Dpi);
            if (geometry.HitBounds.IsEmpty) return;
            if (_externalFullPanelHandle is null)
            {
                _externalFullPanelHandle = _externalFullPanelHandleFactory();
                _externalFullPanelHandle.OpenRequested += ExternalHandle_OpenRequested;
            }
            var placement = _nativeBoundsPx;
            _externalFullPanelHandle.Reveal(new TrayIconBounds((int)Math.Floor(bar.Left), (int)Math.Floor(bar.Top),
                    (int)Math.Ceiling(bar.Right), (int)Math.Ceiling(bar.Bottom)),
                geometry.VisualBounds, geometry.HitBounds, geometry.TransitBounds,
                CreateFullPanelRequest(ensureOpen: true, side, geometry.VisualBounds),
                IsVerticalPresentation, side is FullPanelPlacementSide.Below or FullPanelPlacementSide.Right,
                PresentationSettings.TaskbarExperience.FullPanelEntryTrigger,
                PresentationSettings.TaskbarExperience.FullPanelEntryHoverDelayMilliseconds,
                () => CanKeepExternalFullPanelHandle() && _nativeBoundsPx == placement,
                () => _mediaControl.IsMouseOver);
        }
        catch (Exception exception)
        {
            _externalFullPanelHandle?.Dismiss(immediate: true);
            _log.Warn("FullPanel", "悬浮媒体栏外侧入口显示失败", exception);
        }
    }

    private void ExternalHandle_MediaMouseLeave(object sender, MouseEventArgs e) => _externalHandleRequiresReentry = false;

    private void ExternalHandle_OpenRequested(object? sender, FullPanelOpenRequestEventArgs e)
    {
        if (CanKeepExternalFullPanelHandle()) OpenFullPanelRequested?.Invoke(this, e);
    }

    private void RequestFullPanel()
    {
        if (!IsOperational) return;
        if (IsEdgeConcealed) SetEdgeTarget(collapse: false, instant: true);
        OpenFullPanelRequested?.Invoke(this, CreateFullPanelRequest(ensureOpen: false));
    }

    private FullPanelOpenRequestEventArgs CreateFullPanelRequest(bool ensureOpen, FullPanelPlacementSide? side = null, Rect? handleBounds = null)
    {
        var bar = GetPhysicalBounds();
        side ??= FullPanelPlacementPolicy.ResolveSide(bar, Monitor.WorkArea, IsVerticalPresentation);
        var visual = handleBounds ?? FullPanelPlacementPolicy.PlaceHandle(bar, bar, Monitor.WorkArea,
            _mediaControl.GetFullPanelEntryScreenCenter(), side.Value, Dpi).VisualBounds;
        var center = new Point((visual.Left + visual.Width / 2) / Dpi, (visual.Top + visual.Height / 2) / Dpi);
        return new FullPanelOpenRequestEventArgs(GetMediaBarScreenBounds(), IsVerticalPresentation, ensureOpen, side, center);
    }

    internal void SetFullPanelOpen(bool open)
    {
        _isFullPanelOpen = open;
        if (open) SetEdgeTarget(collapse: false);
        _externalHandleRequiresReentry = _mediaControl.IsMouseOver;
        _externalFullPanelHandle?.Dismiss();
    }

    private void DisposeExternalFullPanelHandle()
    {
        _mediaControl.MouseEnter -= ExternalHandle_MediaMouseEnter;
        _mediaControl.MouseLeave -= ExternalHandle_MediaMouseLeave;
        if (_externalFullPanelHandle is not { } handle) return;
        handle.OpenRequested -= ExternalHandle_OpenRequested;
        handle.Dispose();
        _externalFullPanelHandle = null;
    }
}
