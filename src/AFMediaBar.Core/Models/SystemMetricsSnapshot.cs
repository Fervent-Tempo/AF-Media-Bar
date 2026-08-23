namespace AFMediaBar.Models;

public readonly record struct SystemMetricsSnapshot(
    int SystemMemoryPercent,
    int? SystemCpuPercent,
    int? SystemGpuPercent,
    long ProcessMemoryMegabytes);
