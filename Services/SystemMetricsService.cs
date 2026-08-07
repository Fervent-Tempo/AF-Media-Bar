using System.Diagnostics;
using TaskbarPlayer.Interop;
using TaskbarPlayer.Models;

namespace TaskbarPlayer.Services;

internal sealed class SystemMetricsService : IDisposable
{
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasPreviousCpuSample;

    internal SystemMetricsSnapshot Sample()
    {
        var systemMemoryPercent = ReadSystemMemoryPercent();
        var systemCpuPercent = ReadSystemCpuPercent();

        _currentProcess.Refresh();
        var processMemoryMegabytes = (long)Math.Round(_currentProcess.WorkingSet64 / 1024d / 1024d);

        return new SystemMetricsSnapshot(
            systemMemoryPercent,
            systemCpuPercent,
            processMemoryMegabytes);
    }

    private static int ReadSystemMemoryPercent()
    {
        var status = NativeMethods.MemoryStatusEx.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
        {
            return 0;
        }

        var used = status.TotalPhysical - status.AvailablePhysical;
        return (int)Math.Clamp(Math.Round(used * 100d / status.TotalPhysical), 0, 100);
    }

    private int? ReadSystemCpuPercent()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        var currentIdle = idle.ToUInt64();
        var currentKernel = kernel.ToUInt64();
        var currentUser = user.ToUInt64();

        if (!_hasPreviousCpuSample)
        {
            _previousIdle = currentIdle;
            _previousKernel = currentKernel;
            _previousUser = currentUser;
            _hasPreviousCpuSample = true;
            return null;
        }

        var idleDelta = currentIdle - _previousIdle;
        var kernelDelta = currentKernel - _previousKernel;
        var userDelta = currentUser - _previousUser;
        var totalDelta = kernelDelta + userDelta;

        _previousIdle = currentIdle;
        _previousKernel = currentKernel;
        _previousUser = currentUser;

        if (totalDelta == 0)
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round((totalDelta - idleDelta) * 100d / totalDelta), 0, 100);
    }

    public void Dispose()
    {
        _currentProcess.Dispose();
    }
}
