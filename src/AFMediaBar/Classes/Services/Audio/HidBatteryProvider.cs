// 已知 HID 协议的目录与读取；仅 IN10 持有被动监听会话，追踪结束时取消并等待释放。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>首批 USB／接收器电量适配器，不发送设备配置写入。</summary>
public sealed class HidBatteryProvider(AppLogService log) : IDeviceBatteryProvider
{
    private sealed record Target(BatteryHidEndpoint Endpoint, HidBatteryKind Kind, byte Slot = 1, byte Feature = 0, bool Unified = false);
    private sealed class PassiveSession
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Loop { get; set; } = Task.CompletedTask;
        public BluetoothBatterySnapshot? Snapshot;
        public TaskCompletionSource FirstReading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private Dictionary<string, Target> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PassiveSession> _passive = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private byte _sequence;
    public string Id => "hid";
    private static readonly HashSet<ushort> RazerReceivers = [0x007B, 0x00C1, 0x00DF, 0x00B7, 0x00C3, 0x00BF, 0x00E6,
        0x00AB, 0x00CD, 0x00D7, 0x00C5, 0x00D1, 0x00C8, 0x00D4, 0x00B0, 0x00DB, 0x00B4, 0x00B8, 0x00B9, 0x00B3, 0x009A, 0x005A, 0x0045];
    internal static HidBatteryKind? Match(BatteryHidEndpoint h)
    {
        if (h.VendorId == 0x046D && h.UsagePage == 0xFF00 && h.Usage == 2 && h.InputLength == 20 && h.OutputLength == 20) return HidBatteryKind.Logitech;
        if (h.VendorId == 0x3554 && h.ProductId == 0xFA09 && h.UsagePage == 0xFF02 && h.Usage == 2 && h.InputLength == 20 && h.OutputLength == 20) return HidBatteryKind.Mchose;
        if (h.VendorId == 0x0B05 && h.ProductId == 0x1ACE && h.UsagePage == 0xFF00 && h.InputLength == 64 && h.OutputLength == 64) return HidBatteryKind.Rog;
        if (h.VendorId == 0x1532 && RazerReceivers.Contains(h.ProductId) && h.FeatureLength == 91 && h.UsagePage >= 0xFF00) return HidBatteryKind.RazerMouse;
        if (h.VendorId == 0x1532 && h.ProductId == 0x0555 && h.UsagePage == 0xFF00 && h.InputLength == 64 && h.OutputLength == 64) return HidBatteryKind.RazerHeadset;
        if (h.VendorId == 0x373B && h.ProductId == 0x1278 && h.UsagePage == 0xFF02 && h.Usage == 2 && h.InputLength == 17 && h.OutputLength == 17) return HidBatteryKind.AtkMouse;
        if (h.VendorId == 0x373B && h.ProductId == 0x1262 && h.UsagePage == 0xFF90 && h.InputLength == 64 && h.OutputLength == 64) return HidBatteryKind.AtkHeadset;
        var in10 = h.VendorId == 0x1D57 && h.ProductId is 0xFA60 or 0xFA65 || h.VendorId == 0x045E && h.ProductId is 0x0095 or 0x0090 or 0x0093 or 0x0099 or 0x0098 or 0x0273 or 0x0271;
        if (in10 && h.UsagePage == 0x000A && h.InputLength == 5) return HidBatteryKind.Inphic10;
        if (h.VendorId is 0x248A or 0x249A && h.ProductId is 0x5B2E or 0x5B2F && h.FeatureLength == 33 &&
            !(h.UsagePage == 1 && h.Usage is 2 or 6)) return HidBatteryKind.Inphic9;
        return null;
    }
    public async Task<IReadOnlyList<BluetoothBatteryDevice>> GetDevicesAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var endpoints = await Task.Run(() => BatteryHidTransport.Enumerate(timeout.Token), timeout.Token).ConfigureAwait(false);
        var targets = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        var devices = new List<BluetoothBatteryDevice>();
        foreach (var endpoint in endpoints.OrderBy(h => h.Path, StringComparer.OrdinalIgnoreCase))
        {
            timeout.Token.ThrowIfCancellationRequested();
            var kind = Match(endpoint);
            if (kind is null) continue;
            var baseId = $"hid:{kind}:{endpoint.PhysicalId}";
            if (kind != HidBatteryKind.Logitech)
            {
                Add(baseId, new(endpoint, kind.Value), endpoint.Name);
                continue;
            }
            try
            {
                using var transport = new BatteryHidTransport(endpoint);
                foreach (var slot in new byte[] { 1, 2, 3, 4, 5, 6, 0xFF })
                {
                    var id = baseId + ":" + slot;
                    var unified = await GetFeatureAsync(transport, slot, 0x1004, timeout.Token).ConfigureAwait(false);
                    var feature = unified ?? await GetFeatureAsync(transport, slot, 0x1000, timeout.Token).ConfigureAwait(false);
                    if (feature is > 0)
                    {
                        var target = new Target(endpoint, kind.Value, slot, feature.Value, unified is > 0);
                        if (!_names.TryGetValue(id, out var name))
                            _names[id] = name = await GetLogitechNameAsync(transport, slot, timeout.Token).ConfigureAwait(false) ?? endpoint.Name;
                        Add(id, target, name + $" · #{slot:X2}");
                    }
                    else if (_targets.TryGetValue(id, out var previous)) Add(id, previous with { Endpoint = endpoint }, _names.GetValueOrDefault(id, endpoint.Name) + $" · #{slot:X2}");
                }
            }
            catch (Exception error) when (!timeout.IsCancellationRequested)
            {
                log.Warn("Battery", "HID++ 目录查询不可用", error);
                foreach (var (id, previous) in _targets.Where(pair => pair.Key.StartsWith(baseId + ":", StringComparison.OrdinalIgnoreCase)))
                    Add(id, previous with { Endpoint = endpoint }, _names.GetValueOrDefault(id, endpoint.Name) + $" · #{previous.Slot:X2}");
            }
        }
        _targets = targets;
        foreach (var id in _names.Keys.Where(id => !targets.ContainsKey(id)).ToArray()) _names.Remove(id);
        return devices;

