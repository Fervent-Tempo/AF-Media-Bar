// 在后台把媒体栏当前来源解析为音频进程树；每次调用自行释放会话、端点及进程快照，不持有播放器资源。
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Models;
using Microsoft.Win32.SafeHandles;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>通过共享来源映射和真实音频会话解析采集 PID；不把未识别来源回退为全设备音频。</summary>
public sealed class SpectrumCaptureProcessResolver(MediaSourceProcessResolver sourceResolver)
{
    private const uint SnapshotProcesses = 0x00000002;

    public uint? ResolveProcessId(MediaSnapshot source, uint? currentProcessId, CancellationToken token) =>
        ResolveProcessId(MediaIdentity.FromSnapshot(source), currentProcessId, token);

    // 采集只保留来源身份，避免长时间引用某一曲目的封面和歌词。
    internal readonly record struct MediaIdentity(bool IsConnected, string SourceId, string SourceName)
    {
        internal static MediaIdentity FromSnapshot(MediaSnapshot snapshot) =>
            new(snapshot.IsConnected, snapshot.SourceId, snapshot.SourceName);
    }

    internal uint? ResolveProcessId(MediaIdentity source, uint? currentProcessId, CancellationToken token)
    {
        if (!source.IsConnected || string.IsNullOrWhiteSpace(source.SourceId)) return null;
        token.ThrowIfCancellationRequested();
        var processes = ReadProcesses();
        var names = sourceResolver.ResolveProcessNames(source.SourceId);
        var candidates = new Dictionary<uint, float>();
        using var enumerator = new MMDeviceEnumerator();
        using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (var index = 0; index < devices.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var device = devices[index];
                var sessions = device.AudioSessionManager.Sessions;
                for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var session = sessions[sessionIndex];
                        var processId = session.GetProcessID;
                        if (session.State == AudioSessionState.AudioSessionStateExpired || processId == 0 ||
                            processId == Environment.ProcessId || !processes.TryGetValue(processId, out var process) ||
                            string.IsNullOrWhiteSpace(process.Name)) continue;
                        if (!sourceResolver.Matches(source.SourceId, source.SourceName, process.Name, session.DisplayName)) continue;
                        // 父进程只能按明确别名或同名关联；宽松的来源子串匹配可能误把启动器纳入采集。
                        var root = ResolveRoot(processId, processes, name => names.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                            (names.Count == 0 && string.Equals(name, source.SourceName, StringComparison.OrdinalIgnoreCase)));
                        if (root == Environment.ProcessId) continue;
                        if (names.Count > 0 && (!processes.TryGetValue(root, out var rootProcess) ||
                            !names.Contains(rootProcess.Name, StringComparer.OrdinalIgnoreCase))) continue;
                        var peak = session.AudioMeterInformation?.MasterPeakValue ?? 0;
                        candidates[root] = Math.Max(candidates.GetValueOrDefault(root), peak);
                    }
                    catch (COMException) { }
                }
            }
            catch (COMException) { }
        }

        // 暂停的播放器可能暂时没有音频会话；仅用已有来源别名找到它，不凭其他程序的电平猜测。
        if (candidates.Count == 0)
        {
            foreach (var process in processes.Values)
            {
                if (process.Id == 0 || process.Id == Environment.ProcessId || string.IsNullOrWhiteSpace(process.Name) ||
                    !names.Contains(process.Name, StringComparer.OrdinalIgnoreCase)) continue;
                var root = ResolveRoot(process.Id, processes, name => names.Contains(name, StringComparer.OrdinalIgnoreCase));
                if (root != Environment.ProcessId) candidates.TryAdd(root, 0);
            }
        }
        return SelectProcess(candidates, currentProcessId);
    }

    internal static uint? SelectProcess(IReadOnlyDictionary<uint, float> candidates, uint? currentProcessId)
    {
        if (candidates.Count == 0) return null;
        var audible = candidates.Where(candidate => candidate.Value >= 0.002f).ToArray();
        if (currentProcessId is { } current && candidates.ContainsKey(current) &&
            (audible.Length == 0 || audible.Any(candidate => candidate.Key == current))) return current;
        return candidates.OrderByDescending(candidate => candidate.Value).ThenBy(candidate => candidate.Key).First().Key;
    }

    internal static uint ResolveRoot(uint processId, IReadOnlyDictionary<uint, ProcessIdentity> processes, Func<string, bool> matches)
    {
        var visited = new HashSet<uint>();
        while (visited.Add(processId) && processes.TryGetValue(processId, out var process) &&
            processes.TryGetValue(process.ParentId, out var parent) && !visited.Contains(parent.Id) &&
            !string.IsNullOrWhiteSpace(parent.Name) &&
            (string.Equals(parent.Name, process.Name, StringComparison.OrdinalIgnoreCase) || matches(parent.Name)))
            processId = parent.Id;
        return processId;
    }

    internal readonly record struct ProcessIdentity(uint Id, uint ParentId, string Name);

    private static Dictionary<uint, ProcessIdentity> ReadProcesses()
    {
        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = new Dictionary<uint, ProcessIdentity>();
        var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
        if (!Process32First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
        do
        {
            result[entry.th32ProcessID] = new ProcessIdentity(entry.th32ProcessID, entry.th32ParentProcessID,
                Path.GetFileNameWithoutExtension(entry.szExeFile));
        } while (Process32Next(snapshot, ref entry));
        return result;
    }

    // 保留 Win32 结构字段名；命名规则只在这份原生声明中暂停。
#pragma warning disable IDE1006
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
#pragma warning restore IDE1006

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref PROCESSENTRY32 entry);
}
