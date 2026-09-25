using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>
/// 歌词页默认接口绑定列表中的一项：一个播放器（AppID，创建后不可改）与它的备注名、绑定的默认来源。
/// One entry in the lyrics page's default-binding list: a player (AppID, immutable once created) with its remark name and
/// the default source bound to it.
///
/// 内置绑定只是配置文件尚未生成时的预填：列表一旦写回设置，所有行一律按用户条目对待，没有内置/用户两条分支。
/// <c>SelectedSourceId</c> 与备注名都用空字符串表示"未设置"，由视图模型在写回时转换成设置文件的 null。
/// A built-in binding is only the prefill for a settings file that does not exist yet: once the list has been written back,
/// every row is treated as a plain user entry with no built-in/user branching. An empty <c>SelectedSourceId</c> or remark
/// means "unset", which the view model converts into the settings file's null on write-back.
/// </summary>
public partial class LyricsDefaultBindingItem : ObservableObject
{
    /// <summary>播放器标识（设置文件里存的取值，创建后不可改）：预填的是内置规范片段，扫描/手动的是 AUMID 原文或用户输入。
    /// The player identifier (what the settings file stores, immutable once created): a built-in prefill's canonical
    /// fragment, a scanned or manual player's raw AUMID, or the user's input.</summary>
    public string AppId { get; }

    /// <summary>界面上显示的备注名，可编辑；初始为内置播放器名或 AppID 原文。
    /// The remark shown on the interface, editable; it starts as the built-in player's name or the raw AppID.</summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>当前绑定的来源 id；空字符串表示不绑定。/ The currently bound source id; an empty string means not bound.</summary>
    [ObservableProperty]
    private string _selectedSourceId;

    /// <summary>备注或绑定变化时通知页面视图模型重新写入设置。/ Notifies the page view model to write the settings again.</summary>
    public event Action<LyricsDefaultBindingItem>? BindingChanged;

    public LyricsDefaultBindingItem(string appId, string displayName, string? selectedSourceId)
    {
        AppId = appId;
        _displayName = displayName;
        _selectedSourceId = selectedSourceId ?? string.Empty;
    }

    partial void OnSelectedSourceIdChanged(string value) => BindingChanged?.Invoke(this);

    partial void OnDisplayNameChanged(string value) => BindingChanged?.Invoke(this);
}
