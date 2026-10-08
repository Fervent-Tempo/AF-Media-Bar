using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Utils;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>设置页中可预览选择的显示模式。 / Display mode selectable for preview on the settings page.</summary>
public enum DisplayModeSelection
{
    Taskbar = 0,
    DynamicIsland = 1,
    DesktopCard = 2,
    FloatingBall = 3
}

/// <summary>四种显示模式及各层功能开关。 / Four display modes and their layer feature switches.</summary>
public partial class DisplayModesViewModel : ObservableObject, IDisposable
{
    private readonly IDisplayMonitorService _displayMonitorService;
    /// <summary>Cancels asynchronous UI work when this editor context becomes inactive.</summary>
    public CancellationToken ContextCancellationToken => _configuration.CancellationToken;

    private readonly LocalizationService _localization;
    private bool _isRefreshing;
    private readonly ISettingsConfiguration _configuration;
    private readonly Dispatcher _dispatcher = DispatcherHelper.Current;
    private bool _disposed;
    private IReadOnlyList<TaskbarMonitorSelectionItem> _taskbarMonitorOptions = Array.Empty<TaskbarMonitorSelectionItem>();

    /// <summary>可逐项勾选的任务栏目标列表；至少保留一项，既支持单选也支持多选。/ Individually selectable taskbar targets; at least one remains selected, supporting one or many.</summary>
    public IReadOnlyList<TaskbarMonitorSelectionItem> TaskbarMonitorOptions => _taskbarMonitorOptions;
    public WindowMode CurrentWindowMode => _configuration.Current.WindowMode;
    public DisplayModeSelection SelectedMode => (DisplayModeSelection)_configuration.Context.Mode;
    public bool IsTaskbarMode => SelectedMode == DisplayModeSelection.Taskbar;
    public bool IsDynamicIslandMode => SelectedMode == DisplayModeSelection.DynamicIsland;
    public bool IsDesktopCardMode => SelectedMode == DisplayModeSelection.DesktopCard;
    public bool IsFloatingBallMode => SelectedMode == DisplayModeSelection.FloatingBall;
    public bool IsUnimplementedMode => !IsTaskbarMode;

    /// <summary>Name of the actual running family supplied by the settings context.</summary>
    public string HostingModeText => Translations.Format(
        "DisplayModes.Status.Current",
        Translations.Get(IsTaskbarMode ? "DisplayModes.Mode.Taskbar" : "Common.DynamicIsland"));

    /// <summary>
    /// 任务栏是否就是当前运行模式。模式卡片用它决定“当前模式”芯片是否显示，
    /// 取代以前写死在任务栏卡片上的那个芯片。
    /// Whether the taskbar really is the running mode. The mode cards use it to decide whether the
    /// "current mode" chip shows, replacing the chip that used to be hardcoded on the taskbar card.
    /// </summary>
    public bool IsTaskbarHostingActive => IsTaskbarMode;

