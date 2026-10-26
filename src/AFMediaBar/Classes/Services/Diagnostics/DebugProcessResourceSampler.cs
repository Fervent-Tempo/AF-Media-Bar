#if DEBUG
// 后台拥有进程句柄和 CPU 差分基线；只枚举 AF 后代中的 WebView2，不读取命令行。
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>AF 与所属 WebView2 的有界资源快照。</summary>
internal sealed record DebugProcessResources(int Pid, DateTimeOffset StartedAt, string Role,
    long WorkingSetBytes, long PrivateBytes, double? CpuPercent, int? Handles, int? Threads);

/// <summary>五秒刷新进程树，逐次读取资源；退出和 PID 复用不沿用旧差分基线。</summary>
internal sealed class DebugProcessResourceSampler : IDisposable
{
    private readonly Dictionary<int, Entry> _processes = [];
    private long? _lastRefresh;

    internal DebugProcessResources[] Sample(bool resetBaseline)
    {
        if (_lastRefresh is not long last || Stopwatch.GetElapsedTime(last).TotalSeconds >= 5)
        {
            RefreshTree();
            _lastRefresh = Stopwatch.GetTimestamp();
        }
        List<DebugProcessResources> results = [];
        foreach (var pair in _processes.ToArray())
        {
            var entry = pair.Value;
            try
            {
                if (entry.Process.HasExited) { Remove(pair.Key); continue; }
                entry.Process.Refresh();
                var now = Stopwatch.GetTimestamp();
                var cpu = entry.Process.TotalProcessorTime;
                double? percent = !resetBaseline && entry.Timestamp is long previous
                    ? Math.Clamp((cpu - entry.Cpu).TotalSeconds / Stopwatch.GetElapsedTime(previous, now).TotalSeconds / Environment.ProcessorCount * 100, 0, 100)
                    : null;
                entry.Timestamp = now;
                entry.Cpu = cpu;
                var main = pair.Key == Environment.ProcessId;
                results.Add(new(pair.Key, entry.StartedAt, main ? "AF" : "WebView2", entry.Process.WorkingSet64,
                    entry.Process.PrivateMemorySize64, percent, main ? entry.Process.HandleCount : null,
                    main ? entry.Process.Threads.Count : null));
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            { Remove(pair.Key); }
        }
        return results.ToArray();
    }

    private void RefreshTree()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) return;
        var item = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Name = string.Empty };
        List<(int Pid, int Parent, string Name)> entries = [];
        if (!Process32First(snapshot, ref item)) return;
        do { entries.Add((checked((int)item.ProcessId), checked((int)item.ParentProcessId), item.Name)); }
        while (Process32Next(snapshot, ref item));
        var descendants = new HashSet<int> { Environment.ProcessId };
        for (var pass = 0; pass < 16; pass++)
        {
            var changed = false;
            foreach (var process in entries)
                if (descendants.Contains(process.Parent)) changed |= descendants.Add(process.Pid);
            if (!changed) break;
        }
        var wanted = entries.Where(entry => descendants.Contains(entry.Pid) &&
            (entry.Pid == Environment.ProcessId || entry.Name.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.Pid == Environment.ProcessId ? 0 : 1).Take(32).Select(entry => entry.Pid).ToHashSet();
        foreach (var pid in _processes.Keys.Where(pid => !wanted.Contains(pid)).ToArray()) Remove(pid);
        foreach (var pid in wanted)
        {
            if (_processes.ContainsKey(pid)) continue;
            Process? process = null;
            try
            {
                process = Process.GetProcessById(pid);
                _processes[pid] = new(process, new DateTimeOffset(process.StartTime));
                process = null;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process?.Dispose(); }
        }
    }

    private void Remove(int pid)
    {
        if (_processes.Remove(pid, out var entry)) entry.Process.Dispose();
    }

    public void Dispose()
    {
        foreach (var entry in _processes.Values) entry.Process.Dispose();
        _processes.Clear();
    }

    private sealed class Entry(Process process, DateTimeOffset startedAt)
    {
        internal Process Process { get; } = process;
        internal DateTimeOffset StartedAt { get; } = startedAt;
        internal long? Timestamp { get; set; }
        internal TimeSpan Cpu { get; set; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        internal uint Size;
        internal uint Usage;
        internal uint ProcessId;
        internal UIntPtr DefaultHeapId;
        internal uint ModuleId;
        internal uint Threads;
        internal uint ParentProcessId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Name;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}
#endif
