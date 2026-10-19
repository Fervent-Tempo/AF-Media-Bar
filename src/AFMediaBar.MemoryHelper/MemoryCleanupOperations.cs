// 隔离执行端独占原生清理实现；每次权限与句柄仅在固定操作期间有效。
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using AFMediaBar.Classes.Settings;
namespace AFMediaBar.Classes.Services;

/// <summary>同步原生操作，只在隔离辅助进程的后台线程调用。</summary>
public static class MemoryCleanupOperations
{
    public static MemoryCleanupReply Execute(SystemMemoryOptimizationLevel level, string requestId)
    {
        var before = SystemMemoryCountersReader.Capture();
        var watch = Stopwatch.StartNew();
        var stages = new List<MemoryCleanupStage>();
        if (level == SystemMemoryOptimizationLevel.Deep)
            stages.Add(WithPrivilege("FileCache", "SeIncreaseQuotaPrivilege", () =>
                SetSystemFileCacheSize(nuint.MaxValue, nuint.MaxValue, 0)
                    ? new("FileCache", SystemMemoryCleanupStatus.Completed) : FromWin32("FileCache", Marshal.GetLastWin32Error())));
        var standby = level == SystemMemoryOptimizationLevel.Deep ? "Standby" : "LowStandby";
        stages.Add(WithPrivilege(standby, "SeProfileSingleProcessPrivilege", () =>
        {
            var command = level == SystemMemoryOptimizationLevel.Deep ? 4 : 5;
            return FromNt(standby, NtSetSystemInformation(80, ref command, sizeof(int)));
        }));
        var successes = stages.Count(stage => stage.Status == SystemMemoryCleanupStatus.Completed);
        var status = successes == stages.Count ? SystemMemoryCleanupStatus.Completed
            : successes > 0 ? SystemMemoryCleanupStatus.PartialSuccess : stages[0].Status;
        return new(MemoryCleanupReply.ProtocolVersion, requestId, level, status, stages.ToArray(), watch.ElapsedMilliseconds, before, SystemMemoryCountersReader.Capture());
    }

    private static MemoryCleanupStage WithPrivilege(string operationName, string name, Func<MemoryCleanupStage> operation)
    {
        try
        {
            if (!OpenProcessToken(new IntPtr(-1), 0x28, out var token)) return FromWin32(operationName, Marshal.GetLastWin32Error());
            using (token)
            {
                if (!LookupPrivilegeValue(null, name, out var luid)) return FromWin32(operationName, Marshal.GetLastWin32Error());
                var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                if (!AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() == 1300)
                    return FromWin32(operationName, Marshal.GetLastWin32Error());
                return operation();
            }
        }
        catch (EntryPointNotFoundException error) { return new(operationName, SystemMemoryCleanupStatus.Unsupported, $"HRESULT 0x{error.HResult:X8}"); }
        catch (Exception error) { return new(operationName, SystemMemoryCleanupStatus.Failed, $"HRESULT 0x{error.HResult:X8}"); }
    }

    private static MemoryCleanupStage FromWin32(string operation, int code) => new(operation, code switch
    {
        5 or 1300 or 1314 => SystemMemoryCleanupStatus.PermissionDenied,
        50 or 120 or 127 => SystemMemoryCleanupStatus.Unsupported,
        _ => SystemMemoryCleanupStatus.Failed
    }, $"Win32 {code}");
    private static MemoryCleanupStage FromNt(string operation, int status) => new(operation, status >= 0 ? SystemMemoryCleanupStatus.Completed : unchecked((uint)status) switch
    {
        0xC0000061 or 0xC0000022 => SystemMemoryCleanupStatus.PermissionDenied,
        0xC0000003 or 0xC0000002 or 0xC00000BB => SystemMemoryCleanupStatus.Unsupported,
        _ => SystemMemoryCleanupStatus.Failed
    }, status >= 0 ? null : $"NTSTATUS 0x{status:X8}");
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disable, ref TokenPrivileges state, uint length, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemFileCacheSize(nuint minimum, nuint maximum, uint flags);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int informationClass, ref int command, int length);
}