    public bool HoverLayerEnabled
    {
        get => _configuration.Current.TaskbarExperience.HoverLayerEnabled;
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { HoverLayerEnabled = value });
    }

    public bool FullLayerEnabled
    {
        get => _configuration.Current.TaskbarExperience.FullLayerEnabled;
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { FullLayerEnabled = value });
    }

    /// <summary>
    /// 静置层与悬停层是否提供进入完整层的入口（文字区顶部细杠与悬停层按钮）。界面开关在本页静置层分区，
    /// 关闭后两个入口一起消失；完整层本身与托盘、菜单入口不受影响。
    /// Whether the rest and hover layers offer an entry into the full layer (the thin bar above the text and the hover layer's
    /// button). The switch sits in this page's rest section; turning it off removes both entries, while the full layer itself and
    /// the tray and menu entries stay as they are.
    /// </summary>
    public bool FullPanelEntryVisible
    {
        get => _configuration.Current.TaskbarExperience.FullPanelEntryVisible;
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { FullPanelEntryVisible = value });
    }

    /// <summary>静置层是否显示底部的播放进度条。 / Whether the rest layer shows its bottom playback-progress bar.</summary>
    public bool RestProgressVisible
    {
        get => _configuration.Current.TaskbarExperience.RestProgressVisible;
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { RestProgressVisible = value });
    }

    /// <summary>有媒体时是否显示静置层封面；无媒体时的小音符由另一个设置控制。/ Whether to show artwork with media; the idle note is controlled separately.</summary>
    public bool RestArtworkVisible
    {
        get => _configuration.Current.TaskbarExperience.ArtworkVisible;
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { ArtworkVisible = value });
    }

    /// <summary>静置层是否显示频谱组件。 / Whether the rest layer shows the spectrum component.</summary>
    public bool SpectrumVisible
    {
        get => _configuration.Current.TaskbarExperience.SpectrumVisible;
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { SpectrumVisible = value });
    }

    /// <summary>静置层是否显示性能组件。 / Whether the rest layer shows the performance component.</summary>
    public bool PerformanceVisible
    {
        get => _configuration.Current.TaskbarExperience.PerformanceVisible;
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { PerformanceVisible = value });
    }

    /// <summary>静置层是否显示输出设备按钮（点击打开设备菜单，滚轮切换设备）。 / Whether the rest layer shows the output-device button, which opens the device menu on click and switches devices on the wheel.</summary>
    public bool RestOutputDeviceVisible
    {
        get => _configuration.Current.TaskbarExperience.OutputDeviceVisible;
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { OutputDeviceVisible = value });
    }

    /// <summary>静置层是否显示音量按钮（点击打开音量菜单，滚轮调节当前来源音量）。 / Whether the rest layer shows the volume button, which opens the volume menu on click and adjusts the current source on the wheel.</summary>
    public bool RestVolumeVisible
    {
        get => _configuration.Current.TaskbarExperience.VolumeVisible;
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { VolumeVisible = value });
    }

    /// <summary>
    /// "没有媒体时显示"列表：快速启动小音符（默认开）、频谱、性能监控、输出设备按钮、音量按钮。
    ///
    /// 封面与媒体文字不在这个列表里、也不提供开关：没有媒体时它们没有任何内容可显示，因此一定不显示；一个都不勾选时整条媒体栏隐藏。
    /// The "shown without media" list: the quick-launch note (on by default), the spectrum, the performance monitor, the output-device button, and the
    /// volume button.
    ///
    /// The artwork and the media text are absent from this list and carry no switch: with no media they have nothing to display and are therefore
    /// never shown; with nothing checked the whole bar is hidden.
    /// </summary>
    public ObservableCollection<TaskbarRestComponentSettingItem> IdleComponentEntries { get; } = [];

    /// <summary>
    /// "没有媒体时显示"列表使用固定顺序：快速启动小音符在最前，其余小组件随后。
    /// The "shown without media" list uses a fixed order: quick-launch note first, followed by the other widgets.
    /// </summary>
    private void RefreshIdleComponentEntries()
    {
        var kept = TaskbarRestLayoutPolicy.ResolveIdleComponents(_configuration.Current.TaskbarExperience.IdleComponents);
        var unchanged = IdleComponentEntries.Count == IdleVisibilityOrder.Count &&
                        IdleComponentEntries.Select(entry => entry.Component).SequenceEqual(IdleVisibilityOrder) &&
                        IdleComponentEntries.All(entry => entry.IsVisible == kept.Contains(entry.Component));
        if (unchanged)
        {
            foreach (var entry in IdleComponentEntries)
            {
                var isNote = entry.Component == TaskbarRestComponent.Artwork;
                entry.DisplayName = Translations.Get(isNote
                    ? TaskbarRestComponentSettingItem.QuickLaunchNoteNameKey
                    : TaskbarRestComponentSettingItem.ResolveNameKey(entry.Component));
                entry.Description = Translations.Get(isNote
                    ? TaskbarRestComponentSettingItem.QuickLaunchNoteDescriptionKey
                    : TaskbarRestComponentSettingItem.ResolveDescriptionKey(entry.Component));
            }
            return;
        }

        foreach (var entry in IdleComponentEntries)
            entry.VisibilityChanged -= OnRestComponentVisibilityChanged;

        IdleComponentEntries.Clear();
        foreach (var component in IdleVisibilityOrder)
        {
            var entry = new TaskbarRestComponentSettingItem(
                component, canMove: false, isVisible: kept.Contains(component),
                useQuickLaunchName: component == TaskbarRestComponent.Artwork);
            entry.VisibilityChanged += OnRestComponentVisibilityChanged;
            IdleComponentEntries.Add(entry);
        }
        OnPropertyChanged(nameof(IdleComponentEntries));
    }

    private static IReadOnlyList<TaskbarRestComponent> IdleVisibilityOrder { get; } =
    [
        TaskbarRestComponent.Artwork,
        TaskbarRestComponent.Spectrum,
        TaskbarRestComponent.Performance,
        TaskbarRestComponent.OutputDevice,
        TaskbarRestComponent.Volume
    ];

    private void OnRestComponentVisibilityChanged(TaskbarRestComponentSettingItem entry) => SaveIdleComponents();

    /// <summary>
    /// 把"没有媒体时显示"写回设置。勾选项恰好等于默认值（只有小音符）时写回"未配置"，
    /// 这样"没动过"与"手动勾回默认"在设置文件里长得一样；一个都不勾时写回空列表，表示整条媒体栏隐藏。
    /// Writes the "shown without media" switches back into the settings. A selection exactly equal to the default (the note alone) is stored as
    /// "never configured", so "never touched" and "switched back to the default" look the same in the settings file; nothing checked is stored as an
    /// empty list, which hides the whole bar.
    /// </summary>
    private void SaveIdleComponents()
    {
        var selected = IdleComponentEntries
            .Where(entry => entry.IsVisible)
            .Select(entry => entry.Component)
            .ToArray();
        var isDefault = selected.SequenceEqual(TaskbarRestLayoutPolicy.DefaultIdleComponents);
        UpdateExperience(_configuration.Current.TaskbarExperience with
        {
            IdleComponents = isDefault ? null : selected
        });
    }

    public bool HoverPlayPauseVisible
    {
        get => HoverControls.PlayPauseVisible;
        set => UpdateHoverControls(HoverControls with { PlayPauseVisible = value });
    }

    public bool HoverPreviousNextVisible
    {
        get => HoverControls.PreviousNextVisible;
        set => UpdateHoverControls(HoverControls with { PreviousNextVisible = value });
    }

    public bool HoverOutputDeviceVisible
    {
        get => HoverControls.OutputDeviceVisible;
        set => UpdateHoverControls(HoverControls with { OutputDeviceVisible = value });
    }

    public bool HoverAudioControlVisible
    {
        get => HoverControls.AudioControlVisible;
        set => UpdateHoverControls(HoverControls with { AudioControlVisible = value });
    }

    public bool HoverProgressVisible
    {
        get => HoverControls.ProgressVisible;
        set => UpdateHoverControls(HoverControls with { ProgressVisible = value });
    }

    private TaskbarHoverControlsSettings HoverControls =>
        _configuration.Current.TaskbarExperience.HoverControls;

    public bool FullPanelMediaInfoVisible
    {
        get => FullPanelSettings.MediaInfoVisible;
        set => UpdateFullPanelGroup(FullPanelGroup.MediaInfo, value);
    }

    public bool FullPanelMediaControlsVisible
    {
        get => FullPanelSettings.MediaControlsVisible;
        set => UpdateFullPanelGroup(FullPanelGroup.MediaControls, value);
    }

    public bool FullPanelAudioControlsVisible
    {
        get => FullPanelSettings.AudioControlsVisible;
        set => UpdateFullPanelGroup(FullPanelGroup.AudioControls, value);
    }

    public bool FullPanelPerformanceVisible
    {
        get => FullPanelSettings.PerformanceVisible;
        set => UpdateFullPanelGroup(FullPanelGroup.Performance, value);
    }

    public bool CanToggleFullPanelMediaInfo => CanToggleFullPanelGroup(FullPanelSettings.MediaInfoVisible);
    public bool CanToggleFullPanelMediaControls => CanToggleFullPanelGroup(FullPanelSettings.MediaControlsVisible);
    public bool CanToggleFullPanelAudioControls => CanToggleFullPanelGroup(FullPanelSettings.AudioControlsVisible);
    public bool CanToggleFullPanelPerformance => CanToggleFullPanelGroup(FullPanelSettings.PerformanceVisible);

    /// <summary>
    /// 完整层当前生效的分区组合，作为状态文本显示。三个名称与「套用预设」按钮上的文案取自同一批键。
    /// The section combination the full panel currently uses, shown as a status text. The three names come from the
    /// same keys as the captions on the "apply a preset" buttons.
    /// </summary>
    public string FullPanelLayoutStatus => FullPanelSettings == TaskbarFullPanelSettings.Compact
        ? Translations.Get("DisplayModes.Full.Preset.Compact")
        : FullPanelSettings == TaskbarFullPanelSettings.Full
            ? Translations.Get("DisplayModes.Full.Preset.Full")
            : Translations.Get("DisplayModes.Full.Preset.Custom");

    private TaskbarFullPanelSettings FullPanelSettings => _configuration.Current.TaskbarExperience.FullPanel.Normalize();

    public LayoutOrientationMode Orientation => LayoutOrientationMode.Auto;
    /// <summary>Actual taskbar orientation on the editor display.</summary>
    public string OrientationText => Translations.Get(_configuration.Context.Orientation switch
    {
        LayoutOrientation.Vertical => "DisplayModes.Placement.Orientation.Vertical",
        LayoutOrientation.Horizontal => "DisplayModes.Placement.Orientation.Horizontal",
        _ => "Settings.Context.Unavailable"
    });

    /// <summary>横向任务栏对齐设置是否适用于当前编辑环境。</summary>
    public bool IsHorizontalLayout => _configuration.Context.Orientation != LayoutOrientation.Vertical;

    /// <summary>任务栏主轴对齐；切换时清除拖动偏移和手动内容排布。</summary>
    public TaskbarBarPosition TaskbarPosition
    {
        get => _configuration.Current.Position;
        set
        {
            if (_disposed || !_configuration.IsActive || _isRefreshing || !IsTaskbarMode ||
                !Enum.IsDefined(value) || _configuration.Current.Position == value)
                return;
            _configuration.SetTaskbarPlacement(alignment: value);
        }
    }

    /// <summary>横向任务栏的左侧或右侧内容排布。</summary>
    public TaskbarContentArrangement TaskbarArrangement
    {
        get => TaskbarArrangementPolicy.ResolveContent(
            _configuration.Current.TaskbarExperience.Normalize().Arrangement, _configuration.Current.Position);
        set => UpdateExperience(_configuration.Current.TaskbarExperience with { Arrangement = value });
    }

    public bool IsTaskbarPositionLocked
    {
        get => _configuration.Current.TaskbarBarPositionLocked;
        set { _configuration.SetTaskbarPlacement(locked: value); OnPropertyChanged(); }
    }

    public bool IsTaskbarAvoidingIcons
    {
        get => _configuration.Current.TaskbarBarAvoidIcons;
        set
        {
            _configuration.SetTaskbarPlacement(avoidIcons: value);
            OnPropertyChanged();
        }
    }

    public double TaskbarCrossAxisOffsetDip
    {
        get => _configuration.Current.TaskbarBarCrossAxisOffsetDip;
        set
        {
            _configuration.SetTaskbarPlacement(crossAxisOffsetDip: value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 创建显示模式页的视图模型并订阅设置、显示器与语言变化。
    /// 页面缓存释放时退订；显示器快照由平台服务提供。
    /// Creates the display-mode view model and subscribes to settings, monitor, and language changes.
    /// Subscriptions are released with this page scope; platform queries belong to the environment reader.
    /// </summary>
    /// <param name="displayMonitorService">显示器目录，供目标显示器下拉框使用。/ Display catalog behind the target-monitor drop-down.</param>
    /// <param name="localization">
    /// 界面语言。本页由代码拼出的页头状态、显示器名称和组件名在语言变化时重新取值。
    /// 它是必填依赖：可省略的依赖会留下一条"忘记注入就静默不刷新"的路径，而这里没有合理的缺省语言。
    /// Interface language. The composed header status, monitor names, and component names must refresh when the language changes. It is a required dependency:
    /// an omittable one leaves a path where forgetting to inject it silently skips the refresh, and there is no sensible
    /// default language here.
    /// </param>
    public DisplayModesViewModel(
        IDisplayMonitorService displayMonitorService,
        LocalizationService localization, ISettingsConfiguration configuration)
    {
        _displayMonitorService = displayMonitorService;
        _localization = localization;
        _configuration = configuration;
        configuration.Activated += OnActivated;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _displayMonitorService.MonitorsChanged += OnMonitorsChanged;

        // 代码拼出来的文案不会随 XAML 的动态资源一起更新，因此在语言变化时重新通知一遍。
        // Text composed in code does not follow the XAML dynamic resources, so it is announced again when the language
        // changes.
        _localization.LanguageChanged += OnLanguageChanged;

        RefreshMonitorOptions();
        // 无媒体时保留组件列表由代码构建，首次打开页面前必须填充。
        // The idle-component list is built in code and must be populated before the page first opens.
        RefreshIdleComponentEntries();
    }

    /// <summary>
    /// 语言变化后重新取值：显示器名称是构建下拉框列表时拼出来的字符串，不会随绑定自己更新，因此列表重建一次；
    /// 空的属性名让 WPF 重读其余全部绑定，避免逐个列出属性名时漏掉一个而留下半页旧语言。
    /// Re-reads the composed strings after a language change: monitor names are built while the drop-down list is
    /// created and do not follow a binding on their own, so that list is rebuilt, and the empty property name makes WPF
    /// re-read every other binding instead of listing properties one by one and missing one.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshMonitorOptions();
        // 组件名与说明是构建列表项时取的语言快照，不会随空属性名的通知自己更新（列表实例没变，项的属性也没变），
        // 因此这里必须显式刷新一次，否则语言切换后这一列仍是旧语言。
        // The component names and descriptions are a language snapshot taken while the items were built and do not follow the empty-property
        // notification on their own (the list instance is unchanged and so are the items' properties), so this has to refresh explicitly;
        // otherwise the column would keep the previous language after a switch.
        RefreshIdleComponentEntries();
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand] private void SwitchToTaskbarMode() => SelectMode(DisplayModeSelection.Taskbar);
    [RelayCommand] private void SwitchToDynamicIslandMode() => SelectMode(DisplayModeSelection.DynamicIsland);
    [RelayCommand] private void SwitchToDesktopCardMode() => SelectMode(DisplayModeSelection.DesktopCard);
    [RelayCommand] private void SwitchToFloatingBallMode() => SelectMode(DisplayModeSelection.FloatingBall);
    [RelayCommand] private void ApplyCompactFullPanelPreset() => UpdateFullPanel(TaskbarFullPanelSettings.Compact);
    [RelayCommand] private void ApplyFullFullPanelPreset() => UpdateFullPanel(TaskbarFullPanelSettings.Full);

    [RelayCommand]
    private void ResetTaskbarPosition()
    {
        _configuration.SetTaskbarPlacement(reset: true);
    }

    private void SelectMode(DisplayModeSelection mode)
    {
        // No other runtime host is implemented. Preview clicks must never mutate runtime mode or expose fake editors.
        if (mode != DisplayModeSelection.Taskbar) return;
        OnPropertyChanged(nameof(SelectedMode));
    }

    private void UpdateExperience(TaskbarExperienceSettings value)
    {
        if (_disposed || !_configuration.IsActive || _isRefreshing || !IsTaskbarMode) return;
        _configuration.SetTaskbarExperience(value.Normalize());
        RaiseExperience();
    }

    private void UpdateHoverControls(TaskbarHoverControlsSettings controls) =>
        UpdateExperience(_configuration.Current.TaskbarExperience with { HoverControls = controls });

    private void UpdateFullPanelGroup(FullPanelGroup group, bool visible)
    {
        if (_isRefreshing) return;
        var current = FullPanelSettings;
        var updated = group switch
        {
            FullPanelGroup.MediaInfo => current with { MediaInfoVisible = visible },
            FullPanelGroup.MediaControls => current with { MediaControlsVisible = visible },
            FullPanelGroup.AudioControls => current with { AudioControlsVisible = visible },
            FullPanelGroup.Performance => current with { PerformanceVisible = visible },
            _ => current
        };
        if (!updated.MediaInfoVisible && !updated.MediaControlsVisible &&
            !updated.AudioControlsVisible && !updated.PerformanceVisible)
        {
            RaiseFullPanel();
            return;
        }
        UpdateFullPanel(updated);
    }

    private void UpdateFullPanel(TaskbarFullPanelSettings settings) =>
        UpdateExperience(_configuration.Current.TaskbarExperience with { FullPanel = settings.Normalize() });

    private bool CanToggleFullPanelGroup(bool visible) => !visible || VisibleFullPanelGroupCount() > 1;

    private int VisibleFullPanelGroupCount()
    {
        var settings = FullPanelSettings;
        return (settings.MediaInfoVisible ? 1 : 0) +
               (settings.MediaControlsVisible ? 1 : 0) +
               (settings.AudioControlsVisible ? 1 : 0) +
               (settings.PerformanceVisible ? 1 : 0);
    }

    public void ResetDisplayModes() => _configuration.Reset(SettingsResetScope.DisplayModes);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (_disposed || !_configuration.IsActive) return;
        if (!_dispatcher.CheckAccess()) { DispatcherHelper.Run(_dispatcher, () => OnSettingsChanged(sender, e)); return; }
        if (e.ResetScope is SettingsResetScope.DisplayModes or SettingsResetScope.Layout or SettingsResetScope.All)
            RaiseAll();
        else if (!_isRefreshing && (e.PropertyName is nameof(AppSettings.Position) or nameof(AppSettings.TaskbarExperience)))
        {
            // Arrangement is derived from both properties. Publish their effective values while
            // suppressing two-way target refresh from creating a manual override.
            _isRefreshing = true;
            try
            {
                OnPropertyChanged(nameof(TaskbarPosition));
                if (e.PropertyName == nameof(AppSettings.TaskbarExperience))
                    RaiseExperience();
                else
                    OnPropertyChanged(nameof(TaskbarArrangement));
            }
            finally { _isRefreshing = false; }
        }
        else if (!_isRefreshing && e.PropertyName is nameof(AppSettings.TaskbarTargetMonitorDeviceIds) or nameof(AppSettings.TaskbarTargetMonitorDeviceId))
            RefreshMonitorOptions();
    }

    private void OnMonitorsChanged(object? sender, EventArgs e) => DispatcherHelper.Run(_dispatcher, () => { if (!_disposed && _configuration.IsActive) RefreshMonitorOptions(); });

    private void RefreshMonitorOptions()
    {
        // 显示器名称在这里拼装成列表项，因此「主显示器」后缀由文案提供：它与媒体与通知页的显示器列表共用同一个键，
        // 两处必须逐字一致；中文用全角括号、英文需要一个空格，这个空格归文案所有，代码不猜语言。
        // Monitor names are composed into the list items here, so the "primary" suffix comes from the text registry under
        // the same key as the media-and-notifications monitor list, and the two have to match word for word. Chinese uses
        // full-width brackets while English needs a space, so that space belongs to the text rather than to code that would
        // have to guess the language.
        var primarySuffix = Translations.Get("Common.Monitor.PrimarySuffix");
        var disconnectedSuffix = Translations.Get("DisplayModes.Monitor.DisconnectedSuffix");
        var monitors = _displayMonitorService.GetMonitors();
        var availableOptions = monitors
            .Select((monitor, index) => new DisplayMonitorOption(
                monitor.DeviceId,
                $"{index + 1}. {monitor.DeviceName}{(monitor.IsPrimary ? primarySuffix : string.Empty)}",
                monitor.IsPrimary))
            .ToList();


        var selectedTaskbarIds = ResolveConfiguredTaskbarSelection(monitors);
        var taskbarOptions = availableOptions
            .Select(option => new TaskbarMonitorSelectionItem(
                option.DeviceId,
                option.DisplayName,
                option.IsPrimary,
                option.IsAvailable,
                selectedTaskbarIds.Contains(option.DeviceId)))
            .ToList();

        foreach (var deviceId in selectedTaskbarIds.Where(deviceId =>
                     availableOptions.All(option => !string.Equals(option.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))))
        {
            taskbarOptions.Add(new TaskbarMonitorSelectionItem(
                deviceId,
                $"{deviceId}{disconnectedSuffix}",
                false,
                false,
                true));
        }

        foreach (var option in _taskbarMonitorOptions) option.SelectionChanged -= OnTaskbarMonitorSelectionChanged;
        foreach (var option in taskbarOptions)
            option.SelectionChanged += OnTaskbarMonitorSelectionChanged;

        _taskbarMonitorOptions = taskbarOptions;
        UpdateTaskbarMonitorToggleState();
        OnPropertyChanged(nameof(TaskbarMonitorOptions));
    }

    private HashSet<string> ResolveConfiguredTaskbarSelection(IReadOnlyList<DisplayMonitorInfo> monitors)
    {
        var selected = (_configuration.Current.TaskbarTargetMonitorDeviceIds ?? [])
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count > 0)
            return selected;

        var legacy = _configuration.Current.TaskbarTargetMonitorDeviceId;
        if (TaskbarTargetPolicy.IsLegacyAllTaskbars(legacy))
            selected.UnionWith(monitors.Select(monitor => monitor.DeviceId));
        else if (!string.IsNullOrWhiteSpace(legacy))
            selected.Add(legacy);
        else if (_displayMonitorService.ResolveFixedMonitor(null) is { } primary)
            selected.Add(primary.DeviceId);
        return selected;
    }

    private void OnTaskbarMonitorSelectionChanged(TaskbarMonitorSelectionItem changed)
    {
        if (_disposed || !_configuration.IsActive || _isRefreshing)
            return;

        var selected = _taskbarMonitorOptions.Where(option => option.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            changed.IsSelected = true;
            return;
        }

        var deviceIds = selected.Select(option => option.DeviceId).ToArray();
        var current = _configuration.Current.TaskbarTargetMonitorDeviceIds ?? [];
        _isRefreshing = true;
        try
        {
            _configuration.SetTaskbarTargets(deviceIds);
        }
        finally
        {
            _isRefreshing = false;
        }
        UpdateTaskbarMonitorToggleState();
    }

    private void UpdateTaskbarMonitorToggleState()
    {
        var selectedCount = _taskbarMonitorOptions.Count(option => option.IsSelected);
        foreach (var option in _taskbarMonitorOptions)
            option.CanToggle = !option.IsSelected || selectedCount > 1;
    }

    private void RaiseAll()
    {
        _isRefreshing = true;
        try
        {
            OnPropertyChanged(nameof(CurrentWindowMode)); OnPropertyChanged(nameof(IsTaskbarMode)); OnPropertyChanged(nameof(IsDynamicIslandMode));
            OnPropertyChanged(nameof(IsDesktopCardMode)); OnPropertyChanged(nameof(IsFloatingBallMode)); OnPropertyChanged(nameof(IsUnimplementedMode));
            OnPropertyChanged(nameof(HostingModeText)); OnPropertyChanged(nameof(IsTaskbarHostingActive));
            OnPropertyChanged(nameof(TaskbarPosition));
            RaiseExperience(); OnPropertyChanged(nameof(Orientation)); OnPropertyChanged(nameof(IsTaskbarPositionLocked));
            OnPropertyChanged(nameof(IsTaskbarAvoidingIcons)); OnPropertyChanged(nameof(TaskbarCrossAxisOffsetDip));
            RefreshMonitorOptions();
        }
        finally { _isRefreshing = false; }
    }

    private void RaiseExperience()
    {
        OnPropertyChanged(nameof(TaskbarArrangement));
        OnPropertyChanged(nameof(HoverLayerEnabled)); OnPropertyChanged(nameof(FullLayerEnabled));
        OnPropertyChanged(nameof(FullPanelEntryVisible));
        OnPropertyChanged(nameof(RestProgressVisible));
        OnPropertyChanged(nameof(RestArtworkVisible));
        OnPropertyChanged(nameof(SpectrumVisible)); OnPropertyChanged(nameof(PerformanceVisible));
        OnPropertyChanged(nameof(HoverPlayPauseVisible)); OnPropertyChanged(nameof(HoverPreviousNextVisible));
        OnPropertyChanged(nameof(HoverOutputDeviceVisible)); OnPropertyChanged(nameof(HoverAudioControlVisible));
        OnPropertyChanged(nameof(HoverProgressVisible));
        OnPropertyChanged(nameof(RestOutputDeviceVisible)); OnPropertyChanged(nameof(RestVolumeVisible));
        RefreshIdleComponentEntries();
        RaiseFullPanel();
    }

    private void RaiseFullPanel()
    {
        OnPropertyChanged(nameof(FullPanelMediaInfoVisible));
        OnPropertyChanged(nameof(FullPanelMediaControlsVisible));
        OnPropertyChanged(nameof(FullPanelAudioControlsVisible));
        OnPropertyChanged(nameof(FullPanelPerformanceVisible));
        OnPropertyChanged(nameof(CanToggleFullPanelMediaInfo));
        OnPropertyChanged(nameof(CanToggleFullPanelMediaControls));
        OnPropertyChanged(nameof(CanToggleFullPanelAudioControls));
        OnPropertyChanged(nameof(CanToggleFullPanelPerformance));
        OnPropertyChanged(nameof(FullPanelLayoutStatus));
    }

    private enum FullPanelGroup
    {
        MediaInfo,
        MediaControls,
        AudioControls,
        Performance
    }

    private void OnActivated(object? sender, EventArgs e) => RaiseAll();
    /// <summary>Releases monitor, language and settings subscriptions when this page scope closes.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _displayMonitorService.MonitorsChanged -= OnMonitorsChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        _configuration.Activated -= OnActivated;
        foreach (var item in _taskbarMonitorOptions) item.SelectionChanged -= OnTaskbarMonitorSelectionChanged;
    }

}
