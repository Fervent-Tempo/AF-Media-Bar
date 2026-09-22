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
/// 采集目标是"正在出声的那个设备"，而不是"系统默认设备"：虚拟声卡（SteelSeries Sonar 等）会把不同应用路由到不同端点，
/// 系统默认端点可能完全没有声音——只认默认端点时，频谱会一直停在静音。选择规则：
/// 先取可听等级最高的那一档（应用出声 &gt; 仅系统混音出声）；同档内优先保持当前采集的端点（避免来回切换），
/// 其次是默认端点，最后取峰值最大的端点；整档都不可听时沿用当前端点，没有当前端点则回退默认端点。
/// Target-device selection for the spectrum capture.
///
/// The target is the device that is actually audible, not the system default one: virtual audio drivers (SteelSeries Sonar and
/// friends) route different applications to different endpoints, and the default endpoint may carry no sound at all — capturing only
/// the default would leave the spectrum silent. The rules: prefer the highest audibility rank (an application playing beats only the
/// system mixer playing); within that rank keep the currently captured endpoint first (which avoids switching back and forth), then
/// the default endpoint, then the loudest one; when nothing is audible, keep the current endpoint, or fall back to the default.
/// </summary>
public static class AudioCaptureDevicePolicy
{
    /// <summary>没有可听会话。/ No audible session.</summary>
    public const int RankSilent = 0;

    /// <summary>只有系统混音进程（audiodg）的会话在出声，通常是虚拟声卡送到物理端点的回声。/ Only the system mixer process (audiodg) is audible, usually the echo a virtual driver sends to a physical endpoint.</summary>
    public const int RankSystemOnly = 1;

    /// <summary>有普通应用的会话在出声。/ An ordinary application's session is audible.</summary>
    public const int RankApplication = 2;

    /// <summary>
    /// 按候选端点的可听状态选出本次采集应使用的端点；返回 null 表示连回退端点都没有。
    /// Selects the endpoint this capture should use from the candidates' audibility; null means not even a fallback exists.
    /// </summary>
    /// <param name="currentDeviceId">当前正在采集的端点，可能为空。/ The endpoint currently captured, possibly empty.</param>
    /// <param name="defaultDeviceId">系统默认端点，可能为空。/ The system default endpoint, possibly empty.</param>
    /// <param name="candidates">本次枚举到的可听端点。/ The audible endpoints enumerated this time.</param>
    public static string? SelectTarget(
        string? currentDeviceId,
        string? defaultDeviceId,
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
            // 整档都不可听：保持当前端点（没有就回退默认），避免"没有声音"时反复切换采集。
            // Nothing audible at all: keep the current endpoint (or fall back to the default) so silence never causes capture churn.
            return !string.IsNullOrEmpty(currentDeviceId) ? currentDeviceId : defaultDeviceId;
        }

        if (currentRank == bestRank)
        {
            return currentDeviceId;
        }

        if (defaultRank == bestRank)
        {
            return defaultDeviceId;
        }

        return loudestId ?? (!string.IsNullOrEmpty(currentDeviceId) ? currentDeviceId : defaultDeviceId);
    }

    private static bool SameDevice(string left, string? right) =>
        !string.IsNullOrEmpty(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
