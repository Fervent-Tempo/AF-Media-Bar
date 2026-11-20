// 歌词页代理列表的一行，持有模式和最近一次连通性测试结果。
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Lyrics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>
/// 一行只绑一个来源组：代理模式下拉写回时通知页面视图模型保存，
/// 测试结果写到右侧状态文本，行本身不接触设置对象或网络。
/// One row binds one source group: the proxy-mode combo notifies the page view model to persist, the test verdict lands in
/// the right-side status text, and the row never touches the settings object or the network.
/// </summary>
public partial class LyricsSourceProxyItem : ObservableObject
{
    /// <summary>来源组 id，同时是设置里代理模式表的键。/ The source-group id, also the key of the proxy mode table.</summary>
    public string SourceId { get; }

    /// <summary>当前语言下的来源显示名。/ The source's display name in the active language.</summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>该来源的代理模式。/ The source's proxy mode.</summary>
    [ObservableProperty]
    private LyricsSourceProxyMode _proxyMode;

    /// <summary>最近一次连通性测试的详情；空表示还没测过，作为按钮的悬停提示。</summary>
    [ObservableProperty]
    private string _testStatus = string.Empty;

    /// <summary>测试进行中，按钮不可再点。</summary>
    [ObservableProperty]
    private bool _isTesting;

    /// <summary>代理模式变化时通知页面视图模型写回设置。/ Notifies the page view model to persist a proxy-mode change.</summary>
    public event Action<LyricsSourceProxyItem>? ModeChanged;

    public LyricsSourceProxyItem(string sourceId, LyricsSourceProxyMode mode)
    {
        SourceId = sourceId;
        _displayName = LyricsSourceCatalog.GetDisplayName(sourceId);
        _proxyMode = mode;
    }

    partial void OnProxyModeChanged(LyricsSourceProxyMode value) => ModeChanged?.Invoke(this);
}
