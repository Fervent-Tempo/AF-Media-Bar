// 任务栏宿主管理外侧入口的资格、几何和释放；自动隐藏仅冻结此次交互的锚点。
using System.Windows;
using System.Windows.Input;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Views.Windows;

public partial class TaskbarWindow
{
    private Func<ExternalFullPanelHandleWindow>? _externalFullPanelHandleFactory;
    private ExternalFullPanelHandleWindow? _externalFullPanelHandle;
    private bool _isFullPanelOpen;
    private bool _externalHandleRequiresReentry;

    private void InitializeExternalFullPanelHandle(Func<ExternalFullPanelHandleWindow>? factory)
    {
        _externalFullPanelHandleFactory = factory;
        MediaControl.MouseEnter += ExternalHandle_MediaMouseEnter;
        MediaControl.MouseLeave += ExternalHandle_MediaMouseLeave;
        SettingsManager.SettingsChanged += ExternalHandle_SettingsChanged;
    }

    private bool CanKeepExternalFullPanelHandle() => !_isClosing && !_isEnvironmentSuspended && !IsBackgroundPruned &&
        _lastSnapshot.IsConnected &&
        (IsVisible || IsTaskbarPresentationSuspended) &&
        !_isDragging && !_isFullPanelOpen && !PlayerMenu.IsOpen && !_compactFlyout.IsVisible &&
        SettingsManager.Current.TaskbarBarEnabled && SettingsManager.Current.TaskbarExperience.FullPanelEntryVisible;

    private void ExternalHandle_MediaMouseEnter(object sender, MouseEventArgs e)
    {
        if (_externalHandleRequiresReentry || !CanKeepExternalFullPanelHandle() || !IsVisible ||
            IsTaskbarPresentationSuspended || _isRestoringTaskbarPresentation || !_hasTaskbarPresentationPlacement ||
            !MediaControl.IsHitTestVisible || _externalFullPanelHandleFactory is null ||
            !_taskBarService.TryGetTaskbarRect(_lastTaskbarHandle, out var taskbar)) return;
        try
        {
            var dpi = _taskBarService.GetTaskbarDpiScale(_lastTaskbarHandle);
            if (dpi <= 0) return;
            var monitor = MonitorUtil.GetMonitor(_lastTaskbarHandle).monitorArea;
            var vertical = IsVerticalPresentation;
            var attachment = new Rect(taskbar.Left, taskbar.Top, taskbar.Right - taskbar.Left, taskbar.Bottom - taskbar.Top);
            var screen = new Rect(monitor.Left, monitor.Top, monitor.Right - monitor.Left, monitor.Bottom - monitor.Top);
            var side = FullPanelPlacementPolicy.ResolveSide(attachment, screen, vertical);
            var positive = side is FullPanelPlacementSide.Below or FullPanelPlacementSide.Right;
            var center = MediaControl.GetFullPanelEntryScreenCenter();
            var bar = GetMediaBarScreenPhysicalBounds();
            var geometry = FullPanelPlacementPolicy.PlaceHandle(
                new Rect(bar.Left, bar.Top, bar.Right - bar.Left, bar.Bottom - bar.Top),
                attachment, screen, center, side, dpi);
            if (geometry.HitBounds.IsEmpty) return;
            if (_externalFullPanelHandle is null)
            {
                _externalFullPanelHandle = _externalFullPanelHandleFactory();
                _externalFullPanelHandle.OpenRequested += ExternalHandle_OpenRequested;
            }
            var taskbarHandle = _lastTaskbarHandle;
            _externalFullPanelHandle.Reveal(bar, geometry.VisualBounds, geometry.HitBounds, geometry.TransitBounds,
                new FullPanelOpenRequestEventArgs(GetMediaBarScreenBounds(), vertical, ensureOpen: true, side), vertical, positive,
                SettingsManager.Current.TaskbarExperience.FullPanelEntryTrigger,
                SettingsManager.Current.TaskbarExperience.FullPanelEntryHoverDelayMilliseconds,
                () => CanKeepExternalFullPanelHandle() && taskbarHandle == _lastTaskbarHandle &&
                    Math.Abs(_taskBarService.GetTaskbarDpiScale(taskbarHandle) - dpi) < 0.001 &&
                    _taskBarService.TryGetTaskbarRect(taskbarHandle, out var current) &&
                    Math.Abs((current.Right - current.Left) - (taskbar.Right - taskbar.Left)) <= 2 &&
                    Math.Abs((current.Bottom - current.Top) - (taskbar.Bottom - taskbar.Top)) <= 2 &&
                    (vertical ? current.Top == taskbar.Top && Math.Abs(current.Left - taskbar.Left) <= taskbar.Right - taskbar.Left
                        : current.Left == taskbar.Left && Math.Abs(current.Top - taskbar.Top) <= taskbar.Bottom - taskbar.Top));
        }
        catch (Exception exception)
        {
            _externalFullPanelHandle?.Dismiss(immediate: true);
            AFMediaBar.Classes.Services.AppLogService.Current?.Warn("FullPanel", "外侧完整面板入口显示失败", exception);
        }
    }

    private void ExternalHandle_MediaMouseLeave(object sender, MouseEventArgs e) => _externalHandleRequiresReentry = false;

    private void ExternalHandle_OpenRequested(object? sender, FullPanelOpenRequestEventArgs e)
    {
        if (CanKeepExternalFullPanelHandle()) OpenFullPanelRequested?.Invoke(this, e);
    }

    internal void SetFullPanelOpen(bool open)
    {
        _isFullPanelOpen = open;
        _externalHandleRequiresReentry = MediaControl.IsMouseOver;
        _externalFullPanelHandle?.Dismiss();
    }

    private void ExternalHandle_SettingsChanged(object? sender, SettingsChangedEventArgs e) => _externalFullPanelHandle?.Dismiss();

    private void DisposeExternalFullPanelHandle()
    {
        SettingsManager.SettingsChanged -= ExternalHandle_SettingsChanged;
        MediaControl.MouseEnter -= ExternalHandle_MediaMouseEnter;
        MediaControl.MouseLeave -= ExternalHandle_MediaMouseLeave;
        if (_externalFullPanelHandle is not { } handle) return;
        handle.OpenRequested -= ExternalHandle_OpenRequested;
        handle.Dispose();
        _externalFullPanelHandle = null;
    }
}
