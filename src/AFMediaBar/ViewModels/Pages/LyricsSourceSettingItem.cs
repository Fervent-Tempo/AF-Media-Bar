using AFMediaBar.Classes.Services.Lyrics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>
/// 歌词页来源列表中的一项：一个来源 id、它的显示名与启用状态。
/// One entry in the lyrics page's source list: a source id, its display name, and whether it is enabled.
///
/// 显示名按当前语言解析后写进来，因此语言变化时由页面视图模型重建整个列表，而不是在这里监听语言事件。
/// The display name is resolved in the active language and written in, so a language change rebuilds the whole list inside the page
/// view model instead of this item listening for language events.
/// </summary>
public partial class LyricsSourceSettingItem : ObservableObject
{
    /// <summary>来源 id（设置文件里的取值）。/ The source id, which is what the settings file stores.</summary>
    public string SourceId { get; }

    /// <summary>当前语言下的来源显示名。/ The source's display name in the active language.</summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>该来源是否参与取词。/ Whether this source takes part in retrieval.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _canMoveUp;

    [ObservableProperty]
    private bool _canMoveDown;

    /// <summary>启用状态变化时通知页面视图模型重新写入设置。/ Notifies the page view model to write the settings again.</summary>
    public event Action<LyricsSourceSettingItem>? EnabledChanged;

    /// <summary>
    /// 创建一个来源列表项。
    /// Creates one source list entry.
    /// </summary>
    /// <param name="sourceId">来源 id / Source id.</param>
    /// <param name="isEnabled">是否启用 / Whether it is enabled.</param>
    public LyricsSourceSettingItem(string sourceId, bool isEnabled)
    {
        SourceId = sourceId;
        _displayName = LyricsSourceCatalog.GetDisplayName(sourceId);
        _isEnabled = isEnabled;
    }

    partial void OnIsEnabledChanged(bool value) => EnabledChanged?.Invoke(this);
}
