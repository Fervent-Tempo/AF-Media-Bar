// 媒体来源的全局选择偏好；不保存运行时会话标识或播放历史。
namespace AFMediaBar.Classes.Settings;

/// <summary>自动选择媒体来源的策略。</summary>
public enum MediaSelectionMode
{
    Automatic = 0,
    LatestSession = 1,
    PreferredApplication = 2
}

/// <summary>自动选择策略及优先应用的稳定来源标识。</summary>
public sealed record MediaSelectionSettings(MediaSelectionMode Mode = MediaSelectionMode.Automatic, string? PreferredSourceId = null)
{
    public static MediaSelectionSettings Default { get; } = new(MediaSelectionMode.Automatic, null);

    public MediaSelectionSettings Normalize() => new(
        Enum.IsDefined(Mode) ? Mode : MediaSelectionMode.Automatic,
        string.IsNullOrWhiteSpace(PreferredSourceId) ? null : PreferredSourceId.Trim());
}
