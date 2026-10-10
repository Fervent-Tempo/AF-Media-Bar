// 普通重启的进程交接契约；确认界面由调用方显式请求。
namespace AFMediaBar.Classes.Abstractions;

/// <summary>普通重启请求结果。</summary>
public enum ApplicationRestartResult { Requested, Busy, Canceled, Failed }

/// <summary>准备新实例并请求应用执行正常退出。</summary>
public interface IApplicationRestartService
{
    event EventHandler? RestartRequested;
    Task<ApplicationRestartResult> RequestRestartAsync(CancellationToken cancellationToken = default);
}
