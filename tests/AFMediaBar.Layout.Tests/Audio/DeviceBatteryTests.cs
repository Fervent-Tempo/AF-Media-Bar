// 只回放电量帧、状态及会话边界，不连接真实设备；硬件兼容性由用户验收。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class DeviceBatteryTests
{
    [TestMethod]
    public void KnownFramesRejectShortCorruptAndOutOfRangeReplies()
    {
        var mchose = Convert.FromHexString("134A0100026401000000000000000000000000C5");
        var atk = Convert.FromHexString("080400000002640010DD000000000000F6");
        var rog = new byte[64]; rog[0] = 2; rog[1] = 0x12; rog[2] = 1; rog[6] = 48;
        var headset = new byte[64]; headset[0] = 2; headset[2] = 0x50; headset[3] = 0x49; headset[12] = 0x21; headset[13] = 1; headset[14] = 1; headset[15] = 75;
        var n9 = new byte[64]; n9[0] = 0x55; n9[1] = 0x65; n9[2] = 89; n9[3] = 3;
        var in9 = new byte[33]; in9[0] = 0x0C; in9[1] = 1; in9[2] = 0x20; in9[4] = 1; in9[6] = 1; in9[0x12] = 63; in9[0x13] = 2; in9[0x15] = 1;
        var in10 = Convert.FromHexString("0398400164");
        foreach (var (kind, frame, percent) in new[]
        {
            (HidBatteryKind.Mchose, mchose, 100), (HidBatteryKind.AtkMouse, atk, 100), (HidBatteryKind.Rog, rog, 48),
            (HidBatteryKind.RazerHeadset, headset, 75), (HidBatteryKind.AtkHeadset, n9, 89),
            (HidBatteryKind.Inphic9, in9, 63), (HidBatteryKind.Inphic10, in10, 100)
        })
        {
            Assert.AreEqual(percent, HidBatteryProtocol.Parse(kind, frame)?.Percent, kind.ToString());
            Assert.IsNull(HidBatteryProtocol.Parse(kind, frame.AsSpan(0, frame.Length - 1)), kind.ToString());
            var corrupt = (byte[])frame.Clone(); corrupt[0] ^= 0x80;
            Assert.IsNull(HidBatteryProtocol.Parse(kind, corrupt), kind.ToString());
        }
        mchose[5] = 255; mchose[19] = HidBatteryProtocol.Sum(mchose.AsSpan(0, 19));
        Assert.IsNull(HidBatteryProtocol.Parse(HidBatteryKind.Mchose, mchose));
        atk[16] ^= 1; Assert.IsNull(HidBatteryProtocol.Parse(HidBatteryKind.AtkMouse, atk));
        Assert.IsNull(HidBatteryProtocol.Parse(HidBatteryKind.Inphic10, in10, 2));
        in9[4] = 2; Assert.IsNull(HidBatteryProtocol.Parse(HidBatteryKind.Inphic9, in9));
        n9[3] = 0x1A;
        Assert.AreEqual(new HidBatteryValue(89, null), HidBatteryProtocol.Parse(HidBatteryKind.AtkHeadset, n9));
    }
    [TestMethod]
    public void RazerUsesFullByteRangeAndMatchesTransaction()
    {
        foreach (var (raw, expected) in new[] { (0, 0), (64, 25), (100, 39), (128, 50), (255, 100) })
        {
            var frame = HidBatteryProtocol.Query(HidBatteryKind.RazerMouse, 9, 0x80);
            frame[1] = 2; frame[10] = (byte)raw; frame[89] = 0;
            for (var i = 3; i < 89; i++) frame[89] ^= frame[i];
            Assert.AreEqual(expected, HidBatteryProtocol.Parse(HidBatteryKind.RazerMouse, frame, 9, 0x80)?.Percent);
            Assert.IsNull(HidBatteryProtocol.Parse(HidBatteryKind.RazerMouse, frame, 8, 0x80));
            frame[89] ^= 1; Assert.IsNull(HidBatteryProtocol.Parse(HidBatteryKind.RazerMouse, frame, 9, 0x80));
        }
    }
    [TestMethod]
    public void LogitechKeepsSlotsFeaturesAndSoftwareIdsSeparate()
    {
        var frame = new byte[20]; frame[0] = 0x11; frame[1] = 2; frame[2] = 6; frame[3] = 0x1E; frame[4] = 97; frame[5] = 8; frame[6] = 0;
        Assert.AreEqual(new HidBatteryValue(97, false), HidBatteryProtocol.ParseLogitech(frame, 2, 6, true));
        Assert.IsNull(HidBatteryProtocol.ParseLogitech(frame, 1, 6, true));
        Assert.IsNull(HidBatteryProtocol.ParseLogitech(frame, 2, 7, true));
        frame[3] = 0x1D; Assert.IsNull(HidBatteryProtocol.ParseLogitech(frame, 2, 6, true));
        frame[3] = 0x1E; frame[4] = 255; Assert.IsNull(HidBatteryProtocol.ParseLogitech(frame, 2, 6, true));
        var endpoint = new BatteryHidEndpoint("test", 0x373B, 0x1262, 0xFF90, 1, 64, 64, 0, "ATK", "", null, false);
        Assert.AreEqual(HidBatteryKind.AtkHeadset, HidBatteryProvider.Match(endpoint));
        Assert.IsNull(HidBatteryProvider.Match(endpoint with { UsagePage = 0xFF82 }));
        Assert.IsNull(HidBatteryProvider.Match(endpoint with { UsagePage = 1, Usage = 2 }));
    }
    [TestMethod]
    public void NativeFeatureLengthsAndRepeatedSerialsKeepTheirBoundaries()
    {
        Assert.AreEqual(91, BatteryHidTransport.FeatureResultLength(90, 91, 0));
        Assert.AreEqual(33, BatteryHidTransport.FeatureResultLength(33, 33, 0x0C));
        Assert.AreEqual(0, BatteryHidTransport.FeatureResultLength(0, 91, 0));
        Assert.AreEqual(12, BatteryHidTransport.FeatureResultLength(12, 33, 0x0C));
        var endpoint = new BatteryHidEndpoint("a", 0x046D, 0xC52B, 0xFF00, 2, 20, 20, 0, "receiver", "0000", Guid.NewGuid(), false);
        Assert.AreNotEqual(endpoint.PhysicalId, (endpoint with { Path = "b", ContainerId = Guid.NewGuid() }).PhysicalId);
    }
    [TestMethod]
    public void CachedSystemPropertiesAndStaleValuesNeverTriggerAlerts()
    {
        var policy = new BluetoothBatteryAlertPolicy();
        var high = new BluetoothBatterySnapshot("mouse", 90, BluetoothBatterySource.Hid, true, true, DateTimeOffset.UtcNow);
        Assert.IsFalse(policy.Evaluate("mouse", high, true, 20));
        var low = high with { Percent = 10, UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        Assert.IsFalse(low.IsRealtime);
        Assert.IsFalse(policy.Evaluate("mouse", low, true, 20));
        var cached = low with { UpdatedAt = DateTimeOffset.UtcNow, Source = BluetoothBatterySource.WindowsProperty, FreshnessKnown = false };
        Assert.IsFalse(cached.IsRealtime);
        Assert.IsFalse(policy.Evaluate("mouse", cached, true, 20));
        Assert.IsFalse(policy.Evaluate("mouse", high, true, 20));
        Assert.IsTrue(policy.Evaluate("mouse", high with { Percent = 10 }, true, 20));
        Assert.IsFalse(policy.Evaluate("mouse", high with { Percent = 10 }, true, 20));
    }
    [TestMethod]
    public async Task ReaderReleasesOnlyDevicesNoLongerTracked()
    {
        var bluetooth = new Provider("bluetooth"); var hid = new Provider("hid");
        var reader = new DeviceBatteryReader([bluetooth, hid]);
        var devices = await reader.GetDevicesAsync(CancellationToken.None);
        foreach (var device in devices) await reader.ReadAsync(device, device.Id, CancellationToken.None);
        await reader.ReleaseExceptAsync(new HashSet<string> { "hid" });
        CollectionAssert.AreEqual(new[] { "bluetooth" }, bluetooth.Released);
        Assert.AreEqual(0, hid.Released.Count);
        await reader.ReleaseExceptAsync(new HashSet<string>());
        CollectionAssert.AreEqual(new[] { "hid" }, hid.Released);
        await reader.ReleaseExceptAsync(new HashSet<string>());
        Assert.AreEqual(1, hid.Released.Count);
    }
    [TestMethod]
    public async Task DelayedReadingCannotPublishAfterCancellationOrTargetChange()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var requestVersion = 1; var currentVersion = 1;
        async Task<bool> CompleteAsync() { await completed.Task; return BluetoothBatteryService.CanPublish(requestVersion, cancellation.Token, currentVersion, false); }
        var pending = CompleteAsync(); currentVersion = 2; cancellation.Cancel(); completed.SetResult();
        Assert.IsFalse(await pending);
        Assert.IsFalse(BluetoothBatteryService.CanPublish(1, CancellationToken.None, 2, false));
        Assert.IsFalse(BluetoothBatteryService.CanPublish(2, CancellationToken.None, 2, true));
        Assert.IsTrue(BluetoothBatteryService.CanPublish(2, CancellationToken.None, 2, false));
    }
    private sealed class Provider(string id) : IDeviceBatteryProvider
    {
        public string Id => id;
        public List<string> Released { get; } = [];
        public Task<IReadOnlyList<BluetoothBatteryDevice>> GetDevicesAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<BluetoothBatteryDevice>>([new(id, id, null, "", []) { ProviderId = id, IsPresent = true }]);
        public Task<BluetoothBatterySnapshot> ReadAsync(BluetoothBatteryDevice device, string selectedId, CancellationToken token) =>
            Task.FromResult(new BluetoothBatterySnapshot(selectedId, 50, BluetoothBatterySource.Hid, true, true, DateTimeOffset.UtcNow));
        public Task ReleaseAsync(string deviceId) { Released.Add(deviceId); return Task.CompletedTask; }
    }
}
