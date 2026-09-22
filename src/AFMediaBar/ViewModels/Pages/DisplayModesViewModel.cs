using AFMediaBar.Classes.Models;
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

/// <summary>四种显示模式及任务栏轻度自定义。 / Four display modes and light taskbar customization.</summary>
public partial class DisplayModesViewModel : ObservableObject
{
    private readonly IDisplayMonitorService _displayMonitorService;
    private readonly TaskbarLengthConstraintsService _taskbarLengthConstraints;
    private readonly LocalizationService _localization;
    private bool _isRefreshing;
    private DisplayModeSelection _selectedMode = DisplayModeSelection.Taskbar;
    private IReadOnlyList<DisplayMonitorOption> _monitorOptions = Array.Empty<DisplayMonitorOption>();
    private IReadOnlyList<TaskbarMonitorSelectionItem> _taskbarMonitorOptions = Array.Empty<TaskbarMonitorSelectionItem>();

    public IReadOnlyList<DisplayMonitorOption> MonitorOptions => _monitorOptions;
    /// <summary>可逐项勾选的任务栏目标列表；至少保留一项，既支持单选也支持多选。/ Individually selectable taskbar targets; at least one remains selected, supporting one or many.</summary>
    public IReadOnlyList<TaskbarMonitorSelectionItem> TaskbarMonitorOptions => _taskbarMonitorOptions;
    public WindowMode CurrentWindowMode => SettingsManager.Current.WindowMode;
    public DisplayModeSelection SelectedMode => _selectedMode;
    public bool IsTaskbarMode => SelectedMode == DisplayModeSelection.Taskbar;
    public bool IsDynamicIslandMode => SelectedMode == DisplayModeSelection.DynamicIsland;
    public bool IsDesktopCardMode => SelectedMode == DisplayModeSelection.DesktopCard;
    public bool IsFloatingBallMode => SelectedMode == DisplayModeSelection.FloatingBall;
    public bool IsUnimplementedMode => !IsTaskbarMode;

    /// <summary>
    /// 当前显示模式的文本，供页头状态芯片显示。它读的是真实 <see cref="WindowMode"/>，
    /// 而不是本页的预览选择——页内选择只改变高亮，从不切换窗口，两者不能混为一谈。
    /// 模式名取自与模式卡片相同的键，因此芯片与卡片任何时候都写作同一个词。
    /// Text for the header status chip. It reads the real <see cref="WindowMode"/> rather than this page's
    /// preview selection, because the in-page selection only changes the highlight and never switches the
    /// window; the two must not be conflated. The mode name comes from the same keys the mode cards use, so the
    /// chip and the cards always read as one word.
    /// </summary>
    public string HostingModeText => Translations.Format(
        "DisplayModes.Status.Current",
        Translations.Get(CurrentWindowMode == WindowMode.Taskbar ? "DisplayModes.Mode.Taskbar" : "Common.DynamicIsland"));

    /// <summary>
    /// 任务栏是否就是当前运行模式。模式卡片用它决定“当前模式”芯片是否显示，
    /// 取代以前写死在任务栏卡片上的那个芯片。
    /// Whether the taskbar really is the running mode. The mode cards use it to decide whether the
    /// "current mode" chip shows, replacing the chip that used to be hardcoded on the taskbar card.
    /// </summary>
    public bool IsTaskbarHostingActive => CurrentWindowMode == WindowMode.Taskbar;

