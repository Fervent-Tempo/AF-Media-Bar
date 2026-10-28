// 首页只借用已有会话，不负责启动来源或执行媒体控制。
using AFMediaBar.Classes.Models;
namespace AFMediaBar.Classes.Abstractions;

/// <summary>已运行媒体服务的只读快照；允许离线验收替换环境。</summary>
public interface IHomeMediaState
{
    MediaSnapshot? Snapshot { get; }
    IReadOnlyList<MediaSessionOption> Sessions { get; }
    event EventHandler? Changed;
}
