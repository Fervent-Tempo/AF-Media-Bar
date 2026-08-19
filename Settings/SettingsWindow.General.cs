using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Settings;
/// <summary>
/// 处理通用、窗口、性能和任务栏位置设置，并通过 SettingsCoordinator 提交变更。
/// Handles general, window, performance, and placement settings through SettingsCoordinator.
/// </summary>
public partial class SettingsWindow
{
    private void GeneralCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        TryUpdate(() => _coordinator.UpdateStartup(StartupCheckBox.IsChecked == true));
    }

    private void LanguageRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var language = LanguageFollowSystemRadioButton.IsChecked == true
            ? AppLanguage.FollowSystem
            : LanguageZhCnRadioButton.IsChecked == true
                ? AppLanguage.ZhCn
                : LanguageZhTwRadioButton.IsChecked == true
                    ? AppLanguage.ZhTw
                    : AppLanguage.EnUs;
        TryUpdate(() => _coordinator.UpdateLanguage(language));
    }

    private void MetricCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var current = _coordinator.Current.Metrics;
        TryUpdate(() => _coordinator.UpdateMetrics(current with
        {
            LowGpuMode = LowGpuModeCheckBox.IsChecked == true
        }));
    }

    private void WindowCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        UpdateWindowSettings();
    }

    private void WindowRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        UpdateWindowSettings();
    }

    private void UpdateWindowSettings()
    {
        var current = _coordinator.Current.Window;
        var hostMode = FloatingModeRadioButton.IsChecked == true
            ? WindowHostMode.Floating
            : WindowHostMode.Taskbar;
        var layoutMode = VerticalLayoutRadioButton.IsChecked == true
            ? PlayerLayoutMode.Vertical
            : HorizontalLayoutRadioButton.IsChecked == true
                ? PlayerLayoutMode.Horizontal
                : PlayerLayoutMode.Automatic;
        var settings = current with
        {
            HideWhenNoMedia = HideWhenNoMediaCheckBox.IsChecked == true,
            AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true,
            HostMode = hostMode,
            LayoutMode = layoutMode,
            LengthScalePercent = (int)Math.Round(LengthScaleSlider.Value),
            ThicknessScalePercent = (int)Math.Round(ThicknessScaleSlider.Value)
        };
        TryUpdate(() => _coordinator.UpdateWindow(settings));
    }

    private void ScaleSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized)
        {
            return;
        }

        LengthScaleValueText.Text = $"{Math.Round(LengthScaleSlider.Value):0}%";
        ThicknessScaleValueText.Text = $"{Math.Round(ThicknessScaleSlider.Value):0}%";
        TaskbarTopOffsetValueText.Text =
            $"{Math.Round(TaskbarTopOffsetSlider.Value):+0;-0;0}";
        if (_isSyncing)
        {
            return;
        }

        _scaleSaveTimer.Stop();
        _scaleSaveTimer.Start();
    }

    private void ScaleSaveTimer_OnTick(object? sender, EventArgs e)
    {
        _scaleSaveTimer.Stop();
        UpdateWindowSettings();
        var currentPlacement = _coordinator.Current.Placement;
        TryUpdate(() => _coordinator.UpdatePlacement(currentPlacement with
        {
            TaskbarTopOffsetDip = (int)Math.Round(TaskbarTopOffsetSlider.Value)
        }));
    }

    private void PlacementCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var current = _coordinator.Current.Placement;
        var automatic = AutomaticPlacementCheckBox.IsChecked == true;
        var locked = LockPositionCheckBox.IsChecked == true || automatic;
        if (automatic)
        {
            _isSyncing = true;
            LockPositionCheckBox.IsChecked = true;
            _isSyncing = false;
        }

        TryUpdate(() => _coordinator.UpdatePlacement(current with
        {
            AutomaticPlacement = automatic,
            PositionLocked = locked,
            VerticalPositionLocked = locked
        }));
    }

    private void ResetPosition_OnClick(object sender, RoutedEventArgs e)
    {
        var currentWindow = _coordinator.Current.Window with
        {
            FloatingLeft = null,
            FloatingTop = null
        };
        TryUpdate(() =>
        {
            _coordinator.UpdatePlacement(PlacementSettings.Default);
            _coordinator.UpdateWindow(currentWindow);
        });
    }

}
