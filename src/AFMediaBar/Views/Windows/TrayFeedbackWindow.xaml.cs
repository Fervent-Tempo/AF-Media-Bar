using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Utils;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>在托盘图标附近显示短暂的音频操作反馈。 / Shows brief audio-operation feedback near the tray icon.</summary>
public partial class TrayFeedbackWindow : FluentWindow
{
    private readonly DispatcherTimer _closeTimer;

    public TrayFeedbackWindow()
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(950) };
        _closeTimer.Tick += (_, _) => FadeOut();
    }

    public void ShowFeedback(string text, TrayIconBounds? bounds)
    {
        FeedbackText.Text = text;
        Opacity = 0;
        Show();
        UpdateLayout();
        PositionNear(bounds);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    private void FadeOut()
    {
        _closeTimer.Stop();
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        animation.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, animation);
    }

    private void PositionNear(TrayIconBounds? bounds)
    {
        if (bounds is not { } icon)
        {
            return;
        }

        var point = new NativeMethods.POINT { X = (icon.Left + icon.Right) / 2, Y = (icon.Top + icon.Bottom) / 2 };
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        var dpi = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX / 96d : 1d;
        var width = (int)Math.Ceiling(ActualWidth * dpi);
        var height = (int)Math.Ceiling(ActualHeight * dpi);
        var x = Math.Clamp((icon.Left + icon.Right - width) / 2, info.rcWork.Left, info.rcWork.Right - width);
        var y = icon.Top - height - 6;
        if (y < info.rcWork.Top)
        {
            y = icon.Bottom + 6;
        }

        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, -1, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closeTimer.Stop();
        base.OnClosing(e);
    }
}
