namespace AFMediaBar.Classes.Models;

/// <summary>可启动媒体应用的目标类型。 / Kind of target used to launch a media application.</summary>
public enum QuickLaunchTargetKind
{
    Executable = 0,
    Shortcut = 1,
    AppUserModelId = 2
}

/// <summary>由用户维护的快速启动条目。 / User-maintained quick-launch entry.</summary>
public sealed record QuickLaunchEntry(
    string Id,
    string DisplayName,
    QuickLaunchTargetKind Kind,
    string Target,
    string? SourceId = null);

/// <summary>运行期发现的 SMTC 应用来源。 / SMTC application source discovered at runtime.</summary>
public sealed record MediaSourceDescriptor(
    string SourceId,
    string DisplayName,
    QuickLaunchTargetKind? LaunchKind,
    string? LaunchTarget)
{
    public bool CanQuickLaunch => LaunchKind is not null && !string.IsNullOrWhiteSpace(LaunchTarget);
}

/// <summary>可显示的性能指标。 / Performance metric available to the compact taskbar component.</summary>
public enum MetricKind
{
    SystemMemory = 0,
    SystemCpu = 1,
    SystemGpu = 2,
    ProcessMemory = 3,
    SystemNetwork = 4
}

/// <summary>快速启动执行结果。 / Result of a quick-launch request.</summary>
public enum QuickLaunchResult
{
    Success = 0,
    InvalidTarget = 1,
    Failed = 2
}
