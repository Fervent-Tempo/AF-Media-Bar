// 酷狗已验证构建表：PE 指纹优先，旧构建保留模块文件版本回退。
using System.Diagnostics;
using AFMediaBar.Classes.Services.Media.Readers;

namespace AFMediaBar.Classes.Services.Media.Sources.KuGou;

/// <summary>酷狗一个已验证版本的内存布局。 / Memory layout of one verified KuGou build.</summary>
internal sealed record KuGouMemoryLayout(
    nint PositionOffset,
    nint DurationRootOffset,
    nint[] DurationOffsets,
    nint GraphRootOffset,
    int DurationUnitsPerSecond = 1);

/// <summary>构建指纹或旧文件版本二选一的匹配项。 / A profile matched by build fingerprint or legacy file version.</summary>
internal sealed record KuGouBuildProfile(
    KuGouMemoryLayout Layout,
    PeBuildFingerprint PlayerFingerprint = default,
    PeBuildFingerprint DurationFingerprint = default,
    string? LegacyPlayerVersion = null,
    string? LegacyDurationVersion = null)
{
    internal bool Matches(PeBuildFingerprint player, PeBuildFingerprint duration) =>
        PlayerFingerprint != default && DurationFingerprint != default &&
        PlayerFingerprint == player && DurationFingerprint == duration;

    internal bool MatchesLegacy(string playerVersion, string durationVersion) =>
        LegacyPlayerVersion is not null && LegacyDurationVersion is not null &&
        string.Equals(LegacyPlayerVersion, playerVersion, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(LegacyDurationVersion, durationVersion, StringComparison.OrdinalIgnoreCase);
}

/// <summary>酷狗构建表；未知构建绝不猜测偏移。</summary>
internal static class KuGouBuildProfiles
{
    internal static readonly KuGouMemoryLayout LegacyFallback = new(
        0x3929D8, 0x023C9B68, [0x70, 0x30, 0x0, 0x24], 0x3936A8);

    private static readonly KuGouBuildProfile Legacy27972 = new(
        LegacyFallback,
        LegacyPlayerVersion: "1.2.0.519",
        LegacyDurationVersion: "20.1.51.27972");

    private static readonly KuGouBuildProfile Legacy27974 = new(
        LegacyFallback with { DurationRootOffset = 0x023C7B68 },
        LegacyPlayerVersion: "1.2.0.519",
        LegacyDurationVersion: "20.1.51.27974");

    private static readonly KuGouBuildProfile Latest28014 = new(
        new KuGouMemoryLayout(0x3949F8, 0x024363D8, [0x30, 0x118], 0x3956C8, 1000),
        PlayerFingerprint: new(0x6AC9EC86, 4587520),
        DurationFingerprint: new(0x6AE2B74C, 39858176),
        LegacyPlayerVersion: "1.2.0.523",
        LegacyDurationVersion: "20.1.61.28014");

    private static readonly KuGouBuildProfile[] Profiles = [Latest28014, Legacy27974, Legacy27972];

    /// <summary>先按 PE 构建指纹匹配，再按旧文件版本匹配；都不匹配时返回 null。</summary>
    internal static KuGouBuildProfile? Match(
        PeBuildFingerprint player,
        PeBuildFingerprint duration,
        FileVersionInfo playerVersion,
        FileVersionInfo durationVersion) =>
        Match(player, duration, VersionText(playerVersion), VersionText(durationVersion));

    internal static KuGouBuildProfile? Match(
        PeBuildFingerprint player,
        PeBuildFingerprint duration,
        string playerVersion,
        string durationVersion)
    {
        foreach (var profile in Profiles)
        {
            if (profile.PlayerFingerprint != default && profile.DurationFingerprint != default)
            {
                // 有构建指纹的条目只认指纹；同文件版本但二进制被替换时必须失败关闭。
                if (profile.Matches(player, duration))
                    return profile;
                continue;
            }

            if (profile.MatchesLegacy(playerVersion, durationVersion))
                return profile;
        }

        return null;
    }

    private static string VersionText(FileVersionInfo version) =>
        $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}";
}
