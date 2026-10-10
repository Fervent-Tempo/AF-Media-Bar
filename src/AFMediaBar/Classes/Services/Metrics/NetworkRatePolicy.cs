// 按接口身份计算流量差分；只处理数据，原生枚举与时间来源由采样服务提供。
namespace AFMediaBar.Classes.Services;

/// <summary>一个已连接物理接口的累计流量。</summary>
public readonly record struct NetworkInterfaceCounters(ulong InterfaceId, ulong BytesSent, ulong BytesReceived);

/// <summary>按接口建立基线，避免设备变化和计数重置产生虚假峰值。</summary>
public sealed class NetworkRatePolicy
{
    private readonly Dictionary<ulong, NetworkInterfaceCounters> _previous = [];
    private bool _hasBaseline;

    public static bool IsEligibleInterface(uint operationalStatus, uint type, byte flags) =>
        operationalStatus == 1 && type is not 24 and not 131 && (flags & 3) == 1;

    public (double? Upload, double? Download) Sample(IReadOnlyCollection<NetworkInterfaceCounters>? interfaces, double elapsedSeconds)
    {
        if (interfaces is null || !double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
        {
            Reset();
            return (null, null);
        }

        double sent = 0;
        double received = 0;
        var comparable = interfaces.Count == 0;
        foreach (var item in interfaces)
        {
            if (!_previous.TryGetValue(item.InterfaceId, out var previous) ||
                item.BytesSent < previous.BytesSent || item.BytesReceived < previous.BytesReceived)
                continue;
            comparable = true;
            sent += item.BytesSent - previous.BytesSent;
            received += item.BytesReceived - previous.BytesReceived;
        }

        var valid = _hasBaseline && comparable;
        _previous.Clear();
        foreach (var item in interfaces)
            _previous[item.InterfaceId] = item;
        _hasBaseline = true;
        return valid ? (sent / elapsedSeconds, received / elapsedSeconds) : (null, null);
    }

    public void Reset()
    {
        _previous.Clear();
        _hasBaseline = false;
    }
}
