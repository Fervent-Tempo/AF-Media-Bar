// 目录只枚举配对身份，电量仅查询所选设备；PnP 句柄、GATT 服务和设备对象在读取内释放。
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Abstractions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>标准系统属性及 BLE 电量读取，不主动连接、配对或发送厂商命令。</summary>
public sealed class BluetoothBatteryReader : IDeviceBatteryProvider
{
    private readonly Dictionary<string, (int Percent, DateTimeOffset At)> _observed = new(StringComparer.OrdinalIgnoreCase);
    public string Id => "bluetooth";
    public Task<IReadOnlyList<BluetoothBatteryDevice>> GetDevicesAsync(CancellationToken token) => ListPairedAsync(token);
    public Task ReleaseAsync(string deviceId) { _observed.Remove(deviceId); return Task.CompletedTask; }
    private sealed class PnpBattery
    {
        public int? Percent;
        public DateTimeOffset? Timestamp;
        public HashSet<string> Addresses { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
    public static readonly string[] DeviceProperties = ["System.Devices.Aep.ContainerId", "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.Bluetooth.Cod.Major", "System.Devices.Aep.Bluetooth.Cod.Minor", "System.Devices.Aep.Bluetooth.Le.Appearance"];

    public static string Selector(bool lowEnergy) => lowEnergy ? BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) : BluetoothDevice.GetDeviceSelectorFromPairingState(true);

    public async Task<IReadOnlyList<BluetoothBatteryDevice>> ListPairedAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var result = new List<BluetoothBatteryDevice>();
        foreach (var lowEnergy in new[] { false, true })
        {
            var devices = await DeviceInformation.FindAllAsync(Selector(lowEnergy), DeviceProperties, DeviceInformationKind.AssociationEndpoint).AsTask(timeout.Token).ConfigureAwait(false);
            foreach (var device in devices)
            {
                var address = NormalizeAddress(device.Properties.GetValueOrDefault("System.Devices.Aep.DeviceAddress") as string);
                var container = device.Properties.GetValueOrDefault("System.Devices.Aep.ContainerId") is Guid id ? AudioDeviceIdentity.PhysicalContainer(id) : null;
                var kind = Classify(device.Properties.GetValueOrDefault("System.Devices.Aep.Bluetooth.Cod.Major") as ushort?,
                    device.Properties.GetValueOrDefault("System.Devices.Aep.Bluetooth.Cod.Minor") as ushort?,
                    device.Properties.GetValueOrDefault("System.Devices.Aep.Bluetooth.Le.Appearance") as ushort?);
                var peer = new BluetoothBatteryPeer(device.Id, lowEnergy, device.Properties.GetValueOrDefault("System.Devices.Aep.IsConnected") is true);
                var index = result.FindIndex(item => item.Matches(device.Id) || container is not null && item.ContainerId == container ||
                    address.Length > 0 && item.Address == address);
                if (index < 0) result.Add(new(device.Id, string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name, container, address, [peer]) { Kind = kind });
                else
                {
                    var existing = result[index];
                    result[index] = existing with { Kind = existing.Kind == BatteryDeviceKind.Unknown ? kind : existing.Kind, ContainerId = existing.ContainerId ?? container,
                        Address = existing.Address.Length > 0 ? existing.Address : address, Peers = existing.Peers.Concat([peer]).DistinctBy(item => item.Id).ToArray() };
                }
            }
        }
        return result.OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static BatteryDeviceKind Classify(ushort? major, ushort? minor, ushort? appearance)
    {
        // Bluetooth SIG Assigned Numbers: Appearance 0x03C1–4 / CoD Audio-Video 与 Peripheral。
        if (appearance is 0x03C1) return BatteryDeviceKind.Keyboard;
        if (appearance is 0x03C2) return BatteryDeviceKind.Mouse;
        if (appearance is 0x03C3 or 0x03C4) return BatteryDeviceKind.Controller;
        if (major == 4 && minor is 1 or 6) return BatteryDeviceKind.Headset;
        if (major == 5 && minor is { } code)
        {
            if ((code & 0x30) == 0x10) return BatteryDeviceKind.Keyboard;
            if ((code & 0x30) == 0x20 && (code & 0x0F) == 0) return BatteryDeviceKind.Mouse;
            if ((code & 0x30) == 0 && (code & 0x0F) is 1 or 2) return BatteryDeviceKind.Controller;
        }
        return BatteryDeviceKind.Unknown;
    }

    public async Task<BluetoothBatterySnapshot> ReadAsync(BluetoothBatteryDevice device, string selectedId, CancellationToken token)
    {
        int? percent = null;
        BluetoothBatterySource? source = null;
        DateTimeOffset? timestamp = null;
        if (device.IsConnected)
            foreach (var peer in device.Peers.Where(peer => peer.IsLowEnergy && peer.IsConnected))
            {
                percent = await ReadGattAsync(peer.Id, token).ConfigureAwait(false);
                if (percent is not null) { source = BluetoothBatterySource.BleGatt; timestamp = DateTimeOffset.UtcNow; break; }
            }
        if (device.IsConnected && percent is null && device.ContainerId is { } container)
        {
            var properties = await Task.Run(() => ReadPnp([container], token), token).ConfigureAwait(false);
            var property = properties.GetValueOrDefault(container);
            percent = property?.Percent;
            timestamp = property?.Timestamp;
            if (percent is not null) source = BluetoothBatterySource.WindowsProperty;
        }
        var freshnessKnown = timestamp is not null;
        if (percent is { } observed && timestamp is null)
        {
            if (!_observed.TryGetValue(device.Id, out var previous) || previous.Percent != observed)
                _observed[device.Id] = (observed, DateTimeOffset.UtcNow);
            timestamp = _observed[device.Id].At;
        }
        return new(selectedId, percent, source, device.IsConnected, percent is not null, timestamp)
        { DeviceName = device.DisplayName, Kind = device.Kind, FreshnessKnown = freshnessKnown };
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
            var timestampKey = new DevPropKey { FormatId = batteryKey.FormatId, Id = 3 };
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
                if (!containers.Contains(container) || AudioDeviceIdentity.PhysicalContainer(container) is null) continue;
                if (!result.TryGetValue(container, out var node)) result[container] = node = new();
                var match = Regex.Match(instanceId, @"DEV_([0-9A-F]{12})(?![0-9A-F])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success) node.Addresses.Add(match.Groups[1].Value);
                if (SetupDiGetDeviceProperty(set, ref info, ref batteryKey, out type, buffer, buffer.Length, out size, 0))
                {
                    var value = type == 3 && size == 1 ? buffer[0] : type == 7 && size == 4 ? (long)BitConverter.ToUInt32(buffer) : -1;
                    if (value is < 0 or > 100) continue;
                    DateTimeOffset? timestamp = null;
                    if (SetupDiGetDeviceProperty(set, ref info, ref timestampKey, out type, buffer, buffer.Length, out size, 0) && type == 16 && size == 8)
                    {
                        try { timestamp = DateTimeOffset.FromFileTime(BitConverter.ToInt64(buffer)); }
                        catch (ArgumentOutOfRangeException) { }
                    }
                    if (node.Percent is null || timestamp is not null && (node.Timestamp is null || timestamp > node.Timestamp))
                    { node.Percent = (int)value; node.Timestamp = timestamp; }
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
