// 音频应用身份及持久化路由；COM 对象由平台线程上的单次操作释放。
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Microsoft.Win32.SafeHandles;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>按可验证的应用身份操作路由，分别检查每个进程和角色。</summary>
public sealed class ApplicationAudioRoutingService(AudioPlatformService platform, AudioDeviceService devices, AudioProcessInfoService processInfo)
{
    public Task<IReadOnlyList<ApplicationAudioSnapshot>> GetApplicationsAsync(CancellationToken token = default) =>
        platform.RunAsync<IReadOnlyList<ApplicationAudioSnapshot>>(EnumerateApplications, token);
    private IReadOnlyList<ApplicationAudioSnapshot> EnumerateApplications()
    {
        var groups = new Dictionary<string, (string Name, string Display, Dictionary<int, AudioProcessIdentity> Processes, bool Output, bool Input)>(StringComparer.OrdinalIgnoreCase);
        using var enumerator = new MMDeviceEnumerator();
        foreach (var direction in Enum.GetValues<AudioDeviceDirection>())
        {
            using var endpoints = enumerator.EnumerateAudioEndPoints(direction == AudioDeviceDirection.Output ? DataFlow.Render : DataFlow.Capture, DeviceState.Active);
            for (var i = 0; i < endpoints.Count; i++)
                try
                {
                    using var endpoint = endpoints[i];
                    var sessions = endpoint.AudioSessionManager.Sessions;
                    for (var j = 0; j < sessions.Count; j++)
                        try
                        {
                            using var session = sessions[j];
                            if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                            var process = ReadIdentity(checked((int)session.GetProcessID));
                            if (process is null || process.ProcessId == Environment.ProcessId) continue;
                            var key = AudioApplicationIdentityPolicy.GetKey(process);
                            var name = Path.GetFileNameWithoutExtension(process.ExecutablePath);
                            if (!groups.TryGetValue(key, out var group)) group = (name, name, new(), false, false);
                            var display = session.DisplayName;
                            if (!string.IsNullOrWhiteSpace(display) && !display.StartsWith('@')) group.Display = display;
                            group.Processes[process.ProcessId] = process;
                            group.Output |= direction == AudioDeviceDirection.Output;
                            group.Input |= direction == AudioDeviceDirection.Input;
                            groups[key] = group;
                        }
                        catch (Exception exception) when (exception is COMException or InvalidCastException or OverflowException) { }
                }
                catch (COMException) { }
        }
        return groups.Select(pair => new ApplicationAudioSnapshot(pair.Key, pair.Value.Name, pair.Value.Display,
            pair.Value.Processes.Values.OrderBy(process => process.ProcessId).ToArray(), pair.Value.Output, pair.Value.Input))
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    internal AudioProcessIdentity? ReadIdentity(int processId)
    {
        if (processId <= 0) return null;
        try
        {
            var path = processInfo.GetExecutablePath((uint)processId);
            if (string.IsNullOrEmpty(path)) return null;
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return null;
            string? package = null;
            using var handle = OpenProcess(0x1000, false, processId);
            if (!handle.IsInvalid)
            {
                uint length = 0;
                if (GetApplicationUserModelId(handle, ref length, null) == 122 && length > 0)
                {
                    var text = new StringBuilder(checked((int)length));
                    if (GetApplicationUserModelId(handle, ref length, text) == 0) package = text.ToString();
                }
            }
            return new(processId, process.StartTime.ToUniversalTime().Ticks, path, package);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { return null; }
    }
    internal bool IsCurrent(AudioProcessIdentity process) =>
        AudioApplicationIdentityPolicy.IsSameProcess(process, ReadIdentity(process.ProcessId));
    public Task<ApplicationAudioRoute> ReadRouteAsync(ApplicationAudioSnapshot app, AudioDeviceDirection direction, CancellationToken token = default) =>
        platform.RunAsync(() =>
        {
            try
            {
                using var policy = new ApplicationAudioPolicy();
                var values = new List<string?>();
                foreach (var process in app.Processes)
                {
                    if (!IsCurrent(process)) return new ApplicationAudioRoute(null, false, true, "Audio.App.Unavailable");
                    for (var role = 0; role < 3; role++) values.Add(policy.Get(process.ProcessId, direction, role));
                }
                var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return new ApplicationAudioRoute(distinct.Length == 1 ? distinct[0] : null, distinct.Length > 1, true);
            }
            catch (COMException exception) when (IsUnsupported(exception)) { return new ApplicationAudioRoute(null, false, false, "Audio.Route.Unsupported"); }
            catch (Exception) { return new ApplicationAudioRoute(null, false, true, "Audio.Route.ReadFailed"); }
        }, token);
    public Task<AudioDeviceOperationResult> SetRouteAsync(ApplicationAudioSnapshot app, AudioDeviceDirection direction,
        AudioDeviceOption? device, CancellationToken token = default, Func<bool>? isCurrent = null) => platform.RunAsync(() =>
    {
        if (isCurrent?.Invoke() == false) return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Cancelled);
        if (device is not null)
        {
            if (device.Direction != direction || !devices.IsAllowed(direction, device.PolicyId))
                return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Unavailable);
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDevice(device.PolicyId);
            if (endpoint.State != DeviceState.Active || endpoint.DataFlow != (direction == AudioDeviceDirection.Output ? DataFlow.Render : DataFlow.Capture))
                return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Unavailable);
        }
        if (app.Processes.Count == 0 || app.Processes.Any(process => !IsCurrent(process)))
            return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Unavailable);
        var succeeded = 0;
        var changed = 0;
        var failed = 0;
        string? error = null;
        try
        {
            using var policy = new ApplicationAudioPolicy();
            foreach (var process in app.Processes)
                for (var role = 0; role < 3; role++)
                {
                    token.ThrowIfCancellationRequested();
                    if (isCurrent?.Invoke() == false || !IsCurrent(process) ||
                        (device is not null && !devices.IsAllowed(direction, device.PolicyId)))
                        return new AudioDeviceOperationResult(changed > 0 ? AudioDeviceOperationStatus.Partial : AudioDeviceOperationStatus.Cancelled);
                    try
                    {
                        if (!string.Equals(policy.Get(process.ProcessId, direction, role), device?.PolicyId, StringComparison.OrdinalIgnoreCase))
                        {
                            policy.Set(process.ProcessId, direction, role, device?.PolicyId);
                            changed++;
                        }
                        succeeded++;
                    }
                    catch (COMException exception) { failed++; error = exception.Message; }
                }
            return new AudioDeviceOperationResult(failed > 0 ? (succeeded > 0 ? AudioDeviceOperationStatus.Partial : AudioDeviceOperationStatus.Failed) :
                changed > 0 ? AudioDeviceOperationStatus.Applied : AudioDeviceOperationStatus.Unchanged, error);
        }
        catch (COMException exception) when (IsUnsupported(exception)) { return new AudioDeviceOperationResult(AudioDeviceOperationStatus.Unsupported); }
        catch (OperationCanceledException) { return new AudioDeviceOperationResult(changed > 0 ? AudioDeviceOperationStatus.Partial : AudioDeviceOperationStatus.Cancelled); }
        catch (Exception exception) { return new AudioDeviceOperationResult(changed > 0 ? AudioDeviceOperationStatus.Partial : AudioDeviceOperationStatus.Failed, exception.Message); }
    }, token);
    private static bool IsUnsupported(COMException exception) =>
        exception.ErrorCode is unchecked((int)0x80004002) or unchecked((int)0x80040154) or unchecked((int)0x8000000F);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? value);
}
