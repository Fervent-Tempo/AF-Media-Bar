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


/// <summary>SMTC 应用来源允许列表。 / Allow-list for SMTC application sources.</summary>
public readonly record struct SmtcSourceFilterSettings(bool Enabled, IReadOnlyList<string>? AllowedSourceIds)
{
    public static SmtcSourceFilterSettings Default { get; } = new(false, []);

    public SmtcSourceFilterSettings Normalize() => this with
    {
        AllowedSourceIds = (AllowedSourceIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray()
    };
}
