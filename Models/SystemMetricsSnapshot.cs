namespace AFMediaBar.Models;

internal readonly record struct SystemMetricsSnapshot(
    int SystemMemoryPercent,
    int? SystemCpuPercent,
    int? SystemGpuPercent,
    long ProcessMemoryMegabytes);
