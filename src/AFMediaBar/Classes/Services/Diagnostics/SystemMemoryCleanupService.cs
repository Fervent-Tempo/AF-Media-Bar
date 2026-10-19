// 仅提供一次性的低优先级待机页清理；提权子进程不接受目标路径或任意命令。
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AFMediaBar.Classes.Services;

/// <summary>固定操作的退出协议；未知退出码按失败处理。</summary>
public enum SystemMemoryCleanupStatus
{
    Completed = 0, PermissionDenied = 10, Unsupported = 11, Failed = 12,
    TimedOut = 13, InvalidRequest = 14, Cancelled = 15, Busy = 16
}

/// <summary>系统可用内存前后读数，不代表本次操作释放的精确字节数。</summary>
public sealed record SystemMemoryCleanupResult(SystemMemoryCleanupStatus Status, ulong? Before, ulong? After);

/// <summary>启动当前程序的受限提权模式，拥有等待期间的进程句柄和取消源。</summary>
public sealed class SystemMemoryCleanupService : IDisposable
{
    public const string Argument = "--internal-memory-clean-low-standby";
    private readonly CancellationTokenSource _stopping = new();
    private int _running;
    private bool _disposed;

    public static bool IsCleanupRequest(string[] args) => args.Any(arg => arg.StartsWith("--internal-memory-clean", StringComparison.Ordinal));

    /// <summary>只在隔离的辅助启动分支调用；看门狗保证不留下长驻提权进程。</summary>
    public static async Task<int> ExecuteHelperAsync(string[] args)
    {
        if (args.Length != 1 || args[0] != Argument) return (int)SystemMemoryCleanupStatus.InvalidRequest;
        using var watchdog = new System.Threading.Timer(_ => Environment.Exit((int)SystemMemoryCleanupStatus.TimedOut),
            null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        return (int)await Task.Run(CleanLowPriorityStandby);
    }

    public async Task<SystemMemoryCleanupResult> RunAsync()
    {
        if (_disposed) return new(SystemMemoryCleanupStatus.Cancelled, null, null);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return new(SystemMemoryCleanupStatus.Busy, null, null);
        ulong? before = null;
        try
        {
            before = await Task.Run(MemoryToolsService.CaptureAvailableBytes);
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable) || !Path.GetFileName(executable).Equals("AFMediaBar.exe", StringComparison.OrdinalIgnoreCase))
                return new(SystemMemoryCleanupStatus.Unsupported, before, null);
            using var process = await Task.Run(() => Process.Start(new ProcessStartInfo(executable)
            {
                Arguments = Argument,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }));
            if (process is null) return new(SystemMemoryCleanupStatus.Failed, before, null);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(35));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                return new(_stopping.IsCancellationRequested ? SystemMemoryCleanupStatus.Cancelled : SystemMemoryCleanupStatus.TimedOut, before, null);
            }
            var status = Enum.IsDefined(typeof(SystemMemoryCleanupStatus), process.ExitCode)
                ? (SystemMemoryCleanupStatus)process.ExitCode : SystemMemoryCleanupStatus.Failed;
            return new(status, before, await Task.Run(MemoryToolsService.CaptureAvailableBytes));
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { return new(SystemMemoryCleanupStatus.Cancelled, before, null); }
        catch (Win32Exception error) when (error.NativeErrorCode == 5) { return new(SystemMemoryCleanupStatus.PermissionDenied, before, null); }
        catch (Exception) { return new(SystemMemoryCleanupStatus.Failed, before, null); }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    private static SystemMemoryCleanupStatus CleanLowPriorityStandby()
    {
        try
        {
            const uint TokenAccess = 0x20 | 0x8;
            if (!OpenProcessToken(new IntPtr(-1), TokenAccess, out var token)) return SystemMemoryCleanupStatus.PermissionDenied;
            using (token)
            {
                if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid)) return SystemMemoryCleanupStatus.PermissionDenied;
                var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() == 1300)
                    return SystemMemoryCleanupStatus.PermissionDenied;
                // SystemMemoryListInformation / MemoryPurgeLowPriorityStandbyList；不回退为全待机页清理。
                var command = 5;
                var status = NtSetSystemInformation(80, ref command, sizeof(int));
                return status >= 0 ? SystemMemoryCleanupStatus.Completed : unchecked((uint)status) switch
                {
                    0xC0000061 or 0xC0000022 => SystemMemoryCleanupStatus.PermissionDenied,
                    0xC0000003 or 0xC0000002 or 0xC00000BB => SystemMemoryCleanupStatus.Unsupported,
                    _ => SystemMemoryCleanupStatus.Failed
                };
            }
        }
        catch (EntryPointNotFoundException) { return SystemMemoryCleanupStatus.Unsupported; }
        catch (Exception) { return SystemMemoryCleanupStatus.Failed; }
    }

    public void Dispose() { if (_disposed) return; _disposed = true; _stopping.Cancel(); }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disable,
        ref TokenPrivileges state, uint length, IntPtr previous, IntPtr returned);
    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int informationClass, ref int command, int length);
}