        void Add(string id, Target target, string name)
        {
            if (!targets.TryAdd(id, target)) return;
            var h = target.Endpoint;
            // 共用 USB 标识时不把产品名称强制替换成某个品牌或型号。
            if (string.IsNullOrWhiteSpace(name)) name = $"HID {h.VendorId:X4}:{h.ProductId:X4}";
            devices.Add(new(id, name, h.ContainerId, string.Empty, [])
            {
                ProviderId = Id, IsPresent = true, IsPassive = target.Kind == HidBatteryKind.Inphic10,
                Protocol = target.Kind.ToString(), Transport = h.IsBluetooth ? BatteryTransport.Bluetooth : target.Kind == HidBatteryKind.Inphic9 && h.ProductId == 0x5B2E ? BatteryTransport.Usb : BatteryTransport.Receiver
            });
        }
    }
    public async Task<BluetoothBatterySnapshot> ReadAsync(BluetoothBatteryDevice device, string selectedId, CancellationToken token)
    {
        var target = _targets[device.Id];
        if (target.Kind == HidBatteryKind.Inphic10) return await ReadPassiveAsync(device, selectedId, target, token).ConfigureAwait(false);
        using var transport = new BatteryHidTransport(target.Endpoint, target.Kind == HidBatteryKind.RazerMouse ? 0x40000000u : 0xC0000000u);
        var sequence = unchecked(++_sequence); if (sequence == 0) sequence = ++_sequence;
        HidBatteryValue? value = null;
        if (target.Kind == HidBatteryKind.Logitech)
        {
            var frame = await LogitechCallAsync(transport, target.Slot, target.Feature, target.Unified ? (byte)1 : (byte)0, [], token).ConfigureAwait(false);
            if (frame is not null) value = HidBatteryProtocol.ParseLogitech(frame, target.Slot, target.Feature, target.Unified);
        }
        else if (target.Kind == HidBatteryKind.RazerMouse)
        {
            value = await QueryFeatureAsync(transport, target, sequence, 0x80, token).ConfigureAwait(false);
            if (value is not null)
            {
                var charge = await QueryFeatureAsync(transport, target, ++sequence, 0x84, token).ConfigureAwait(false);
                value = value with { Charging = charge?.Percent switch { 0 => false, 1 => true, _ => null } };
            }
        }
        else if (target.Kind == HidBatteryKind.Inphic9)
        {
            await transport.SetFeatureAsync(HidBatteryProtocol.Query(target.Kind, sequence, 0xF0), token).ConfigureAwait(false);
            value = await QueryFeatureAsync(transport, target, sequence, 0x20, token).ConfigureAwait(false);
        }
        else
        {
            value = await QueryInputAsync(transport, target.Kind, 0x21, token).ConfigureAwait(false);
            if (value is not null && target.Kind == HidBatteryKind.RazerHeadset)
            {
                var charge = await QueryInputAsync(transport, target.Kind, 0x2A, token).ConfigureAwait(false);
                value = value with { Charging = charge?.Percent switch { 0 => false, 1 => true, _ => null } };
            }
        }
        return SnapshotFor(device, selectedId, value);
    }
    private static BluetoothBatterySnapshot SnapshotFor(BluetoothBatteryDevice device, string id, HidBatteryValue? value) =>
        new(id, value?.Percent, BluetoothBatterySource.Hid, value is not null, value is not null, value is not null ? DateTimeOffset.UtcNow : null)
        { DeviceName = device.DisplayName, Transport = device.Transport, Protocol = device.Protocol, IsCharging = value?.Charging };
    private static async Task<HidBatteryValue?> QueryInputAsync(BatteryHidTransport transport, HidBatteryKind kind, byte field, CancellationToken token)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await transport.WriteAsync(HidBatteryProtocol.Query(kind, 1, field), kind is HidBatteryKind.Mchose or HidBatteryKind.AtkMouse, token).ConfigureAwait(false);
            var deadline = Environment.TickCount64 + 1200;
            while (Environment.TickCount64 < deadline)
            {
                var frame = await transport.ReadAsync((int)Math.Min(200, deadline - Environment.TickCount64), token).ConfigureAwait(false);
                if (frame is not null && HidBatteryProtocol.Parse(kind, frame, 1, field) is { } value) return value;
            }
            await Task.Delay(120, token).ConfigureAwait(false);
        }
        return null;
    }
    private static async Task<HidBatteryValue?> QueryFeatureAsync(BatteryHidTransport transport, Target target, byte sequence, byte field, CancellationToken token)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await transport.SetFeatureAsync(HidBatteryProtocol.Query(target.Kind, sequence, field), token).ConfigureAwait(false);
            await Task.Delay(target.Kind == HidBatteryKind.Inphic9 ? 10 : 30, token).ConfigureAwait(false);
            for (var response = 0; response < 3; response++)
            {
                var frame = await transport.GetFeatureAsync(target.Kind == HidBatteryKind.Inphic9 ? (byte)0x0C : (byte)0, target.Endpoint.FeatureLength, token).ConfigureAwait(false);
                if (frame is not null && HidBatteryProtocol.Parse(target.Kind, frame, sequence, field) is { } value) return value;
                await Task.Delay(30, token).ConfigureAwait(false);
            }
        }
        return null;
    }
    private static async Task<byte[]?> LogitechCallAsync(BatteryHidTransport transport, byte slot, byte feature, byte function, byte[] parameters, CancellationToken token)
    {
        var request = new byte[20]; request[0] = 0x11; request[1] = slot; request[2] = feature; request[3] = (byte)((function << 4) | 0x0E);
        parameters.CopyTo(request, 4);
        await transport.WriteAsync(request, false, token).ConfigureAwait(false);
        var deadline = Environment.TickCount64 + 450;
        while (Environment.TickCount64 < deadline)
        {
            var frame = await transport.ReadAsync((int)Math.Min(100, deadline - Environment.TickCount64), token).ConfigureAwait(false);
            if (frame is null) continue;
            if (HidBatteryProtocol.MatchesLogitech(frame, slot, feature, request[3])) return frame;
            if (frame.Length >= 6 && frame[0] == 0x11 && frame[1] == slot && frame[2] == 0xFF && frame[3] == feature && frame[4] == request[3]) return null;
        }
        return null;
    }
    private static async Task<byte?> GetFeatureAsync(BatteryHidTransport transport, byte slot, ushort feature, CancellationToken token)
    {
        var frame = await LogitechCallAsync(transport, slot, 0, 0, [(byte)(feature >> 8), (byte)feature], token).ConfigureAwait(false);
        return frame is not null && frame[4] != 0 ? frame[4] : null;
    }
    private static async Task<string?> GetLogitechNameAsync(BatteryHidTransport transport, byte slot, CancellationToken token)
    {
        var feature = await GetFeatureAsync(transport, slot, 5, token).ConfigureAwait(false);
        if (feature is null) return null;
        var lengthReply = await LogitechCallAsync(transport, slot, feature.Value, 0, [], token).ConfigureAwait(false);
        if (lengthReply is null || lengthReply[4] is 0 or > 64) return null;
        var length = lengthReply[4]; var text = new List<byte>();
        for (byte offset = 0; offset < length; offset += 16)
        {
            var part = await LogitechCallAsync(transport, slot, feature.Value, 1, [offset], token).ConfigureAwait(false);
            if (part is null) return null;
            text.AddRange(part.Skip(4).Take(Math.Min(16, length - offset)));
        }
        return System.Text.Encoding.UTF8.GetString(text.ToArray()).TrimEnd('\0');
    }
    private async Task<BluetoothBatterySnapshot> ReadPassiveAsync(BluetoothBatteryDevice device, string selectedId, Target target, CancellationToken token)
    {
        if (_passive.TryGetValue(device.Id, out var existing) && existing.Loop.IsCompleted) await ReleaseAsync(device.Id).ConfigureAwait(false);
        if (!_passive.TryGetValue(device.Id, out var session))
        {
            session = new(); _passive.Add(device.Id, session);
            session.Loop = ListenAsync(device, target, session);
        }
        if (Volatile.Read(ref session.Snapshot) is null)
            try { await session.FirstReading.Task.WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false); }
            catch (TimeoutException) { }
        token.ThrowIfCancellationRequested();
        var snapshot = Volatile.Read(ref session.Snapshot);
        if (snapshot is null) return SnapshotFor(device, selectedId, null);
        var online = DateTimeOffset.UtcNow - snapshot.UpdatedAt < TimeSpan.FromSeconds(8);
        return snapshot with { DeviceId = selectedId, IsConnected = online, ReadSucceeded = online };
    }
    private async Task ListenAsync(BluetoothBatteryDevice device, Target target, PassiveSession session)
    {
        var token = session.Cancellation.Token;
        try
        {
            using var transport = new BatteryHidTransport(target.Endpoint, 0x80000000);
            while (!token.IsCancellationRequested)
            {
                var frame = await transport.ReadAsync(1000, token).ConfigureAwait(false);
                if (frame is null) continue;
                var value = HidBatteryProtocol.Parse(HidBatteryKind.Inphic10, frame, target.Slot);
                if (value is null) continue;
                Volatile.Write(ref session.Snapshot, SnapshotFor(device, device.Id, value));
                session.FirstReading.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { log.Warn("Battery", "INPHIC 被动监听已停止", error); }
        finally { session.FirstReading.TrySetResult(); }
    }
    public async Task ReleaseAsync(string deviceId)
    {
        if (!_passive.Remove(deviceId, out var session)) return;
        session.Cancellation.Cancel();
        await session.Loop.ConfigureAwait(false);
        session.Cancellation.Dispose();
    }
}
