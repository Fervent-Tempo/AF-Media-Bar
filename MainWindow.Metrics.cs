using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar;

/// <summary>
/// 协调系统指标采样、轮换和 WPF 文本呈现；采样实现仍由 SystemMetricsService 负责。
/// Coordinates system-metric sampling, cycling, and WPF text presentation; SystemMetricsService owns sampling.
/// </summary>
public partial class MainWindow
{
    private readonly SystemMetricsService _systemMetricsService = new();
    private readonly DispatcherTimer _metricsTimer;
    private int _metricCycleIndex;
    private int _metricCycleTicks;

    private void OnMetricsTimerTick(object? sender, EventArgs e)
    {
        _metricCycleTicks++;
        var selectedCount = _metricSettings.SelectedCount;
        var advance = selectedCount > 1 && _metricCycleTicks % 3 == 0;
        UpdateMetrics(advance);
    }

    private void UpdateMetrics(bool advanceCycle)
    {
        var samplingSettings = LayoutRuntimeService.ResolveMetricSamplingSettings(
            _activeLayoutProfile,
            _metricSettings);
        var sample = _systemMetricsService.Sample(samplingSettings);
        ComponentSurface_OnMetricsSnapshotChanged(sample);
        var selectedCount = _metricSettings.SelectedCount;
        if (selectedCount == 0)
        {
            MetricsText.Text = string.Empty;
            MetricsHost.Visibility = Visibility.Collapsed;
            VerticalMetricsText.Text = string.Empty;
            VerticalMetricsHost.Visibility = Visibility.Collapsed;
            UpdatePlayerWidth(metricsVisible: false);
            return;
        }

        MetricsHost.Visibility = Visibility.Visible;
        VerticalMetricsHost.Visibility = Visibility.Visible;
        var metricsCursor = _metricSettings.OpenTaskManagerOnMetricsClick
            ? Cursors.Hand
            : Cursors.Arrow;
        MetricsHost.Cursor = metricsCursor;
        VerticalMetricsHost.Cursor = metricsCursor;
        UpdatePlayerWidth(metricsVisible: true);
        if (advanceCycle)
        {
            _metricCycleIndex = (_metricCycleIndex + 1) % selectedCount;
        }
        else
        {
            _metricCycleIndex = Math.Clamp(_metricCycleIndex, 0, selectedCount - 1);
        }

        var text = MetricTextFormatter.Format(sample, _metricSettings, _metricCycleIndex);
        SetMetricText(text, advanceCycle);
    }

    private void MetricsHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_metricSettings.OpenTaskManagerOnMetricsClick ||
            _metricSettings.SelectedCount == 0)
        {
            return;
        }

        e.Handled = true;
        OpenTaskManager();
    }

    private void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("open-task-manager", exception);
            MessageBox.Show(
                exception.Message,
                Loc.Get("Msg.OpenTaskManagerFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdatePlayerWidth(bool metricsVisible)
    {
        ApplyResponsivePlayerDimensions();
        PositionOverTaskbar(force: true);
    }

    private void SetMetricText(string text, bool animate)
    {
        if (!animate || _metricSettings.LowGpuMode)
        {
            MetricsText.BeginAnimation(UIElement.OpacityProperty, null);
            MetricsText.Opacity = 1;
            MetricsText.Text = text;
            VerticalMetricsText.Opacity = 1;
            VerticalMetricsText.Text = text;
            return;
        }

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90));
        fadeOut.Completed += (_, _) =>
        {
            MetricsText.Text = text;
            VerticalMetricsText.Text = text;
            MetricsText.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
            VerticalMetricsText.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
        };
        MetricsText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        VerticalMetricsText.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90)));
    }

    private void ApplyMetricSettings()
    {
        if (_isClosed)
        {
            return;
        }

        ApplyOutputDeviceSettings();
        ApplyVolumeControlSettings();
        _metricCycleIndex = 0;
        _metricCycleTicks = 0;
        UpdateMetrics(advanceCycle: false);
        if (_metricSettings.LowGpuMode)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            SetExpanded(_isExpanded, animate: false);
            StopMarquees();
        }
        else
        {
            RenderOptions.ProcessRenderMode = RenderMode.Default;
            ScheduleMarqueeUpdate();
        }

        ApplyAudioMonitorSettings();
        _ = RefreshAutomaticPlacementSafelyAsync();
    }

}