    /// <summary>灵动岛背景方案。/ Dynamic-island background scheme.</summary>
    public DynamicIslandBackgroundMode DynamicIslandBackgroundMode
    {
        get => SettingsManager.Current.DynamicIslandBackgroundMode;
        set
        {
            if (_isRefreshing || SettingsManager.Current.DynamicIslandBackgroundMode == value) return;
            SettingsManager.Current.DynamicIslandBackgroundMode = value;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    /// <summary>灵动岛贴靠边缘。/ Edge where the dynamic island docks.</summary>
    public DynamicIslandEdge DynamicIslandEdge
    {
        get => SettingsManager.Current.DynamicIslandEdge;
        set
        {
            if (_isRefreshing || SettingsManager.Current.DynamicIslandEdge == value) return;
            SettingsManager.Current.DynamicIslandEdge = value;
            SettingsManager.Current.DynamicIslandEdgeDocked = true;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    public bool TrackChangeNotificationEnabled
    {
        get => NotificationSettings.Enabled;
        set => UpdateNotification(NotificationSettings with { Enabled = value });
    }

    public bool ShowTrackChangeNotificationWhenFullscreen
    {
        get => NotificationSettings.ShowWhenFullscreen;
        set => UpdateNotification(NotificationSettings with { ShowWhenFullscreen = value });
    }

    public int TrackChangeNotificationDurationSeconds
    {
        get => NotificationSettings.DurationMilliseconds / 1000;
        set => UpdateNotification(NotificationSettings with { DurationMilliseconds = value * 1000 });
    }

    public TrackChangeNotificationPosition TrackChangeNotificationPosition
    {
        get => NotificationSettings.Position;
        set => UpdateNotification(NotificationSettings with { Position = value });
    }

    public NotificationTargetMode TrackChangeNotificationTargetMode
    {
        get => NotificationSettings.TargetMode;
        set => UpdateNotification(NotificationSettings with { TargetMode = value });
    }

    public string? TrackChangeNotificationFixedMonitorDeviceId
    {
        get => NotificationSettings.FixedMonitorDeviceId ?? _displayMonitorService.ResolveFixedMonitor(null)?.DeviceId;
        set => UpdateNotification(NotificationSettings with { FixedMonitorDeviceId = value });
    }

    public bool CanSelectTrackChangeNotificationMonitor =>
        TrackChangeNotificationEnabled && TrackChangeNotificationTargetMode == NotificationTargetMode.Fixed;

    public bool HoverLayerEnabled
    {
        get => SettingsManager.Current.TaskbarExperience.HoverLayerEnabled;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { HoverLayerEnabled = value });
    }

    public bool FullLayerEnabled
    {
        get => SettingsManager.Current.TaskbarExperience.FullLayerEnabled;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { FullLayerEnabled = value });
    }

    public TaskbarInformationDensity Density
    {
        get => SettingsManager.Current.TaskbarExperience.Density;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { Density = value });
    }

    public TaskbarContentLayout ContentLayout
    {
        get => SettingsManager.Current.TaskbarExperience.ContentLayout;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { ContentLayout = value });
    }

