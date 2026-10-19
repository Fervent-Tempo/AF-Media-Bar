// 只读取与音频容器可靠关联的蓝牙电量；每批只枚举一次 PnP，句柄与 GATT 对象在读取内释放。
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>标准系统属性及 BLE 电量读取，不连接、配对或发送厂商命令。</summary>
public sealed class BluetoothBatteryReader
{
    private sealed class PnpBattery
    {
        public int? Percent;
        public HashSet<string> Addresses { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private sealed record Peer(string Id, string Address, Guid? AssociationContainer, bool Connected, bool IsLe);
    private readonly Dictionary<Guid, (DateTimeOffset Next, int Failures)> _gattRetries = [];
    private static readonly string[] _properties = ["System.Devices.Aep.ContainerId", "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected"];

    public async Task<IReadOnlyDictionary<Guid, BluetoothBatterySnapshot>> ReadAsync(IReadOnlyList<AudioDeviceOption> endpoints, CancellationToken token)
    {
        var containers = endpoints.Where(device => device.ContainerId is not null).Select(device => device.ContainerId!.Value).ToHashSet();
        if (containers.Count == 0) return new Dictionary<Guid, BluetoothBatterySnapshot>();
        var pnp = await Task.Run(() => ReadPnp(containers, token), token).ConfigureAwait(false);
        var peers = new List<Peer>();
        foreach (var isLe in new[] { false, true })
        {
            var selector = isLe ? BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) : BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector, _properties, DeviceInformationKind.AssociationEndpoint).AsTask(token).ConfigureAwait(false);
            foreach (var device in devices)
            {
                var address = device.Properties.GetValueOrDefault("System.Devices.Aep.DeviceAddress") as string;
                var association = device.Properties.GetValueOrDefault("System.Devices.Aep.ContainerId") is Guid id && id != Guid.Empty ? id : (Guid?)null;
                peers.Add(new(device.Id, NormalizeAddress(address), association,
                    device.Properties.GetValueOrDefault("System.Devices.Aep.IsConnected") is true, isLe));
            }
        }
        var result = new Dictionary<Guid, BluetoothBatterySnapshot>();
        foreach (var container in containers)
        {
            token.ThrowIfCancellationRequested();
            pnp.TryGetValue(container, out var node);
            var matches = peers.Where(peer => peer.AssociationContainer == container || node?.Addresses.Contains(peer.Address) == true).ToArray();
            if (node is null && matches.Length == 0) continue;
            var connected = matches.Any(peer => peer.Connected);
            var percent = connected ? node?.Percent : null;
            BluetoothBatterySource? source = percent is not null ? BluetoothBatterySource.WindowsProperty : null;
            if (connected && percent is null && (!_gattRetries.TryGetValue(container, out var retry) || DateTimeOffset.UtcNow >= retry.Next))
            {
                foreach (var peer in matches.Where(peer => peer.Connected && peer.IsLe))
                {
                    percent = await ReadGattAsync(peer.Id, token).ConfigureAwait(false);
                    if (percent is not null) { source = BluetoothBatterySource.BleGatt; break; }
                }
            }
            if (percent is not null) _gattRetries.Remove(container);
            else if (connected && (!_gattRetries.TryGetValue(container, out var oldRetry) || DateTimeOffset.UtcNow >= oldRetry.Next))
            {
                var failures = Math.Min(_gattRetries.GetValueOrDefault(container).Failures + 1, 4);
                _gattRetries[container] = (DateTimeOffset.UtcNow.AddSeconds(Math.Min(60 * (1 << failures), 600)), failures);
            }
            foreach (var absent in _gattRetries.Keys.Where(id => !containers.Contains(id)).ToArray()) _gattRetries.Remove(absent);
            result[container] = new(container, percent, source, connected, percent is not null,
                percent is not null ? DateTimeOffset.UtcNow : null);
        }
        return result;
    }

    private static async Task<int?> ReadGattAsync(string deviceId, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(deviceId).AsTask(timeout.Token).ConfigureAwait(false);
            if (device is null || device.ConnectionStatus != BluetoothConnectionStatus.Connected) return null;
            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(timeout.Token).ConfigureAwait(false);
            if (services.Status != GattCommunicationStatus.Success) return null;
            try
            {
                foreach (var service in services.Services)
                {
                    var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(timeout.Token).ConfigureAwait(false);
                    if (characteristics.Status != GattCommunicationStatus.Success) continue;
                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token).ConfigureAwait(false);
                        if (value.Status != GattCommunicationStatus.Success || value.Value is null || value.Value.Length != 1) continue;
                        using var reader = DataReader.FromBuffer(value.Value);
                        var percent = reader.ReadByte();
                        if (percent <= 100) return percent;
                    }
                }
            }
            finally { foreach (var service in services.Services) service.Dispose(); }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        catch (Exception) when (!token.IsCancellationRequested) { }
        return null;
    }

    private static Dictionary<Guid, PnpBattery> ReadPnp(HashSet<Guid> containers, CancellationToken token)
    {
        var result = new Dictionary<Guid, PnpBattery>();
        var set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 2 | 4);
        if (set == new IntPtr(-1)) return result;
        try
        {
            var containerKey = new DevPropKey { FormatId = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), Id = 2 };
            // 驱动提供的属性并非统一电量协议；缺失或类型不符时交给 BLE/未知回退。
            var batteryKey = new DevPropKey { FormatId = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), Id = 2 };
            for (uint index = 0; ; index++)
            {
                token.ThrowIfCancellationRequested();
                var info = new DevInfo { Size = (uint)Marshal.SizeOf<DevInfo>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info)) break;
                var text = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceId(set, ref info, text, text.Capacity, out _)) continue;
                var instanceId = text.ToString();
                if (!instanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) continue;
                var buffer = new byte[16];
                if (!SetupDiGetDeviceProperty(set, ref info, ref containerKey, out var type, buffer, buffer.Length, out var size, 0) || type != 13 || size != 16) continue;
                var container = new Guid(buffer);
                if (!containers.Contains(container) || container == Guid.Empty) continue;
                if (!result.TryGetValue(container, out var node)) result[container] = node = new();
                var match = Regex.Match(instanceId, @"DEV_([0-9A-F]{12})(?![0-9A-F])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success) node.Addresses.Add(match.Groups[1].Value);
                if (SetupDiGetDeviceProperty(set, ref info, ref batteryKey, out type, buffer, buffer.Length, out size, 0))
                {
                    var value = type == 3 && size == 1 ? buffer[0] : type == 7 && size == 4 ? (long)BitConverter.ToUInt32(buffer) : -1;
                    if (value is >= 0 and <= 100 && node.Percent is null) node.Percent = (int)value;
                }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    private static string NormalizeAddress(string? value)
    {
        var address = (value ?? string.Empty).Replace(":", "").Replace("-", "");
        return address.Length == 12 && address.All(char.IsAsciiHexDigit) ? address.ToUpperInvariant() : string.Empty;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DevInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey { public Guid FormatId; public uint Id; }
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DevInfo info);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DevInfo info, StringBuilder text, int capacity, out uint required);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DevInfo info, ref DevPropKey key, out uint type,
        [Out] byte[] buffer, int size, out uint required, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
