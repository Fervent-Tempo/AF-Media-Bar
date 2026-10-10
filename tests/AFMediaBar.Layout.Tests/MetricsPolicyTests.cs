// 验证差分基线、接口变化和格式边界；不联网，也不修改真实网卡或系统性能设置。
using System.Globalization;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>性能差分和单位格式的回归验证。</summary>
[TestClass]
public sealed class MetricsPolicyTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("NativeSmoke")]
    public async Task NativeReaderBuildsANetworkBaselineAndClearsUnusedDemand()
    {
        nint table = 0;
        try
        {
            Assert.AreEqual(0u, NativeMethods.GetIfTable2(out table), "Read-only IP Helper enumeration failed.");
            Assert.AreNotEqual(nint.Zero, table);
            TestContext.WriteLine($"IP Helper interfaces: {Marshal.ReadInt32(table)}");
        }
        finally
        {
            if (table != 0)
                NativeMethods.FreeMibTable(table);
        }

        using var sampler = new SystemMetricsService();
        sampler.SetDemand([AFMediaBar.Classes.Models.MetricKind.SystemNetwork]);
        var first = sampler.Sample();
        Assert.IsNull(first.NetworkUploadBytesPerSecond);
        Assert.IsNull(first.NetworkDownloadBytesPerSecond);
        await Task.Delay(250);
        var second = sampler.Sample();
        Assert.IsNotNull(second.NetworkUploadBytesPerSecond);
        Assert.IsNotNull(second.NetworkDownloadBytesPerSecond);
        Assert.IsTrue(double.IsFinite(second.NetworkUploadBytesPerSecond.Value));
        Assert.IsTrue(double.IsFinite(second.NetworkDownloadBytesPerSecond.Value));
        Assert.IsNull(second.SystemCpuPercent);
        Assert.IsNull(second.SystemGpuPercent);
        Assert.IsNull(second.ProcessMemoryMegabytes);
        TestContext.WriteLine($"Physical-adapter rates: up={second.NetworkUploadBytesPerSecond}, down={second.NetworkDownloadBytesPerSecond} B/s");
        sampler.SetDemand([]);
        Assert.AreEqual(default, sampler.Sample());
        sampler.SetDemand([AFMediaBar.Classes.Models.MetricKind.SystemNetwork]);
        Assert.IsNull(sampler.Sample().NetworkUploadBytesPerSecond);
        sampler.Dispose();
        Assert.AreEqual(default, sampler.Sample());
    }

    [TestMethod]
    public void NetworkRatesSumComparableAdaptersUsingActualElapsedTime()
    {
        var rates = new NetworkRatePolicy();
        Assert.AreEqual((null, null), rates.Sample([new(1, 100, 200), new(2, 1000, 2000)], 1));
        var result = rates.Sample([new(1, 300, 600), new(2, 1600, 3200)], 2);
        Assert.AreEqual(400d, result.Upload);
        Assert.AreEqual(800d, result.Download);
    }

    [TestMethod]
    public void AdapterChangesAndResetsDoNotIncludeHistoricalTotals()
    {
        var rates = new NetworkRatePolicy();
        rates.Sample([new(1, 100, 100), new(2, 1000, 1000)], 1);
        var added = rates.Sample([new(1, 120, 130), new(3, 900000, 800000)], 1);
        Assert.AreEqual(20d, added.Upload);
        Assert.AreEqual(30d, added.Download);
        var reset = rates.Sample([new(1, 5, 10), new(3, 900040, 800050)], 1);
        Assert.AreEqual(40d, reset.Upload);
        Assert.AreEqual(50d, reset.Download);
        Assert.AreEqual((0d, 0d), rates.Sample([new(1, 5, 10), new(3, 900040, 800050)], 1));
        Assert.AreEqual((null, null), rates.Sample([new(4, 1000000, 1000000)], 1));
    }

    [TestMethod]
    public void ReadFailuresAndInvalidIntervalsRequireANewBaseline()
    {
        var rates = new NetworkRatePolicy();
        rates.Sample([new(1, 100, 100)], 1);
        Assert.AreEqual((null, null), rates.Sample(null, 1));
        Assert.AreEqual((null, null), rates.Sample([new(1, 1000, 1000)], 1));
        Assert.AreEqual((0d, 0d), rates.Sample([new(1, 1000, 1000)], 1));
        Assert.AreEqual((null, null), rates.Sample([new(1, 2000, 2000)], 0));
        Assert.AreEqual((null, null), rates.Sample([new(1, 3000, 3000)], 1));
        rates.Reset();
        Assert.AreEqual((null, null), rates.Sample([], 1));
        Assert.AreEqual((0d, 0d), rates.Sample([], 1));
    }

    [TestMethod]
    public void InterfaceSelectionExcludesVirtualFiltersLoopbackAndDisconnectedRows()
    {
        Assert.IsTrue(NetworkRatePolicy.IsEligibleInterface(1, 6, 1));
        Assert.IsTrue(NetworkRatePolicy.IsEligibleInterface(1, 71, 1));
        Assert.IsFalse(NetworkRatePolicy.IsEligibleInterface(2, 6, 1));
        Assert.IsFalse(NetworkRatePolicy.IsEligibleInterface(1, 6, 0));
        Assert.IsFalse(NetworkRatePolicy.IsEligibleInterface(1, 6, 3));
        Assert.IsFalse(NetworkRatePolicy.IsEligibleInterface(1, 24, 1));
        Assert.IsFalse(NetworkRatePolicy.IsEligibleInterface(1, 131, 1));
    }

    [TestMethod]
    public void CpuRebasesAfterRollbackAndRejectsImpossibleDeltas()
    {
        var cpu = new CpuUsagePolicy();
        Assert.IsNull(cpu.Sample(100, 200, 100));
        Assert.AreEqual(50, cpu.Sample(150, 250, 150));
        Assert.IsNull(cpu.Sample(1, 2, 1));
        Assert.AreEqual(50, cpu.Sample(51, 52, 51));
        Assert.IsNull(cpu.Sample(151, 53, 52));
        cpu.Reset();
        Assert.IsNull(cpu.Sample(200, 300, 200));
    }

    [TestMethod]
    public void RateFormattingUsesBinaryUnitsAndPreservesUnavailableValues()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.AreEqual("—", MetricPresentationPolicy.FormatRate(null));
            Assert.AreEqual("—", MetricPresentationPolicy.FormatRate(double.NaN));
            Assert.AreEqual("—", MetricPresentationPolicy.FormatRate(-1));
            Assert.AreEqual("0 B/s", MetricPresentationPolicy.FormatRate(0));
            Assert.AreEqual("1.0 KiB/s", MetricPresentationPolicy.FormatRate(1024));
            Assert.AreEqual("1.0 MiB/s", MetricPresentationPolicy.FormatRate(1024 * 1024 - 1));
            Assert.AreEqual("1.0 GiB/s", MetricPresentationPolicy.FormatRate(1024d * 1024 * 1024));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [TestMethod]
    public void NativeCounterLayoutMatchesWindowsAbi()
    {
        Assert.AreEqual(1352, Marshal.SizeOf<NativeMethods.MibIfRow2>());
        Assert.AreEqual(8, (int)Marshal.OffsetOf<NativeMethods.MibIfTable2>(nameof(NativeMethods.MibIfTable2.FirstRow)));
        Assert.AreEqual(1208, (int)Marshal.OffsetOf<NativeMethods.MibIfRow2>(nameof(NativeMethods.MibIfRow2.InOctets)));
        Assert.AreEqual(1280, (int)Marshal.OffsetOf<NativeMethods.MibIfRow2>(nameof(NativeMethods.MibIfRow2.OutOctets)));
    }
}
