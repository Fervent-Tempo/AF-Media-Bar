namespace AFMediaBar.Models;

/// <summary>
/// 表示一次更新检查的最终状态。
/// Represents the final state of an update check.
/// </summary>
public enum UpdateCheckStatus
{
    NotDue,
    AlreadyChecking,
    NoUpdate,
    UpdateAvailable,
    Failed
}

/// <summary>
/// 保存经过校验的版本清单内容；这里只描述更新，不执行下载或安装。
/// Stores validated manifest data; this model describes an update but never downloads or installs it.
/// </summary>
public sealed record UpdateInfo(
    Version Version,
    string VersionText,
    string Title,
    DateTimeOffset? ReleaseDate,
    string? MinimumSupportedVersion,
    bool Mandatory,
    IReadOnlyList<string> Changelog,
    Uri? ReleaseNotesUri,
    IReadOnlyDictionary<string, Uri> DownloadUris,
    IReadOnlyDictionary<string, string> Checksums,
    Uri ManifestUri);

/// <summary>
/// 封装更新检查结果，供启动提示和设置窗口共用。
/// Wraps an update-check result shared by startup notifications and the settings window.
/// </summary>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateInfo? Update,
    Uri? ManifestUri,
    string? ErrorMessage)
{
    public static UpdateCheckResult NotDue { get; } =
        new(UpdateCheckStatus.NotDue, null, null, null);

    public static UpdateCheckResult AlreadyChecking { get; } =
        new(UpdateCheckStatus.AlreadyChecking, null, null, null);

    public static UpdateCheckResult NoUpdate(UpdateInfo release) =>
        new(UpdateCheckStatus.NoUpdate, release, release.ManifestUri, null);

    public static UpdateCheckResult Available(UpdateInfo update) =>
        new(UpdateCheckStatus.UpdateAvailable, update, update.ManifestUri, null);

    public static UpdateCheckResult Failed(string message) =>
        new(UpdateCheckStatus.Failed, null, null, message);
}
