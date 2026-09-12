namespace AFMediaBar.Classes.Models;

/// <summary>系统和当前进程的性能快照。 / Performance snapshot for the system and current process.</summary>
public readonly record struct SystemMetricsSnapshot(
    int SystemMemoryPercent,
    int? SystemCpuPercent,
    int? SystemGpuPercent,
    long ProcessMemoryMegabytes);
