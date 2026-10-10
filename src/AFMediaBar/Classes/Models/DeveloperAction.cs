// 开发者动作与宿主快照契约；不持有窗口、渲染器或异常对象。
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Models;

/// <summary>手动测试动作对当前进程的影响。</summary>
public enum DeveloperActionImpact { Preview, Session, Diagnostic }

/// <summary>请求反馈；Accepted 和 Queued 不代表可见结果已经完成。</summary>
public enum DeveloperActionStatus { Completed, Accepted, Queued, Unavailable, Busy, Canceled, Failed }

/// <summary>共用于按钮、命令建议及帮助的动作说明。</summary>
public sealed record DeveloperAction(string Command, string ResourceId, DeveloperActionImpact Impact, bool RequiresRenderer = false)
{
    public string Title => Translations.Get("Developer.Action." + ResourceId);
    public string Description => Translations.Get("Developer.Description." + ResourceId);
    public string ImpactText => Translations.Get("Developer.Impact." + Impact);
}

/// <summary>一次动作的实际结果及可选诊断快照。</summary>
public sealed record DeveloperActionResult(DeveloperActionStatus Status, string? Detail = null)
{
    public string StatusText => Translations.Get("Developer.Result." + Status);
}

/// <summary>单个歌词宿主的只读诊断；HostId 随窗口重建改变。</summary>
public sealed record DeveloperLyricsHostState(string HostId, string Monitor, bool Loaded, bool HasRenderer,
    bool Ready, bool Disabled, int Generation, int Recoveries, int Failures, bool RetryPending, string RuntimeVersion)
{
    public string DisplayName => Monitor;
}

/// <summary>预览与会话操作的确认结果。</summary>
public enum DeveloperConfirmationResult { Confirmed, Declined, Busy }
