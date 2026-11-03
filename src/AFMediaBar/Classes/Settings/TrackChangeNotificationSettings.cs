// 按功能归组的设置契约；保持既有类型全名、枚举值及默认语义。
using AFMediaBar.Classes.Models;
using System.IO;

namespace AFMediaBar.Classes.Settings;


/// <summary>曲目切换通知在目标工作区中的位置。 / Position of the track-change notification in the target work area.</summary>
public enum TrackChangeNotificationPosition
{
    BottomLeft = 0,
    TopLeft = 1,
    TopCenter = 2,
    TopRight = 3,
    BottomCenter = 4,
    BottomRight = 5
}

/// <summary>曲目切换通知选择显示器的方式。 / Method used to select the display for track-change notifications.</summary>
public enum NotificationTargetMode
{
    Fixed = 0,
    ForegroundWindow = 1
}

/// <summary>曲目切换通知的用户设置。 / User settings for track-change notifications.</summary>
public readonly record struct TrackChangeNotificationSettings(
    bool Enabled,
    bool ShowWhenFullscreen,
    int DurationMilliseconds,
    TrackChangeNotificationPosition Position,
    NotificationTargetMode TargetMode,
    string? FixedMonitorDeviceId)
{
    public TrackChangeNotificationSettings() : this(false, false, 0, default, default, null) { }
    public const int MaximumOffsetDip = 4096;
    public const int MinimumOffsetDip = -MaximumOffsetDip;
    public const int MinimumFontSize = 8;
    public const int MaximumFontSize = 40;
    public double HorizontalOffsetDip { get; init; }
    public double VerticalOffsetDip { get; init; }
    public int TitleFontSize { get; init; } = 15;
    public int ArtistFontSize { get; init; } = 12;
    public string? TitleColor { get; init; }
    public string? ArtistColor { get; init; }

    /// <summary>Minimum track notification duration stored in milliseconds.</summary>
    public const int MinimumDurationMilliseconds = 1000;
    /// <summary>Maximum track notification duration stored in milliseconds.</summary>
    public const int MaximumDurationMilliseconds = 10000;
    /// <summary>通知的默认设置。 / Default notification settings.</summary>
    public static TrackChangeNotificationSettings Default { get; } = new(
        true,
        true,
        3000,
        TrackChangeNotificationPosition.TopCenter,
        NotificationTargetMode.ForegroundWindow,
        null);

    /// <summary>归一化枚举、时长和设备标识。 / Normalizes enums, duration, and the device identifier.</summary>
    public TrackChangeNotificationSettings Normalize()
    {
        var defaults = Default;
        return this with
        {
            DurationMilliseconds = Math.Clamp(DurationMilliseconds, MinimumDurationMilliseconds, MaximumDurationMilliseconds),
            Position = Enum.IsDefined(Position) ? Position : defaults.Position,
            HorizontalOffsetDip = double.IsFinite(HorizontalOffsetDip) ? Math.Clamp(HorizontalOffsetDip, -MaximumOffsetDip, MaximumOffsetDip) : 0,
            VerticalOffsetDip = double.IsFinite(VerticalOffsetDip) ? Math.Clamp(VerticalOffsetDip, -MaximumOffsetDip, MaximumOffsetDip) : 0,
            TitleFontSize = Math.Clamp(TitleFontSize, MinimumFontSize, MaximumFontSize),
            ArtistFontSize = Math.Clamp(ArtistFontSize, MinimumFontSize, MaximumFontSize),
            TitleColor = Classes.Utils.ColorHex.TryParse(TitleColor, out var title) ? Classes.Utils.ColorHex.Format(title) : null,
            ArtistColor = Classes.Utils.ColorHex.TryParse(ArtistColor, out var artist) ? Classes.Utils.ColorHex.Format(artist) : null,
            TargetMode = Enum.IsDefined(TargetMode) ? TargetMode : defaults.TargetMode,
            FixedMonitorDeviceId = string.IsNullOrWhiteSpace(FixedMonitorDeviceId)
                ? null
                : FixedMonitorDeviceId.Trim()
        };
    }
}