    /// <summary>任务栏标题和歌手文字的对齐方式。 / Alignment of taskbar title and artist text.</summary>
    public TaskbarMediaTextAlignment MediaTextAlignment
    {
        get => SettingsManager.Current.TaskbarExperience.MediaTextAlignment;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { MediaTextAlignment = value });
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
        get => SettingsManager.Current.TaskbarExperience.FullPanelEntryVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { FullPanelEntryVisible = value });
    }

    /// <summary>静置层是否显示底部的播放进度条。 / Whether the rest layer shows its bottom playback-progress bar.</summary>
    public bool RestProgressVisible
    {
        get => SettingsManager.Current.TaskbarExperience.RestProgressVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { RestProgressVisible = value });
    }

    /// <summary>静置层是否显示频谱组件。 / Whether the rest layer shows the spectrum component.</summary>
    public bool SpectrumVisible
    {
        get => SettingsManager.Current.TaskbarExperience.SpectrumVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { SpectrumVisible = value });
    }

    /// <summary>静置层是否显示性能组件。 / Whether the rest layer shows the performance component.</summary>
    public bool PerformanceVisible
    {
        get => SettingsManager.Current.TaskbarExperience.PerformanceVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { PerformanceVisible = value });
    }

    /// <summary>静置层是否显示输出设备按钮（点击打开设备菜单，滚轮切换设备）。 / Whether the rest layer shows the output-device button, which opens the device menu on click and switches devices on the wheel.</summary>
    public bool RestOutputDeviceVisible
    {
        get => SettingsManager.Current.TaskbarExperience.OutputDeviceVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { OutputDeviceVisible = value });
    }

    /// <summary>静置层是否显示音量按钮（点击打开音量菜单，滚轮调节当前来源音量）。 / Whether the rest layer shows the volume button, which opens the volume menu on click and adjusts the current source on the wheel.</summary>
    public bool RestVolumeVisible
    {
        get => SettingsManager.Current.TaskbarExperience.VolumeVisible;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { VolumeVisible = value });
    }

    /// <summary>
    /// 静置层顺序列表：前两行是固定在最前面的封面与媒体文字（不提供上下移），其后是可排序的频谱、性能、输出设备、音量。
    ///
    /// 封面与媒体文字不参与排序是刻意的：它们是这条媒体栏的主干，此前允许移动时界面会错乱，因此现在固定在开头。
    /// The rest-layer order list: the first two rows are the artwork and the media text pinned to the front (with no move buttons), followed by the
    /// reorderable spectrum, performance, output device, and volume.
    ///
    /// The artwork and the media text deliberately take no part in ordering: they are the backbone of the bar, and allowing them to move broke the
    /// layout, so they are pinned to the front.
    /// </summary>
    public ObservableCollection<TaskbarRestComponentSettingItem> RestOrderEntries { get; } = [];

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
    /// 重建两张静置层列表（顺序按设置、显隐按"没有媒体时保留"的取值）。
    ///
    /// 顺序与显隐没有变化时只重取显示名（语言可能刚换过）：列表里没有任何变化时重建它，会让 ItemsControl 重新生成全部行，
    /// 拖动一个无关的滑杆也会把这两份列表闪一下。
    /// Rebuilds both rest-layer lists (the order from the settings, the switches from the "kept without media" value).
    ///
    /// When neither the order nor the switches changed, only the display names are re-read (the language may have just changed): rebuilding a list in
    /// which nothing changed makes the ItemsControl regenerate every row and flashes both lists even while an unrelated slider is dragged.
    /// </summary>
    private void RefreshRestComponentEntries()
    {
        var experience = SettingsManager.Current.TaskbarExperience.Normalize();
        var order = TaskbarRestLayoutPolicy.ResolveOrder(experience.RestComponentOrder);
        var kept = TaskbarRestLayoutPolicy.ResolveIdleComponents(experience.IdleComponents);

        var orderUnchanged = RestOrderEntries.Count == order.Count &&
                             RestOrderEntries.Select(entry => entry.Component).SequenceEqual(order);
        var idleUnchanged = IdleComponentEntries.Count == IdleVisibilityOrder.Count &&
                            IdleComponentEntries.Select(entry => entry.Component).SequenceEqual(IdleVisibilityOrder) &&
                            IdleComponentEntries.All(entry => entry.IsVisible == kept.Contains(entry.Component));

        if (orderUnchanged && idleUnchanged)
        {
            foreach (var entry in RestOrderEntries)
            {
                entry.DisplayName = Translations.Get(TaskbarRestComponentSettingItem.ResolveNameKey(entry.Component));
                entry.Description = Translations.Get(TaskbarRestComponentSettingItem.ResolveDescriptionKey(entry.Component));
            }

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

        foreach (var entry in RestOrderEntries)
        {
            entry.VisibilityChanged -= OnRestComponentVisibilityChanged;
        }

        foreach (var entry in IdleComponentEntries)
        {
            entry.VisibilityChanged -= OnRestComponentVisibilityChanged;
        }

        RestOrderEntries.Clear();
        foreach (var component in order)
        {
            RestOrderEntries.Add(new TaskbarRestComponentSettingItem(
                component,
                canMove: !TaskbarRestLayoutPolicy.FixedOrder.Contains(component),
                isVisible: true));
        }

        IdleComponentEntries.Clear();
        foreach (var component in IdleVisibilityOrder)
        {
            var entry = new TaskbarRestComponentSettingItem(
                component,
                canMove: false,
                isVisible: kept.Contains(component),
                useQuickLaunchName: component == TaskbarRestComponent.Artwork);
            entry.VisibilityChanged += OnRestComponentVisibilityChanged;
            IdleComponentEntries.Add(entry);
        }

        OnPropertyChanged(nameof(RestOrderEntries));
        OnPropertyChanged(nameof(IdleComponentEntries));
    }

    /// <summary>
    /// "没有媒体时显示"列表的条目集合与顺序：快速启动小音符在最前，其余按顺序列表里排出来的先后。
    /// The set and order of the "shown without media" list: the quick-launch note first, the rest in the order the order list arranged them.
    /// </summary>
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
        UpdateExperience(SettingsManager.Current.TaskbarExperience with
        {
            IdleComponents = isDefault ? null : selected
        });
    }

    [RelayCommand]
    private void MoveRestComponentUp(TaskbarRestComponentSettingItem? entry) => MoveRestComponent(entry, -1);

    [RelayCommand]
    private void MoveRestComponentDown(TaskbarRestComponentSettingItem? entry) => MoveRestComponent(entry, 1);

    /// <summary>
    /// 在顺序列表里移动一行。只有可排序组件会被写入设置：封面与媒体文字虽然在列表里（用户要看得见自己在排序什么），
    /// 但它们的位置不参与排序，也不会被写进设置文件。
    /// Moves one row inside the order list. Only reorderable components reach the settings file: the artwork and the media text are listed (the user
    /// has to see what they are ordering around) but their position takes no part in ordering and is never stored.
    /// </summary>
    private void MoveRestComponent(TaskbarRestComponentSettingItem? entry, int offset)
    {
        if (entry is null || !entry.CanMove)
        {
            return;
        }

        var index = RestOrderEntries.IndexOf(entry);
        var target = index + offset;
        // 固定头占着列表最前面几行，可排序组件的目标位置不能越过它们。
        // The pinned head occupies the first rows, and a reorderable component must not move past them.
        if (index < 0 || target < TaskbarRestLayoutPolicy.FixedOrder.Count || target >= RestOrderEntries.Count)
        {
            return;
        }

        RestOrderEntries.Move(index, target);
        SaveRestComponentOrder();
    }

    /// <summary>把可排序段的顺序写回设置：恰好等于默认顺序时写回"未配置"，与歌词来源列表同一处理。/ Writes the reorderable order back into the settings: an order exactly equal to the default is stored as "never configured", the same handling as the lyric-source list.</summary>
    private void SaveRestComponentOrder()
    {
        var ordered = RestOrderEntries
            .Where(entry => entry.CanMove)
            .Select(entry => entry.Component)
            .ToArray();
        var isDefault = ordered.SequenceEqual(TaskbarRestLayoutPolicy.DefaultTailOrder);
        UpdateExperience(SettingsManager.Current.TaskbarExperience with
        {
            RestComponentOrder = isDefault ? null : ordered
        });
    }

    [RelayCommand]
    private void ResetRestComponentOrder()
    {
        var ordered = RestOrderEntries
            .OrderBy(entry => entry.CanMove
                ? TaskbarRestLayoutPolicy.DefaultTailOrder.ToList().IndexOf(entry.Component) + TaskbarRestLayoutPolicy.FixedOrder.Count
                : TaskbarRestLayoutPolicy.FixedOrder.ToList().IndexOf(entry.Component))
            .ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var current = RestOrderEntries.IndexOf(ordered[i]);
            if (current != i)
            {
                RestOrderEntries.Move(current, i);
            }
        }

        SaveRestComponentOrder();
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
        SettingsManager.Current.TaskbarExperience.HoverControls;

    /// <summary>当前模式的组件间距（DIP）。/ Component gap for the current mode in DIP.</summary>
    public double ComponentSpacingDip
    {
        get => SettingsManager.Current.TaskbarExperience.ComponentSpacingDip;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { ComponentSpacingDip = value });
    }

    public bool FollowMediaTextLength
    {
        get => SettingsManager.Current.TaskbarExperience.LengthMode == TaskbarLengthMode.FollowContent;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with
        {
            LengthMode = value ? TaskbarLengthMode.FollowContent : TaskbarLengthMode.Fixed,
            FixedLengthDip = value
                ? SettingsManager.Current.TaskbarExperience.FixedLengthDip
                : Math.Clamp(
                    SettingsManager.Current.TaskbarExperience.FixedLengthDip,
                    FixedTaskbarLengthMinimum,
                    FixedTaskbarLengthMaximum)
        });
    }

    public bool UsesFixedTaskbarLength => !FollowMediaTextLength;
    public double FixedTaskbarLengthMinimum => Math.Ceiling(_taskbarLengthConstraints.MinimumLengthDip);
    public double FixedTaskbarLengthMaximum => Math.Max(FixedTaskbarLengthMinimum, Math.Floor(_taskbarLengthConstraints.MaximumLengthDip));

    public double FixedTaskbarLengthDip
    {
        get => Math.Clamp(
            SettingsManager.Current.TaskbarExperience.FixedLengthDip,
            FixedTaskbarLengthMinimum,
            FixedTaskbarLengthMaximum);
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with
        {
            FixedLengthDip = Math.Clamp(value, FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum)
        });
    }

    /// <summary>固定宽度的可用范围提示，随任务栏长度约束变化；单位与默认值一样放在括号与数字里。/ Tooltip for the fixed width's available range, which follows the taskbar length constraints; the unit sits with the numbers as it does for every default value.</summary>
    public string FixedTaskbarLengthRangeText =>
        Translations.Format("DisplayModes.Width.RangeText", FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum);

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

    private TaskbarFullPanelSettings FullPanelSettings => SettingsManager.Current.TaskbarExperience.FullPanel.Normalize();

    /// <summary>
    /// 灵动岛模式自己的背景样式。它写在 <c>DynamicIslandSurface</c> 上，与任务栏表面互相独立。
    /// 界面位于显示模式页的灵动岛分区，因此属性也归这里，避免同一份设置被两个视图模型各写一次。
    /// The dynamic island's own background style, stored on <c>DynamicIslandSurface</c> and independent of the
    /// taskbar surface. The interface lives in the display-mode page's island section, so the property lives here
    /// too and one setting is never written from two view models.
    /// </summary>
    public PlayerSurfaceStyle IslandSurfaceStyle
    {
        get => IslandSurface.Style;
        set => PublishIslandSurface(IslandSurface with { Style = value });
    }

    /// <inheritdoc cref="IslandSurfaceStyle" />
    public int IslandSurfaceOpacityPercent
    {
        get => IslandSurface.BackgroundOpacityPercent;
        set => PublishIslandSurface(IslandSurface with { BackgroundOpacityPercent = value });
    }

    /// <inheritdoc cref="IslandSurfaceStyle" />
    public double IslandSurfaceCornerRadiusDip
    {
        get => IslandSurface.CornerRadiusDip;
        set => PublishIslandSurface(IslandSurface with { CornerRadiusDip = value });
    }

    private static ModeSurfaceSettings IslandSurface => SettingsManager.Current.DynamicIslandSurface;

    private void PublishIslandSurface(ModeSurfaceSettings settings)
    {
        SettingsManager.Current.DynamicIslandSurface = settings.Normalize();
        OnPropertyChanged(nameof(IslandSurfaceStyle));
        OnPropertyChanged(nameof(IslandSurfaceOpacityPercent));
        OnPropertyChanged(nameof(IslandSurfaceCornerRadiusDip));
    }

    public LayoutOrientationMode Orientation
    {
        get => SettingsManager.Current.LayoutOrientationMode;
        set
        {
            if (_isRefreshing || SettingsManager.Current.LayoutOrientationMode == value) return;
            SettingsManager.Current.LayoutOrientationMode = value;
            SettingsManager.RaiseLayoutSettingsChanged(SettingsManager.Current.WindowMode, value);
            OnPropertyChanged();
        }
    }

    public bool IsTaskbarPositionLocked
    {
        get => SettingsManager.Current.TaskbarBarPositionLocked;
        set { SettingsManager.Current.TaskbarBarPositionLocked = value; OnPropertyChanged(); }
    }

    public bool IsTaskbarAvoidingIcons
    {
        get => SettingsManager.Current.TaskbarBarAvoidIcons;
        set
        {
            SettingsManager.Current.TaskbarBarAvoidIcons = value;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    public double TaskbarCrossAxisOffsetDip
    {
        get => SettingsManager.Current.TaskbarBarCrossAxisOffsetDip;
        set
        {
            SettingsManager.Current.TaskbarBarCrossAxisOffsetDip = value;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 创建显示模式页的视图模型并订阅设置、显示器与长度约束三处变化。
    /// 三个订阅源都是单例，因此订阅与进程同寿命，不需要退订。
    /// Creates the display-mode view model and subscribes to settings, monitor, and length-constraint changes.
    /// All three sources are singletons, so the subscriptions live as long as the process and never need cancelling.
    /// </summary>
    /// <param name="displayMonitorService">显示器目录，供目标显示器下拉框使用。/ Display catalog behind the target-monitor drop-down.</param>
    /// <param name="taskbarLengthConstraints">任务栏可用长度约束，决定固定宽度的取值范围。/ Taskbar length constraints that bound the fixed width.</param>
    /// <param name="localization">
    /// 界面语言。本页有三处文案由代码拼出（页头状态芯片、固定宽度可用范围、显示器名称），语言变化时必须重新取值。
    /// 它是必填依赖：可省略的依赖会留下一条"忘记注入就静默不刷新"的路径，而这里没有合理的缺省语言。
    /// Interface language. Three strings on this page are composed in code (the header status chip, the fixed-width
    /// available range, and the monitor names) and have to be re-read when the language changes. It is a required dependency:
    /// an omittable one leaves a path where forgetting to inject it silently skips the refresh, and there is no sensible
    /// default language here.
    /// </param>
    public DisplayModesViewModel(
        IDisplayMonitorService displayMonitorService,
        TaskbarLengthConstraintsService taskbarLengthConstraints,
        LocalizationService localization)
    {
        _displayMonitorService = displayMonitorService;
        _taskbarLengthConstraints = taskbarLengthConstraints;
        _localization = localization;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _displayMonitorService.MonitorsChanged += OnMonitorsChanged;
        _taskbarLengthConstraints.Changed += OnTaskbarLengthConstraintsChanged;

        // 代码拼出来的文案不会随 XAML 的动态资源一起更新，因此在语言变化时重新通知一遍。
        // Text composed in code does not follow the XAML dynamic resources, so it is announced again when the language
        // changes.
        _localization.LanguageChanged += OnLanguageChanged;

        _displayMonitorService.Refresh();
        RefreshMonitorOptions();
        // 静置层组件列表是代码构建的（顺序与保留状态来自设置），构造函数里必须先填一次：本页其余属性都是直接读设置的
        // getter，只有它为空的唯一表现就是"打开设置页看到一份空列表"。
        // The rest-layer component list is built in code (its order and kept switches come from the settings), so it has to be filled once
        // here: every other property on this page is a getter that reads the settings directly, and leaving this one empty would show up
        // only as an empty list when the settings page is opened.
        RefreshRestComponentEntries();
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
        RefreshRestComponentEntries();
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
        SettingsManager.Current.Position = TaskbarBarPosition.Start;
        SettingsManager.Current.TaskbarBarManualPadding = 0;
        TaskbarCrossAxisOffsetDip = 0;
        SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
    }

    private void SelectMode(DisplayModeSelection mode)
    {
        if (_selectedMode == mode) return;
        _selectedMode = mode;
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(IsTaskbarMode));
        OnPropertyChanged(nameof(IsDynamicIslandMode));
        OnPropertyChanged(nameof(IsDesktopCardMode));
        OnPropertyChanged(nameof(IsFloatingBallMode));
        OnPropertyChanged(nameof(IsUnimplementedMode));
    }

    private void UpdateExperience(TaskbarExperienceSettings value)
    {
        // 页面上高亮一个未实现的承载模式时，任务栏专属设置不接受写入——这是既有且受测试保护的不变量。
        // 代价是那些控件会“看着能改、实际不保存”，因此页面在同一状态下会显示一条明确的只读提示，
        // 而不是让用户自己猜。提示由 DisplayModesPage 绑定 IsUnimplementedMode 呈现。
        // While an unimplemented hosting mode is highlighted, taskbar-only settings refuse writes: that is an
        // existing invariant guarded by a test. The cost is controls that look editable without saving, so the
        // page shows an explicit read-only notice in that state instead of leaving the user to guess.
        if (_isRefreshing || !IsTaskbarMode) return;
        SettingsManager.SetTaskbarExperienceSettings(value.Normalize());
        RaiseExperience();
    }

    private void UpdateHoverControls(TaskbarHoverControlsSettings controls) =>
        UpdateExperience(SettingsManager.Current.TaskbarExperience with { HoverControls = controls });

    private void UpdateNotification(TrackChangeNotificationSettings value)
    {
        if (_isRefreshing)
            return;

        SettingsManager.SetTrackChangeNotificationSettings(value.Normalize());
        RaiseNotification();
    }

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
        UpdateExperience(SettingsManager.Current.TaskbarExperience with { FullPanel = settings.Normalize() });

    private bool CanToggleFullPanelGroup(bool visible) => !visible || VisibleFullPanelGroupCount() > 1;

    private int VisibleFullPanelGroupCount()
    {
        var settings = FullPanelSettings;
        return (settings.MediaInfoVisible ? 1 : 0) +
               (settings.MediaControlsVisible ? 1 : 0) +
               (settings.AudioControlsVisible ? 1 : 0) +
               (settings.PerformanceVisible ? 1 : 0);
    }

    public void ResetDisplayModes() => SettingsManager.ResetDisplayModes();
    public void ResetExtraFeatures() => SettingsManager.ResetExtraFeatures();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.ResetScope is SettingsResetScope.DisplayModes or SettingsResetScope.Layout or SettingsResetScope.All)
            RaiseAll();
        else if (!_isRefreshing && e.PropertyName is nameof(AppSettings.TaskbarTargetMonitorDeviceIds) or nameof(AppSettings.TaskbarTargetMonitorDeviceId))
            RefreshMonitorOptions();
    }

    private void OnMonitorsChanged(object? sender, EventArgs e) => RefreshMonitorOptions();

    private void OnTaskbarLengthConstraintsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(FixedTaskbarLengthMinimum));
        OnPropertyChanged(nameof(FixedTaskbarLengthMaximum));
        OnPropertyChanged(nameof(FixedTaskbarLengthDip));
        OnPropertyChanged(nameof(FixedTaskbarLengthRangeText));
    }

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

        var options = availableOptions.ToList();

        var selectedTaskbarIds = ResolveConfiguredTaskbarSelection(monitors);
        var taskbarOptions = availableOptions
            .Select(option => new TaskbarMonitorSelectionItem(
                option.DeviceId,
                option.DisplayName,
                option.IsPrimary,
                option.IsAvailable,
                selectedTaskbarIds.Contains(option.DeviceId)))
            .ToList();

        static void AddDisconnected(
            List<DisplayMonitorOption> target,
            IReadOnlyList<DisplayMonitorOption> available,
            string? preferredId,
            string suffix,
            int index)
        {
            if (string.IsNullOrWhiteSpace(preferredId) ||
                available.Any(option => string.Equals(option.DeviceId, preferredId, StringComparison.OrdinalIgnoreCase)))
                return;

            target.Insert(index, new DisplayMonitorOption(
                preferredId,
                $"{preferredId}{suffix}",
                false,
                false));
        }

        AddDisconnected(options, availableOptions, NotificationSettings.FixedMonitorDeviceId, disconnectedSuffix, 0);
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

        foreach (var option in taskbarOptions)
            option.SelectionChanged += OnTaskbarMonitorSelectionChanged;

        _monitorOptions = options;
        _taskbarMonitorOptions = taskbarOptions;
        UpdateTaskbarMonitorToggleState();
        OnPropertyChanged(nameof(MonitorOptions));
        OnPropertyChanged(nameof(TaskbarMonitorOptions));
        OnPropertyChanged(nameof(TrackChangeNotificationFixedMonitorDeviceId));
    }

    private HashSet<string> ResolveConfiguredTaskbarSelection(IReadOnlyList<DisplayMonitorInfo> monitors)
    {
        var selected = (SettingsManager.Current.TaskbarTargetMonitorDeviceIds ?? [])
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count > 0)
            return selected;

        var legacy = SettingsManager.Current.TaskbarTargetMonitorDeviceId;
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
        if (_isRefreshing)
            return;

        var selected = _taskbarMonitorOptions.Where(option => option.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            changed.IsSelected = true;
            return;
        }

        var deviceIds = selected.Select(option => option.DeviceId).ToArray();
        var current = SettingsManager.Current.TaskbarTargetMonitorDeviceIds ?? [];
        _isRefreshing = true;
        try
        {
            if (!current.SequenceEqual(deviceIds, StringComparer.OrdinalIgnoreCase))
                SettingsManager.Current.TaskbarTargetMonitorDeviceIds = deviceIds;
            SettingsManager.Current.TaskbarTargetMonitorDeviceId = null;
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
            OnPropertyChanged(nameof(DynamicIslandBackgroundMode)); OnPropertyChanged(nameof(DynamicIslandEdge));
            OnPropertyChanged(nameof(IslandSurfaceStyle)); OnPropertyChanged(nameof(IslandSurfaceOpacityPercent));
            OnPropertyChanged(nameof(IslandSurfaceCornerRadiusDip));
            RaiseExperience(); OnPropertyChanged(nameof(Orientation)); OnPropertyChanged(nameof(IsTaskbarPositionLocked));
            OnPropertyChanged(nameof(IsTaskbarAvoidingIcons)); OnPropertyChanged(nameof(TaskbarCrossAxisOffsetDip));
            RefreshMonitorOptions();
            RaiseNotification();
        }
        finally { _isRefreshing = false; }
    }

    private void RaiseExperience()
    {
        OnPropertyChanged(nameof(HoverLayerEnabled)); OnPropertyChanged(nameof(FullLayerEnabled));
        OnPropertyChanged(nameof(Density)); OnPropertyChanged(nameof(ContentLayout));
        OnPropertyChanged(nameof(MediaTextAlignment));
        OnPropertyChanged(nameof(FullPanelEntryVisible));
        OnPropertyChanged(nameof(RestProgressVisible));
        OnPropertyChanged(nameof(SpectrumVisible)); OnPropertyChanged(nameof(PerformanceVisible));
        OnPropertyChanged(nameof(HoverPlayPauseVisible)); OnPropertyChanged(nameof(HoverPreviousNextVisible));
        OnPropertyChanged(nameof(HoverOutputDeviceVisible)); OnPropertyChanged(nameof(HoverAudioControlVisible));
        OnPropertyChanged(nameof(HoverProgressVisible));
        OnPropertyChanged(nameof(ComponentSpacingDip));
        OnPropertyChanged(nameof(FollowMediaTextLength)); OnPropertyChanged(nameof(UsesFixedTaskbarLength));
        OnPropertyChanged(nameof(FixedTaskbarLengthMinimum)); OnPropertyChanged(nameof(FixedTaskbarLengthMaximum));
        OnPropertyChanged(nameof(FixedTaskbarLengthDip)); OnPropertyChanged(nameof(FixedTaskbarLengthRangeText));
        OnPropertyChanged(nameof(RestOutputDeviceVisible)); OnPropertyChanged(nameof(RestVolumeVisible));
        // 顺序列表与"没有媒体时显示"列表是两份内容，重建它们同时刷新了两者（也在语言变化后重取显示名）。
        // The order list and the "shown without media" list are the two contents, and rebuilding them refreshes both (and re-reads the display
        // names after a language change).
        RefreshRestComponentEntries();
        RaiseFullPanel();
    }

    private void RaiseNotification()
    {
        OnPropertyChanged(nameof(TrackChangeNotificationEnabled));
        OnPropertyChanged(nameof(ShowTrackChangeNotificationWhenFullscreen));
        OnPropertyChanged(nameof(TrackChangeNotificationDurationSeconds));
        OnPropertyChanged(nameof(TrackChangeNotificationPosition));
        OnPropertyChanged(nameof(TrackChangeNotificationTargetMode));
        OnPropertyChanged(nameof(TrackChangeNotificationFixedMonitorDeviceId));
        OnPropertyChanged(nameof(CanSelectTrackChangeNotificationMonitor));
    }

    private TrackChangeNotificationSettings NotificationSettings =>
        SettingsManager.Current.TrackChangeNotification.Normalize();

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
}
