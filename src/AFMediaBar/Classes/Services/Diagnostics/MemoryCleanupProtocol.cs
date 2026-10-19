// 主程序与隔离执行端共用固定协议和清理操作；不读取用户配置，也不接受文件或进程目标。
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>执行结果；超时不代表原生操作已经撤回。</summary>
public enum SystemMemoryCleanupStatus
{
    Completed = 0, PermissionDenied = 10, Unsupported = 11, Failed = 12,
    TimedOut = 13, InvalidRequest = 14, Cancelled = 15, Busy = 16, PartialSuccess = 17,
    AuthorizationRequired = 18
}

/// <summary>单个系统操作的实际返回值。</summary>
public sealed record MemoryCleanupStage(string Operation, SystemMemoryCleanupStatus Status, string? ErrorCode = null);

/// <summary>仅包含固定操作结果的进程通信数据。</summary>
public sealed record MemoryCleanupReply(int Version, string RequestId, SystemMemoryOptimizationLevel Level,
    SystemMemoryCleanupStatus Status, MemoryCleanupStage[] Stages, long ElapsedMilliseconds, SystemMemoryCounters? Before = null, SystemMemoryCounters? After = null)
{
    public const int ProtocolVersion = 2;
}
