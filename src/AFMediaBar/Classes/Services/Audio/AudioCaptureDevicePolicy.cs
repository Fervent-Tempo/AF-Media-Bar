using AFMediaBar.Classes.Abstractions;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 一个候选输出端点的可听状态。
/// The audibility state of one candidate render endpoint.
/// </summary>
/// <param name="DeviceId">端点标识（Core Audio 的端点 ID）。/ Endpoint identifier (the Core Audio endpoint ID).</param>
/// <param name="Rank">可听等级，见 <see cref="AudioCaptureDevicePolicy"/> 的 Rank 常量。/ Audibility rank; see the Rank constants on <see cref="AudioCaptureDevicePolicy"/>.</param>
/// <param name="Peak">该端点当前最大的会话峰值（0–1）。/ Largest current session peak on this endpoint (0–1).</param>
public readonly record struct AudioEndpointAudibility(string DeviceId, int Rank, float Peak);

/// <summary>
/// 频谱采集的目标设备选择。
///
/// 候选仅包含 AF 当前媒体来源的音频会话。优先出声端点，其次是已匹配但静音的端点；
/// 同档优先当前端点、默认端点，再按峰值选择。会话暂时消失时保留仍连接的端点，否则回退系统默认。
/// </summary>
public static class AudioCaptureDevicePolicy
{
    /// <summary>没有可听会话。/ No audible session.</summary>
    public const int RankSilent = 0;

    /// <summary>已匹配媒体来源，但会话暂停或当前静音。</summary>
    public const int RankMatchedSilent = 1;

    /// <summary>已匹配媒体来源，且会话正在出声。</summary>
    public const int RankApplication = 2;

    /// <summary>正常档位的扫描间隔：两秒一次端点与会话枚举，毫秒级成本，足以在"出声设备变化"后迅速跟上。
    /// Scan interval at the normal level: one endpoint and session enumeration every two seconds, costing milliseconds and following
    /// a change of the audible device promptly.</summary>
    public static readonly TimeSpan NormalScanInterval = TimeSpan.FromSeconds(2);

    /// <summary>空闲档位的扫描间隔：用户已离开，慢一点没有代价。</summary>
    /// <summary>Scan interval at the idle level: the user is gone, so a slower scan costs nothing.</summary>
    public static readonly TimeSpan IdleScanInterval = TimeSpan.FromSeconds(10);

    /// <summary>息屏/睡眠档位的唤醒间隔：只唤醒观察档位，不做任何枚举。</summary>
    /// <summary>Wake interval at the display-off or suspend level: the loop only wakes to observe the level and enumerates nothing.</summary>
    public static readonly TimeSpan PausedScanInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 仅在频谱最近仍被取样、且剪枝档位允许时枚举；隐藏频谱与息屏时都不需要端点解析。
    /// Enumerates only while spectrum sampling is recently requested and the prune level allows it; neither a hidden spectrum nor
    /// a dark display needs endpoint resolution.
    /// </summary>
    /// <param name="level">当前剪枝档位。/ The current prune level.</param>
    /// <param name="hasRecentDemand">频谱是否正在取样。/ Whether spectrum samples are currently requested.</param>
    public static bool ShouldScan(MemoryPruneLevel level, bool hasRecentDemand) =>
        hasRecentDemand && level < MemoryPruneLevel.DisplayOff;

    /// <summary>按剪枝档位取扫描间隔（息屏/睡眠用唤醒间隔）。/ Resolves the scan interval for a prune level (display-off and suspend use the wake interval).</summary>
    /// <param name="level">当前剪枝档位。/ The current prune level.</param>
    public static TimeSpan ResolveScanInterval(MemoryPruneLevel level) => level switch
    {
        >= MemoryPruneLevel.DisplayOff => PausedScanInterval,
        MemoryPruneLevel.Idle => IdleScanInterval,
        _ => NormalScanInterval
    };

    /// <summary>
    /// 按候选端点的可听状态选出本次采集应使用的端点；返回 null 表示连回退端点都没有。
    /// Selects the endpoint this capture should use from the candidates' audibility; null means not even a fallback exists.
    /// </summary>
    /// <param name="currentDeviceId">当前正在采集的端点，可能为空。/ The endpoint currently captured, possibly empty.</param>
    /// <param name="defaultDeviceId">系统默认端点，可能为空。/ The system default endpoint, possibly empty.</param>
    /// <param name="activeDeviceIds">本次枚举到的所有活动端点，包括静音端点。/ Every active endpoint found, including silent ones.</param>
    /// <param name="candidates">本次枚举到的可听端点。/ The audible endpoints enumerated this time.</param>
    public static string? SelectTarget(
        string? currentDeviceId,
        string? defaultDeviceId,
        IReadOnlyCollection<string> activeDeviceIds,
        IReadOnlyList<AudioEndpointAudibility> candidates)
    {
        var currentRank = RankSilent;
        var defaultRank = RankSilent;
        var bestRank = RankSilent;
        string? loudestId = null;
        var loudestPeak = 0f;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate.DeviceId) || candidate.Rank <= RankSilent)
            {
                continue;
            }

            if (SameDevice(candidate.DeviceId, currentDeviceId))
            {
                currentRank = Math.Max(currentRank, candidate.Rank);
            }

            if (SameDevice(candidate.DeviceId, defaultDeviceId))
            {
                defaultRank = Math.Max(defaultRank, candidate.Rank);
            }

            if (candidate.Rank > bestRank || candidate.Rank == bestRank && candidate.Peak > loudestPeak)
            {
                bestRank = candidate.Rank;
                loudestPeak = candidate.Peak;
                loudestId = candidate.DeviceId;
            }
        }

        if (bestRank <= RankSilent)
        {
            // 静音时保持仍然连接的当前端点；设备已拔除时回退默认，避免反复尝试失效 ID。
            // Keep a still-connected endpoint through silence, but fall back after unplugging rather than retrying a stale ID.
            return activeDeviceIds.Any(id => SameDevice(id, currentDeviceId)) ? currentDeviceId : defaultDeviceId;
        }

        if (currentRank == bestRank)
        {
            return currentDeviceId;
        }

        if (defaultRank == bestRank)
        {
            return defaultDeviceId;
        }

        return loudestId ?? defaultDeviceId;
    }

    private static bool SameDevice(string left, string? right) =>
        !string.IsNullOrEmpty(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
