// 读取已连接物理接口的累计字节数；每次读取后释放原生表，不持有网络事件订阅。
using System.Diagnostics;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

/// <summary>使用单调时钟和 64 位接口计数器采样双向速率。</summary>
public sealed class NetworkUsageSampler
{
    private readonly NetworkRatePolicy _rates = new();
    private long? _previousTimestamp;

    public (double? Upload, double? Download) Sample()
    {
        var interfaces = ReadInterfaces();
        var now = Stopwatch.GetTimestamp();
        var seconds = _previousTimestamp is long previous ? Stopwatch.GetElapsedTime(previous, now).TotalSeconds : 1d;
        _previousTimestamp = interfaces is null ? null : now;
        return _rates.Sample(interfaces, seconds);
    }

    public void Reset()
    {
        _previousTimestamp = null;
        _rates.Reset();
    }

    private static unsafe List<NetworkInterfaceCounters>? ReadInterfaces()
    {
        nint table = 0;
        try
        {
            if (NativeMethods.GetIfTable2(out table) != 0 || table == 0)
                return null;
            var count = *(uint*)table;
            var firstRow = table + (int)Marshal.OffsetOf<NativeMethods.MibIfTable2>(nameof(NativeMethods.MibIfTable2.FirstRow));
            var rows = (NativeMethods.MibIfRow2*)firstRow;
            var result = new List<NetworkInterfaceCounters>();
            for (uint index = 0; index < count; index++)
            {
                ref var row = ref rows[index];
                // HardwareInterface 位由系统提供，不按名称猜测 VPN 或虚拟适配器。
                if (!NetworkRatePolicy.IsEligibleInterface(row.OperStatus, row.Type, row.InterfaceAndOperStatusFlags))
                    continue;
                result.Add(new(row.InterfaceLuid, row.OutOctets, row.InOctets));
            }
            return result;
        }
        finally
        {
            if (table != 0)
                NativeMethods.FreeMibTable(table);
        }
    }
}
