// 统一路由电量提供器；目录与查询串行，避免枚举更新端点时与读取竞争。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>共享设备目录及平台提供器会话，所有调用都在后台完成。</summary>
public sealed class DeviceBatteryReader
{
    private readonly IDeviceBatteryProvider[] _providers;
    // ponytail: 全局串行适用于少量桌面外设；设备很多时改为每个接收器的锁。
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IDeviceBatteryProvider> _sessions = new(StringComparer.OrdinalIgnoreCase);

    private readonly AppLogService? _log;
    public bool HadDiscoveryFailures { get; private set; }
    public DeviceBatteryReader(IEnumerable<IDeviceBatteryProvider>? providers = null, AppLogService? log = null)
    { _providers = providers?.ToArray() ?? [new BluetoothBatteryReader()]; _log = log; }

    public async Task<IReadOnlyList<BluetoothBatteryDevice>> GetDevicesAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var result = new List<BluetoothBatteryDevice>();
            var succeeded = false;
            HadDiscoveryFailures = false;
            foreach (var provider in _providers)
            {
                token.ThrowIfCancellationRequested();
                try { result.AddRange(await provider.GetDevicesAsync(token).ConfigureAwait(false)); succeeded = true; }
                catch (Exception error) when (!token.IsCancellationRequested) { HadDiscoveryFailures = true; _log?.Warn("Battery", provider.Id + " 目录不可用", error); }
            }
            token.ThrowIfCancellationRequested();
            if (!succeeded) throw new InvalidOperationException("没有可用的设备电量目录。");
            // HID 蓝牙端点只在有同一物理容器证据时替代目录条目，不按名称猜测。
            var hidContainers = result.Where(d => d.ProviderId != "bluetooth" && d.Transport == BatteryTransport.Bluetooth && d.ContainerId is not null).Select(d => d.ContainerId).ToHashSet();
            for (var index = 0; index < result.Count; index++)
                if (result[index] is { ProviderId: not "bluetooth", Transport: BatteryTransport.Bluetooth, ContainerId: not null } hid)
                    result[index] = hid with { Peers = hid.Peers.Concat(result.Where(d => d.ProviderId == "bluetooth" && d.ContainerId == hid.ContainerId).SelectMany(d => d.Peers)).DistinctBy(p => p.Id).ToArray() };
            return result.Where(d => d.ProviderId != "bluetooth" || d.ContainerId is null || !hidContainers.Contains(d.ContainerId))
                .OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Id).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<BluetoothBatterySnapshot> ReadAsync(BluetoothBatteryDevice device, string selectedId, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var provider = _providers.First(p => p.Id == device.ProviderId);
            _sessions[device.Id] = provider;
            return await provider.ReadAsync(device, selectedId, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ReleaseExceptAsync(IReadOnlySet<string> targets)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var id in _sessions.Keys.Where(id => !targets.Contains(id)).ToArray())
            {
                var provider = _sessions[id];
                _sessions.Remove(id);
                await provider.ReleaseAsync(id).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }
}
