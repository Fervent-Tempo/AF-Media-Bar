// 外部前台应用跟踪；Hook 在音频线程安装和释放，回调只排队身份读取。
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>前台应用身份的只读平台边界。</summary>
public interface IForegroundAudioApplicationResolver
{
    Task<ApplicationAudioSnapshot?> CaptureAsync(CancellationToken token = default);
}

/// <summary>捕获最近外部应用，避免 AF 菜单取得焦点后改变设备操作目标。</summary>
public sealed class ForegroundAudioApplicationService : IForegroundAudioApplicationResolver, IDisposable
{
    private readonly AudioPlatformService _platform;
    private readonly ApplicationAudioRoutingService _routing;
    private readonly WinEventCallback _callback;
    private AudioProcessIdentity? _last;
    private nint _hook;
    private int _disposed;
    private readonly Task _startup;
    public ForegroundAudioApplicationService(AudioPlatformService platform, ApplicationAudioRoutingService routing)
    {
        _platform = platform;
        _routing = routing;
        _callback = (_, _, window, _, _, _, _) => _ = RecordAsync(window);
        _startup = StartAsync();
    }
    private async Task StartAsync()
    {
        try
        {
            await _platform.RunAsync(() =>
            {
                if (_disposed != 0) return false;
                _hook = SetWinEventHook(3, 3, 0, _callback, 0, 0, 0);
                _platform.RegisterCleanup(() => { if (_hook != 0) { UnhookWinEvent(_hook); _hook = 0; } });
                RecordWindow(GetForegroundWindow());
                return true;
            });
        }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private async Task RecordAsync(nint window)
    {
        try { await _platform.RunAsync(() => { RecordWindow(window); return true; }); }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private void RecordWindow(nint window)
    {
        if (_disposed != 0) return;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return;
        var identity = _routing.ReadIdentity(processId);
        if (identity is null) return;
        var name = Path.GetFileNameWithoutExtension(identity.ExecutablePath);
        if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)) return;
        _last = identity;
    }

    public async Task<ApplicationAudioSnapshot?> CaptureAsync(CancellationToken token = default)
    {
        // 在调用边界复制 HWND/PID，身份读取排队期间焦点变化不会改变本次目标。
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundPid);
        var last = Volatile.Read(ref _last);
        await _startup;
        var identity = await _platform.RunAsync(() =>
        {
            var captured = foregroundPid == Environment.ProcessId ? null : _routing.ReadIdentity(foregroundPid);
            if (captured is not null && !IsShell(captured)) return captured;
            if (captured is null && foregroundPid != Environment.ProcessId) return null;
            return last is not null && _routing.IsCurrent(last) ? last : null;
        }, token);
        if (identity is null) return null;
        var apps = await _routing.GetApplicationsAsync(token);
        var exact = apps.FirstOrDefault(app => app.Processes.Any(process => process.ProcessId == identity.ProcessId)) ??
            apps.FirstOrDefault(app => app.Identity == AudioApplicationIdentityPolicy.GetKey(identity));
        if (exact is not null) return exact;
        return await _platform.RunAsync(() =>
        {
            if (!_routing.IsCurrent(identity)) return null;
            var parents = GetParentProcesses();
            return apps.FirstOrDefault(app => app.Processes.Any(process => IsVerifiedChild(process, identity, parents)));
        }, token);
    }
    private static bool IsShell(AudioProcessIdentity identity) =>
        Path.GetFileNameWithoutExtension(identity.ExecutablePath).ToLowerInvariant() is
            "explorer" or "shellexperiencehost" or "startmenuexperiencehost" or "searchhost";
    private bool IsVerifiedChild(AudioProcessIdentity process, AudioProcessIdentity parent, Dictionary<int, int> parents)
    {
        if (!_routing.IsCurrent(process) || process.StartedAtUtcTicks < parent.StartedAtUtcTicks ||
            !string.Equals(Path.GetDirectoryName(process.ExecutablePath), Path.GetDirectoryName(parent.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            return false;
        var visited = new HashSet<int>();
        var pid = process.ProcessId;
        while (visited.Add(pid) && parents.TryGetValue(pid, out pid))
        {
            if (pid == parent.ProcessId) return true;
            var ancestor = _routing.ReadIdentity(pid);
            if (ancestor is null || ancestor.StartedAtUtcTicks < parent.StartedAtUtcTicks ||
                ancestor.StartedAtUtcTicks > process.StartedAtUtcTicks) return false;
        }
        return false;
    }
    private static Dictionary<int, int> GetParentProcesses()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new nint(-1)) return [];
        try
        {
            var result = new Dictionary<int, int>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Executable = string.Empty };
            if (Process32First(snapshot, ref entry))
                do { result[(int)entry.ProcessId] = (int)entry.ParentProcessId; } while (Process32Next(snapshot, ref entry));
            return result;
        }
        finally { CloseHandle(snapshot); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public nuint DefaultHeap;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [DllImport("kernel32.dll")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _ = ReleaseAsync();
    }
    private async Task ReleaseAsync()
    {
        try { await _platform.RunAsync(() => { if (_hook != 0) { UnhookWinEvent(_hook); _hook = 0; } return true; }); }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private delegate void WinEventCallback(nint hook, uint type, nint window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int process);
}
