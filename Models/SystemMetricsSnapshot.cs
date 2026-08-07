namespace TaskbarPlayer.Models;

internal readonly record struct SystemMetricsSnapshot(
    int SystemMemoryPercent,
    int? SystemCpuPercent,
    long ProcessMemoryMegabytes);
