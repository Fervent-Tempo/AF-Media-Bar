// 任务栏宿主管理外侧入口的资格、几何和释放；自动隐藏仅冻结此次交互的锚点。
using System.Windows;
using System.Windows.Input;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
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
        SettingsManager.Current.TaskbarBarEnabled && SettingsManager.Current.Experiments.ExternalFullPanelHandle &&
        SettingsManager.Current.TaskbarExperience is { FullLayerEnabled: true, FullPanelEntryVisible: true };

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
            var positive = vertical ? taskbar.Left + taskbar.Right < monitor.Left + monitor.Right
                : taskbar.Top + taskbar.Bottom < monitor.Top + monitor.Bottom;
            var center = MediaControl.GetFullPanelEntryScreenCenter();
            var bar = GetMediaBarScreenPhysicalBounds();
            var thickness = Math.Max(1, Math.Round(8 * dpi));
            var length = Math.Max(1, Math.Round(44 * dpi));
            var handleBounds = vertical
                ? new Rect(positive ? taskbar.Right : taskbar.Left - thickness, Math.Round(center.Y - length / 2), thickness, length)
                : new Rect(Math.Round(center.X - length / 2), positive ? taskbar.Bottom : taskbar.Top - thickness, length, thickness);
            if (vertical)
                handleBounds.Y = Math.Clamp(handleBounds.Y, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - length));
            else
                handleBounds.X = Math.Clamp(handleBounds.X, monitor.Left, Math.Max(monitor.Left, monitor.Right - length));
            var hitThickness = Math.Max(1, Math.Round(ExternalFullPanelHandleWindow.HitThicknessDip * dpi));
            var hitLength = Math.Max(1, Math.Round(ExternalFullPanelHandleWindow.HitLengthDip * dpi));
            // 只向桌面侧加厚命中区域，避免覆盖任务栏中的其他按钮；视觉横杆保持原位。
            var hitBounds = vertical
                ? new Rect(positive ? handleBounds.Left : handleBounds.Right - hitThickness,
                    Math.Round(handleBounds.Top + (handleBounds.Height - hitLength) / 2), hitThickness, hitLength)
                : new Rect(Math.Round(handleBounds.Left + (handleBounds.Width - hitLength) / 2),
                    positive ? handleBounds.Top : handleBounds.Bottom - hitThickness, hitLength, hitThickness);
            hitBounds.Intersect(new Rect(monitor.Left, monitor.Top, monitor.Right - monitor.Left, monitor.Bottom - monitor.Top));
            if (hitBounds.IsEmpty) return;
            // 过渡区域只用于延长可见性，不创建覆盖桌面的输入窗口。
            var transit = vertical
                ? new Rect(Math.Min(hitBounds.Left, bar.Right), hitBounds.Top,
                    Math.Max(hitBounds.Right, bar.Left) - Math.Min(hitBounds.Left, bar.Right), hitBounds.Height)
                : new Rect(hitBounds.Left, Math.Min(hitBounds.Top, bar.Bottom), hitBounds.Width,
                    Math.Max(hitBounds.Bottom, bar.Top) - Math.Min(hitBounds.Top, bar.Bottom));
            if (_externalFullPanelHandle is null)
            {
                _externalFullPanelHandle = _externalFullPanelHandleFactory();
                _externalFullPanelHandle.OpenRequested += ExternalHandle_OpenRequested;
            }
            var taskbarHandle = _lastTaskbarHandle;
            _externalFullPanelHandle.Reveal(bar, handleBounds, hitBounds, transit,
                new FullPanelOpenRequestEventArgs(GetMediaBarScreenBounds(), vertical, ensureOpen: true), vertical, positive,
                SettingsManager.Current.Experiments.Normalize().ExternalFullPanelHandleTrigger,
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
