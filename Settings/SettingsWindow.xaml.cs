using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AFMediaBar.Models;
using AFMediaBar.Services;

namespace AFMediaBar.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsCoordinator _coordinator;
    private readonly DispatcherTimer _scaleSaveTimer;
    private readonly IReadOnlyList<SettingsSearchResult> _searchResults =
    [
        new("开机启动", "常规", "启动 登录 自动运行"),
        new("无媒体时隐藏播放器", "常规", "隐藏 媒体 可见性"),
        new("显示性能指标", "播放器组件", "指标 内存 CPU GPU APP"),
        new("音频频谱", "播放器组件", "音频 频谱 可视化"),
        new("输出设备切换", "播放器组件", "音频 输出 设备"),
        new("当前媒体音量", "播放器组件", "音量 应用"),
        new("窗口模式", "布局与位置", "任务栏 悬浮 宿主"),
        new("布局", "布局与位置", "自动 横向 竖向 方向"),
        new("显示比例", "布局与位置", "尺寸 缩放 比例"),
        new("自动避让任务栏图标", "布局与位置", "位置 自动 避让"),
        new("锁定手动位置", "布局与位置", "位置 锁定 拖动"),
        new("媒体控制窗口文字", "外观", "文字 颜色 主题 浅色 深色"),
        new("增强文字可读性", "外观", "可读性 透明 任务栏"),
        new("菜单与设置主题", "外观", "菜单 设置 主题 自动 浅色 深色"),
        new("自动折叠", "交互与动画", "折叠 鼠标 动画"),
        new("桌面边缘自动折叠", "交互与动画", "折叠 边缘 悬浮"),
        new("始终置顶并显示", "交互与动画", "置顶 显示"),
        new("低配置模式", "性能与高级", "性能 GPU 动画 渲染"),
        new("重新连接媒体会话", "性能与高级", "诊断 媒体 连接")
    ];
    private bool _isInitialized;
    private bool _isSyncing = true;

    internal SettingsWindow(SettingsCoordinator coordinator)
    {
        _coordinator = coordinator;
        _scaleSaveTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(360),
            DispatcherPriority.Background,
            ScaleSaveTimer_OnTick,
            Dispatcher);
        _scaleSaveTimer.Stop();
        InitializeComponent();
        _isInitialized = true;
        VersionText.Text = $"当前版本：{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "开发版"}";
        _coordinator.Changed += Coordinator_OnChanged;
        Closed += SettingsWindow_OnClosed;
        SyncFromSettings();
    }

    private void Coordinator_OnChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SyncFromSettings());
            return;
        }

        SyncFromSettings();
    }

    private void SyncFromSettings()
    {
        var settings = _coordinator.Current;
        _scaleSaveTimer.Stop();
        _isSyncing = true;
        StartupCheckBox.IsChecked = settings.StartupEnabled;
        HideWhenNoMediaCheckBox.IsChecked = settings.Window.HideWhenNoMedia;

        MetricsEnabledCheckBox.IsChecked = settings.Metrics.Enabled;
        SystemMemoryCheckBox.IsChecked = settings.Metrics.ShowSystemMemory;
        SystemCpuCheckBox.IsChecked = settings.Metrics.ShowSystemCpu;
        SystemGpuCheckBox.IsChecked = settings.Metrics.ShowSystemGpu;
        ProcessMemoryCheckBox.IsChecked = settings.Metrics.ShowProcessMemory;
        AudioMonitorCheckBox.IsChecked = settings.Metrics.AudioMonitorEnabled;
        OutputDeviceCheckBox.IsChecked = settings.Metrics.OutputDeviceSwitcherEnabled;
        VolumeControlCheckBox.IsChecked = settings.Metrics.VolumeControlEnabled;
        LowGpuModeCheckBox.IsChecked = settings.Metrics.LowGpuMode;

        TaskbarModeRadioButton.IsChecked = settings.Window.HostMode == WindowHostMode.Taskbar;
        FloatingModeRadioButton.IsChecked = settings.Window.HostMode == WindowHostMode.Floating;
        AutomaticLayoutRadioButton.IsChecked = settings.Window.LayoutMode == PlayerLayoutMode.Automatic;
        HorizontalLayoutRadioButton.IsChecked = settings.Window.LayoutMode == PlayerLayoutMode.Horizontal;
        VerticalLayoutRadioButton.IsChecked = settings.Window.LayoutMode == PlayerLayoutMode.Vertical;
        ScaleSlider.Value = settings.Window.DisplayScalePercent;
        ScaleValueText.Text = $"{settings.Window.DisplayScalePercent}%";

        AutomaticPlacementCheckBox.IsChecked = settings.Placement.AutomaticPlacement;
        LockPositionCheckBox.IsChecked = settings.Window.HostMode == WindowHostMode.Taskbar &&
            (settings.Window.LayoutMode == PlayerLayoutMode.Vertical
                ? settings.Placement.VerticalPositionLocked
                : settings.Placement.PositionLocked);

        AutomaticForegroundRadioButton.IsChecked =
            settings.Theme.TaskbarForegroundMode == TaskbarForegroundMode.Automatic;
        LightForegroundRadioButton.IsChecked =
            settings.Theme.TaskbarForegroundMode == TaskbarForegroundMode.LightText;
        DarkForegroundRadioButton.IsChecked =
            settings.Theme.TaskbarForegroundMode == TaskbarForegroundMode.DarkText;
        AutomaticMenuThemeRadioButton.IsChecked =
            settings.Theme.MenuThemeMode == MenuThemeMode.Automatic;
        LightMenuThemeRadioButton.IsChecked =
            settings.Theme.MenuThemeMode == MenuThemeMode.Light;
        DarkMenuThemeRadioButton.IsChecked =
            settings.Theme.MenuThemeMode == MenuThemeMode.Dark;
        EnhancedReadabilityCheckBox.IsChecked = settings.Theme.EnhancedReadability;

        AutoCollapseCheckBox.IsChecked = settings.Window.AutoCollapse;
        EdgeAutoCollapseCheckBox.IsChecked = settings.Window.EdgeAutoCollapse;
        AlwaysOnTopCheckBox.IsChecked = settings.Window.AlwaysOnTop;
        _isSyncing = false;
        UpdateDependencies();
    }

    private void UpdateDependencies()
    {
        var settings = _coordinator.Current;
        var taskbarMode = settings.Window.HostMode == WindowHostMode.Taskbar;
        var forcedVertical = settings.Window.LayoutMode == PlayerLayoutMode.Vertical;
        var canUseAutomaticPlacement = taskbarMode && !forcedVertical;
        MetricsEnabledCheckBox.IsEnabled = true;
        SystemMemoryCheckBox.IsEnabled = settings.Metrics.Enabled;
        SystemCpuCheckBox.IsEnabled = settings.Metrics.Enabled;
        SystemGpuCheckBox.IsEnabled = settings.Metrics.Enabled;
        ProcessMemoryCheckBox.IsEnabled = settings.Metrics.Enabled;
        AutomaticPlacementCheckBox.IsEnabled = canUseAutomaticPlacement;
        AutomaticPlacementDescription.Text = canUseAutomaticPlacement
            ? "依附任务栏模式下自动避让任务栏图标。"
            : "当前窗口模式或竖向布局不支持自动避让。";
        LockPositionCheckBox.IsEnabled = taskbarMode && !settings.Placement.AutomaticPlacement;
        EdgeAutoCollapseCheckBox.IsEnabled = !taskbarMode;
        EdgeAutoCollapseDescription.Text = taskbarMode
            ? "切换到独立悬浮模式后可用。"
            : "将窗口拖到桌面边缘后自动收起。";
    }

    private void NavigationList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        if (NavigationList.SelectedItem is ListBoxItem { Tag: string tag })
        {
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Clear();
            }

            ShowPage(tag);
        }
    }

    private void ShowPage(string tag, FrameworkElement? target = null)
    {
        SearchResultsPage.Visibility = Visibility.Collapsed;
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        ComponentsPage.Visibility = tag == "Components" ? Visibility.Visible : Visibility.Collapsed;
        LayoutPage.Visibility = tag == "Layout" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        InteractionPage.Visibility = tag == "Interaction" ? Visibility.Visible : Visibility.Collapsed;
        PerformancePage.Visibility = tag == "Performance" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageScrollViewer.ScrollToTop();
        if (target is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => target.BringIntoView());
        }
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        var query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (string.IsNullOrEmpty(query))
        {
            SearchResultsList.ItemsSource = null;
            SearchResultsList.Visibility = Visibility.Collapsed;
            SearchEmptyText.Visibility = Visibility.Collapsed;
            if (NavigationList.SelectedItem is ListBoxItem { Tag: string tag })
            {
                ShowPage(tag);
            }
            return;
        }

        var results = _searchResults
            .Where(result => result.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                result.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        SearchResultsList.ItemsSource = results;
        SearchResultsSummaryText.Text = results.Length == 0
            ? $"没有找到与“{query}”匹配的设置。"
            : $"找到 {results.Length} 项与“{query}”相关的设置。";
        SearchResultsList.Visibility = results.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        SearchEmptyText.Visibility = results.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        SearchResultsPage.Visibility = Visibility.Visible;
        GeneralPage.Visibility = Visibility.Collapsed;
        ComponentsPage.Visibility = Visibility.Collapsed;
        LayoutPage.Visibility = Visibility.Collapsed;
        AppearancePage.Visibility = Visibility.Collapsed;
        InteractionPage.Visibility = Visibility.Collapsed;
        PerformancePage.Visibility = Visibility.Collapsed;
        SettingsPageScrollViewer.ScrollToTop();
    }

    private void SearchResults_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        if (SearchResultsList.SelectedItem is not SettingsSearchResult result)
        {
            return;
        }

        var pageTag = result.PageTitle switch
        {
            "常规" => "General",
            "播放器组件" => "Components",
            "布局与位置" => "Layout",
            "外观" => "Appearance",
            "交互与动画" => "Interaction",
            _ => "Performance"
        };
        NavigationList.SelectedIndex = pageTag switch
        {
            "General" => 0,
            "Components" => 1,
            "Layout" => 2,
            "Appearance" => 3,
            "Interaction" => 4,
            _ => 5
        };
        SearchResultsList.SelectedIndex = -1;
        SearchBox.Clear();
        ShowPage(pageTag);
    }

    private void GeneralCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        TryUpdate(() => _coordinator.UpdateStartup(StartupCheckBox.IsChecked == true));
    }

    private void MetricCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        TryUpdate(() => _coordinator.UpdateMetrics(new MetricSettings(
            MetricsEnabledCheckBox.IsChecked == true,
            SystemMemoryCheckBox.IsChecked == true,
            SystemCpuCheckBox.IsChecked == true,
            SystemGpuCheckBox.IsChecked == true,
            ProcessMemoryCheckBox.IsChecked == true,
            LowGpuModeCheckBox.IsChecked == true,
            AudioMonitorCheckBox.IsChecked == true,
            OutputDeviceCheckBox.IsChecked == true,
            VolumeControlCheckBox.IsChecked == true)));
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
            DisplayScalePercent = (int)Math.Round(ScaleSlider.Value),
            AutoCollapse = AutoCollapseCheckBox.IsChecked == true,
            EdgeAutoCollapse = EdgeAutoCollapseCheckBox.IsChecked == true
        };
        TryUpdate(() => _coordinator.UpdateWindow(settings));
    }

    private void ScaleSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized)
        {
            return;
        }

        ScaleValueText.Text = $"{Math.Round(ScaleSlider.Value):0}%";
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

    private void ThemeRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var mode = LightForegroundRadioButton.IsChecked == true
            ? TaskbarForegroundMode.LightText
            : DarkForegroundRadioButton.IsChecked == true
                ? TaskbarForegroundMode.DarkText
                : TaskbarForegroundMode.Automatic;
        var current = _coordinator.Current.Theme;
        TryUpdate(() => _coordinator.UpdateTheme(current with
        {
            TaskbarForegroundMode = mode
        }));
    }

    private void MenuThemeRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var mode = LightMenuThemeRadioButton.IsChecked == true
            ? MenuThemeMode.Light
            : DarkMenuThemeRadioButton.IsChecked == true
                ? MenuThemeMode.Dark
                : MenuThemeMode.Automatic;
        var current = _coordinator.Current.Theme;
        TryUpdate(() => _coordinator.UpdateTheme(current with
        {
            MenuThemeMode = mode
        }));
    }

    private void ThemeCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var current = _coordinator.Current.Theme;
        TryUpdate(() => _coordinator.UpdateTheme(current with
        {
            EnhancedReadability = EnhancedReadabilityCheckBox.IsChecked == true
        }));
    }

    private void ResetDefaults_OnClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "这会恢复所有设置并关闭开机启动，是否继续？",
            "恢复默认设置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        TryUpdate(_coordinator.ResetAll);
    }

    private void OpenLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "无法打开链接",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Reconnect_OnClick(object sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.RequestMediaReconnect();
    }

    private void TryUpdate(Action update)
    {
        try
        {
            update();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "无法保存设置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SyncFromSettings();
        }
    }

    private void SettingsWindow_OnClosed(object? sender, EventArgs e)
    {
        _scaleSaveTimer.Stop();
        _coordinator.Changed -= Coordinator_OnChanged;
        Closed -= SettingsWindow_OnClosed;
    }

    private sealed record SettingsSearchResult(string Title, string PageTitle, string Keywords);
}
