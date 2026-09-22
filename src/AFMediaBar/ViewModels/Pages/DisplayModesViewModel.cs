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

    /// <summary>
    /// 页面是否高亮了一个尚未实现的承载模式（桌面卡片或悬浮球）。任务栏与灵动岛都是实现了的，
    /// 因此它们不再算"未实现"：页面据此显示只读提示。
    /// Whether the page highlights a hosting mode that does not exist yet (the desktop card or the floating ball).
    /// The taskbar and the dynamic island are both implemented, so neither counts as unimplemented any more, and the
    /// page uses this to show its read-only notice.
    /// </summary>
    public bool IsUnimplementedMode => IsDesktopCardMode || IsFloatingBallMode;

    /// <summary>
    /// 当前显示模式的文本，供页头状态芯片显示。它读的是真实 <see cref="WindowMode"/>，
    /// 因此芯片始终说的是"正在运行的那一个"，而不是页面里刚点中的卡片。
    /// 模式名取自与模式卡片相同的键，因此芯片与卡片任何时候都写作同一个词。
    /// Text for the header status chip. It reads the real <see cref="WindowMode"/>, so the chip always names the mode
    /// that is actually running rather than the card just clicked. The mode name comes from the same keys the mode cards
    /// use, so the chip and the cards always read as one word.
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

    /// <summary>
    /// 灵动岛是否就是当前运行模式，与任务栏侧的同名属性对称；灵动岛卡片用它决定"当前模式"芯片是否显示。
    /// 它读的同样是真实 <see cref="WindowMode"/>，因此卡片上的芯片不会因为刚点一下就提前亮起。
    /// Whether the dynamic island really is the running mode, the mirror of the taskbar-side property, and the island
    /// card uses it to decide whether its "current mode" chip shows. It reads the real <see cref="WindowMode"/> too, so
    /// the chip never lights up merely because the card was just clicked.
    /// </summary>
    public bool IsDynamicIslandHostingActive => CurrentWindowMode == WindowMode.DynamicIsland;

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
        // 页面高亮必须从真实运行模式起步：配置文件里存着灵动岛时，页面若默认高亮任务栏，用户一打开这一页
        // 就会看到"运行的是灵动岛、高亮的是任务栏"，还会以为点一下任务栏卡片是"切回去"。
        // The page highlight has to start from the real running mode: with the dynamic island stored in the settings
        // file, a page that defaults to the taskbar highlight would open showing "the island is running, the taskbar is
        // highlighted" and a click on the taskbar card would read as switching back rather than as a no-op.
        _selectedMode = ResolveSelection(SettingsManager.Current.WindowMode);
        // 静置层组件列表是代码构建的（顺序与保留状态来自设置），构造函数里必须先填一次：本页其余属性都是直接读设置的
        // getter，只有它为空的唯一表现就是"打开设置页看到一份空列表"。
        // The rest-layer component list is built in code (its order and kept switches come from the settings), so it has to be filled once
        // here: every other property on this page is a getter that reads the settings directly, and leaving this one empty would show up
        // only as an empty list when the settings page is opened.
        RefreshRestComponentEntries();
    }

    /// <summary>
    /// 真实运行模式对应的页面高亮。只有两个已实现的模式参与映射：任务栏与灵动岛。
    /// 未实现的两种模式永远不会成为运行模式，因此它们的卡片只能靠点击进入。
    /// The page highlight matching a real running mode. Only the two implemented modes take part in the mapping — the
    /// taskbar and the dynamic island. Neither unimplemented mode can ever be the running one, so their cards are only
    /// ever reached by clicking them.
    /// </summary>
    private static DisplayModeSelection ResolveSelection(WindowMode windowMode) => windowMode switch
    {
        WindowMode.DynamicIsland => DisplayModeSelection.DynamicIsland,
        _ => DisplayModeSelection.Taskbar
    };

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
        // 页头芯片与卡片芯片是代码拼出来的模式名，同样不会随空属性名的通知自己更新，因此一并重新通知。
        // The header chip and the cards' chips are mode names composed in code, so they do not follow the empty-property
        // notification either and are re-announced here.
        RaiseSelection();
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

    /// <summary>
    /// 选中一个显示模式。任务栏与灵动岛是已实现的，选中它们会立刻切换正在运行的窗口并广播布局变化；
    /// 桌面卡片与悬浮球尚未实现，只切换页面高亮（它们确实没有窗口可切）。
    ///
    /// 目标模式与当前运行模式相同时不重复广播：布局变化的订阅方会重建承载窗口，重复广播等于白发一次重建。
    /// Selects a display mode. The taskbar and the dynamic island are implemented, so selecting either switches the
    /// running window straight away and publishes the layout change; the desktop card and the floating ball are not, so
    /// they only move the page highlight (they really have no window to switch to).
    ///
    /// A target that already is the running mode is not published again: the layout-change subscribers rebuild the
    /// hosting window, and re-publishing would rebuild it for nothing.
    /// </summary>
    private void SelectMode(DisplayModeSelection mode)
    {
        if (_selectedMode == mode) return;
        _selectedMode = mode;

        var windowMode = mode switch
        {
            DisplayModeSelection.DynamicIsland => WindowMode.DynamicIsland,
            DisplayModeSelection.Taskbar => WindowMode.Taskbar,
            _ => (WindowMode?)null
        };

        if (windowMode is { } target && SettingsManager.Current.WindowMode != target)
        {
            SettingsManager.Current.WindowMode = target;
            // 广播的是刚写下的模式本身：MainWindow 按事件参数决定切换哪一个窗口，
            // 若这里去读设置，任何一处顺序调整都会让广播与实际写入的模式脱节。
            // What is published is the mode just written: MainWindow picks the window from the event argument, so
            // reading the settings here would let any reordering publish a mode that was never stored.
            SettingsManager.RaiseLayoutSettingsChanged(target, Orientation);
        }

        RaiseSelection();
    }

    /// <summary>
    /// 通知四处模式高亮：页内当前选择、四个卡片各自的选中态、未实现提示，以及两个"当前模式"芯片。
    /// 芯片读的是真实运行模式，因此切换运行模式之后必须与高亮一起重新通知。
    /// Announces the four mode highlights: the in-page selection, each card's checked state, the unimplemented notice,
    /// and both "current mode" chips. The chips read the real running mode, so they are re-announced together with the
    /// highlight whenever the running mode changes.
    /// </summary>
    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(IsTaskbarMode));
        OnPropertyChanged(nameof(IsDynamicIslandMode));
        OnPropertyChanged(nameof(IsDesktopCardMode));
        OnPropertyChanged(nameof(IsFloatingBallMode));
        OnPropertyChanged(nameof(IsUnimplementedMode));
        OnPropertyChanged(nameof(IsTaskbarHostingActive));
        OnPropertyChanged(nameof(IsDynamicIslandHostingActive));
        OnPropertyChanged(nameof(HostingModeText));
    }

    /// <summary>
    /// 写回任务栏体验设置。页面高亮一个不是任务栏的模式时，任务栏专属的分组整块只在那张分区里出现、
    /// 这些控件根本不显示，因此这道守卫拦下的只是"看着能改、实际不保存"的写入（页面在同一状态下会显示只读提示）。
    /// 灵动岛的外观不走这里：它写在 <c>DynamicIslandSurface</c> 上，由 <see cref="PublishIslandSurface"/> 直接发布，
    /// 因此这道守卫不会挡住灵动岛分区的可保存设置。
    /// Writes the taskbar experience settings back. While a mode other than the taskbar is highlighted, the
    /// taskbar-only groups live in that other section and are not on screen at all, so this guard only refuses writes that
    /// would look editable without saving — and the page shows a read-only notice in that state. The island's appearance
    /// does not come through here: it lives on <c>DynamicIslandSurface</c> and is published straight by
    /// <see cref="PublishIslandSurface"/>, so this guard never blocks the island section's writable settings.
    /// </summary>
    private void UpdateExperience(TaskbarExperienceSettings value)
    {
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
        else if (e.PropertyName == nameof(AppSettings.WindowMode) && !_isRefreshing)
            SyncSelectionToRuntimeMode();
        else if (!_isRefreshing && e.PropertyName is nameof(AppSettings.TaskbarTargetMonitorDeviceIds) or nameof(AppSettings.TaskbarTargetMonitorDeviceId))
            RefreshMonitorOptions();
    }

    /// <summary>
    /// 运行模式在别处被改动（另一个页面、托盘菜单或重置）之后，把页面高亮跟过去。
    /// 不跟随就会留下"设置里是灵动岛、页面还高亮任务栏"这种自相矛盾的状态，而页头芯片与卡片芯片
    /// 读的都是真实模式，三者必须同时移动。
    /// Follows the running mode after it is changed elsewhere (another page, the tray menu, or a reset). Without this,
    /// the page would keep highlighting the taskbar while the settings hold the dynamic island — a self-contradictory
    /// state, since the header chip and the cards' chips read the real mode and all three have to move together.
    /// </summary>
    private void SyncSelectionToRuntimeMode()
    {
        if (_selectedMode == ResolveSelection(SettingsManager.Current.WindowMode)) return;
        _selectedMode = ResolveSelection(SettingsManager.Current.WindowMode);
        RaiseSelection();
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
            OnPropertyChanged(nameof(CurrentWindowMode)); RaiseSelection();
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
