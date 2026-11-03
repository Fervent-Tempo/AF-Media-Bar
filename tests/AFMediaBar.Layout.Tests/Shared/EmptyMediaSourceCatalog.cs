// 无来源的设置页夹具，不启动 SMTC 或播放器。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Layout.Tests;

internal sealed class EmptyMediaSourceCatalog : IMediaSourceCatalog
{
    public IReadOnlyList<MediaSourceDescriptor> CurrentDiscoveredSources => [];
    public event Action<IReadOnlyList<MediaSourceDescriptor>>? DiscoveredSourcesChanged { add { } remove { } }
}
