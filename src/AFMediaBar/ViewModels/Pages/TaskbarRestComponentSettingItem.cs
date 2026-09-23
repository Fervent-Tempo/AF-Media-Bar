using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>
/// 显示模式页静置层两张列表共用的一项：一个静置层组件、它在界面上的显示名与说明、能否排序，以及"没有媒体时是否显示"。
///
/// 顺序列表与"没有媒体时显示"列表刻意是两份：一张列表同时管两件事时，用户想调顺序就会看到一排显隐开关，
/// 而想调显隐又会看到上下移按钮。两个列表的条目集合不同——固定在最前面的封面与媒体文字只出现在顺序列表里（且不能移动），
/// "没有媒体时显示"列表里则是快速启动小音符加其余四个小组件。
/// One row shared by the Display-modes page's two rest-layer lists: a rest-layer component, the name and description shown for it, whether it can be
/// reordered, and whether it is shown while there is no media.
///
/// The order list and the "shown without media" list are separate on purpose: a single list carrying both makes the user see a row of visibility
/// switches while arranging, and move buttons while switching visibility. The two lists hold different sets — the pinned artwork and media text appear
/// only in the order list (and cannot move), while the "shown without media" list holds the quick-launch note plus the other four widgets.
///
/// 显示名与说明按当前语言解析后写进来，因此语言变化时由页面视图模型重建列表，而不是在这里监听语言事件（与歌词来源列表同一处理）。
/// The display name and description are resolved in the active language and written in, so a language change rebuilds the lists inside the page view
/// model instead of this item listening for language events, the same handling as the lyric-source list.
/// </summary>
public partial class TaskbarRestComponentSettingItem : ObservableObject
{
    /// <summary>这一项对应的静置层组件。/ The rest-layer component this row stands for.</summary>
    public TaskbarRestComponent Component { get; }

    /// <summary>这一项能不能被上移/下移；封面与媒体文字固定在媒体栏最前面，因此为假。/ Whether this row can move up or down; the artwork and the media text are pinned to the front of the bar, so it is false for them.</summary>
    public bool CanMove { get; }

    /// <summary>当前语言下的组件名。/ The component's name in the active language.</summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>当前语言下的一句话说明。/ The one-line description in the active language.</summary>
    [ObservableProperty]
    private string _description;

    /// <summary>没有媒体（没有 SMTC 来源）时是否显示这个组件。/ Whether this component is shown while there is no media (no SMTC source).</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>显隐变化时通知页面视图模型重新写入设置。/ Notifies the page view model to write the settings again.</summary>
    public event Action<TaskbarRestComponentSettingItem>? VisibilityChanged;

    /// <summary>
    /// 创建一个静置层组件列表项。
    /// Creates one rest-layer component entry.
    /// </summary>
    /// <param name="component">组件。/ The component.</param>
    /// <param name="canMove">能否排序。/ Whether it can be reordered.</param>
    /// <param name="isVisible">没有媒体时是否显示。/ Whether it is shown without media.</param>
    /// <param name="useQuickLaunchName">
    /// 是否用"快速启动小音符"这个名字。无媒体时封面框里画的是音符而不是曲目封面，两个列表因此对同一个组件用不同的名字。
    /// Whether to use the "quick-launch note" name. Without media that box draws the note rather than a track's cover, so the two lists name the
    /// same component differently.
    /// </param>
    public TaskbarRestComponentSettingItem(
        TaskbarRestComponent component,
        bool canMove,
        bool isVisible,
        bool useQuickLaunchName = false)
    {
        Component = component;
        CanMove = canMove;
        _displayName = Translations.Get(useQuickLaunchName ? QuickLaunchNoteNameKey : ResolveNameKey(component));
        _description = Translations.Get(useQuickLaunchName ? QuickLaunchNoteDescriptionKey : ResolveDescriptionKey(component));
        _isVisible = isVisible;
    }

    partial void OnIsVisibleChanged(bool value) => VisibilityChanged?.Invoke(this);

    /// <summary>"没有媒体时显示"列表里小音符那一项的文案键。/ The text key of the note row in the "shown without media" list.</summary>
    public const string QuickLaunchNoteNameKey = "DisplayModes.Rest.Component.QuickLaunchNote";

    /// <summary>"没有媒体时显示"列表里小音符那一项的说明文案键。/ The description key of the note row in the "shown without media" list.</summary>
    public const string QuickLaunchNoteDescriptionKey = "DisplayModes.Rest.Component.QuickLaunchNote.Description";

    /// <summary>组件名对应的文案键。/ The text key of a component's name.</summary>
    /// <param name="component">组件。/ The component.</param>
    public static string ResolveNameKey(TaskbarRestComponent component) => component switch
    {
        TaskbarRestComponent.MediaText => "DisplayModes.Rest.Component.MediaText",
        TaskbarRestComponent.Spectrum => "DisplayModes.Rest.Component.Spectrum",
        TaskbarRestComponent.Performance => "DisplayModes.Rest.Component.Performance",
        TaskbarRestComponent.OutputDevice => "DisplayModes.Rest.Component.OutputDevice",
        TaskbarRestComponent.Volume => "DisplayModes.Rest.Component.Volume",
        _ => "DisplayModes.Rest.Component.Artwork"
    };

    /// <summary>组件说明对应的文案键。/ The text key of a component's description.</summary>
    /// <param name="component">组件。/ The component.</param>
    public static string ResolveDescriptionKey(TaskbarRestComponent component) => component switch
    {
        TaskbarRestComponent.MediaText => "DisplayModes.Rest.Component.MediaText.Description",
        TaskbarRestComponent.Spectrum => "DisplayModes.Rest.Component.Spectrum.Description",
        TaskbarRestComponent.Performance => "DisplayModes.Rest.Component.Performance.Description",
        TaskbarRestComponent.OutputDevice => "DisplayModes.Rest.Component.OutputDevice.Description",
        TaskbarRestComponent.Volume => "DisplayModes.Rest.Component.Volume.Description",
        _ => "DisplayModes.Rest.Component.Artwork.Description"
    };
}
