// 设置页共享只读来源目录；会话服务拥有发现与刷新，页面只订阅快照变化。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>当前发现的媒体来源及其可启动信息。</summary>
public interface IMediaSourceCatalog
{
    IReadOnlyList<MediaSourceDescriptor> CurrentDiscoveredSources { get; }
    event Action<IReadOnlyList<MediaSourceDescriptor>>? DiscoveredSourcesChanged;
}
