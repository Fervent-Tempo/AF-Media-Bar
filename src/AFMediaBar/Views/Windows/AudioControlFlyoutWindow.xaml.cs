using System.Windows.Input;
using System.Windows.Interop;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.ViewModels.Windows;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>锚定自有托盘图标的紧凑音频面板。 / Compact audio flyout anchored to the app's own tray icon.</summary>
public partial class AudioControlFlyoutWindow : FluentWindow
{
    public AudioControlViewModel ViewModel { get; }

    public AudioControlFlyoutWindow(AudioControlViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public async Task ToggleAsync(TrayIconBounds? bounds)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        await ViewModel.RefreshAsync();
        Show();
        Activate();
        UpdateLayout();
        PositionNear(bounds);
    }

    private void PositionNear(TrayIconBounds? bounds)
    {
        if (bounds is not { } icon)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var monitorPoint = new NativeMethods.POINT { X = (icon.Left + icon.Right) / 2, Y = (icon.Top + icon.Bottom) / 2 };
        var monitor = NativeMethods.MonitorFromPoint(monitorPoint, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        var dpi = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX / 96d : 1d;
        var width = (int)Math.Ceiling(ActualWidth * dpi);
        var height = (int)Math.Ceiling(ActualHeight * dpi);
        var x = Math.Clamp(icon.Right - width, info.rcWork.Left, info.rcWork.Right - width);
        var spaceAbove = icon.Top - info.rcWork.Top;
        var y = spaceAbove >= height + 8 ? icon.Top - height - 8 : icon.Bottom + 8;
        y = Math.Clamp(y, info.rcWork.Top, info.rcWork.Bottom - height);
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(handle, -1, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void OutputDevice_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.PreviewOutputDeviceWheel(e.Delta);
        e.Handled = true;
    }

    private void Window_OnDeactivated(object? sender, EventArgs e) => Hide();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Application.Current?.Dispatcher.HasShutdownStarted != true)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
