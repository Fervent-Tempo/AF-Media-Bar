using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>
/// 歌词页默认接口绑定列表中的一项：一个播放器（内置的、扫描到的或手动添加的）与它当前绑定的默认来源。
/// One entry in the lyrics page's default-binding list: a player (built-in, scanned, or manually added) and the default
/// source currently bound to it.
///
/// <c>SelectedSourceId</c> 用空字符串表示"不绑定"：ComboBox 的选项里没有真正的 null Tag，空串在视图模型层转换成
/// 设置文件里的移除标记。修改后由页面视图模型整体重写绑定表，与来源列表同一套写法。
/// An empty <c>SelectedSourceId</c> means "not bound": the ComboBox options have no true null Tag, and the view model
/// converts the empty string into the settings file's removal marker. After a change the page view model rewrites the
/// whole table, the same arrangement as the source list.
/// </summary>
public partial class LyricsDefaultBindingItem : ObservableObject
{
    /// <summary>播放器标识（设置文件里存的取值）：内置播放器是规范片段，扫描/手动的是 AUMID 原文或用户输入。
    /// The player identifier (what the settings file stores): a built-in player's canonical fragment, a scanned or manual
    /// player's raw AUMID, or the user's input.</summary>
    public string AppId { get; }

    /// <summary>是否是内置绑定的行：内置行没有删除按钮，删除动作由"不绑定"选项承担。
    /// Whether this row is a built-in binding: built-in rows carry no delete button, deletion is the "not bound" option.</summary>
    public bool IsBuiltIn { get; }

    /// <summary>该行是否可删除（非内置行才有删除按钮）。/ Whether the row can be deleted (only non-built-in rows carry a delete button).</summary>
    public bool IsDeletable => !IsBuiltIn;

    /// <summary>内置播放器的默认来源；非内置行为 null。/ The built-in player's default source, null on non-built-in rows.</summary>
    public string? BuiltInSourceId { get; }

    /// <summary>当前语言下的播放器显示名。/ The player's display name in the active language.</summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>当前绑定的来源 id；空字符串表示不绑定。/ The currently bound source id; an empty string means not bound.</summary>
    [ObservableProperty]
    private string _selectedSourceId;

    /// <summary>绑定变化时通知页面视图模型重新写入设置。/ Notifies the page view model to write the settings again.</summary>
    public event Action<LyricsDefaultBindingItem>? BindingChanged;

    public LyricsDefaultBindingItem(
        string appId,
        string displayName,
        bool isBuiltIn,
        string? builtInSourceId,
        string? selectedSourceId)
    {
        AppId = appId;
        _displayName = displayName;
        _selectedSourceId = selectedSourceId ?? string.Empty;
        IsBuiltIn = isBuiltIn;
        BuiltInSourceId = builtInSourceId;
    }

    partial void OnSelectedSourceIdChanged(string value) => BindingChanged?.Invoke(this);
}
