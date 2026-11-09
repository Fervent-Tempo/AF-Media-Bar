// 宠物的共用外观与待机偏好；不保存宿主几何或浏览器状态。
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Settings;

/// <summary>任务栏和悬浮媒体栏共用的宠物设置。</summary>
public sealed record PetComponentSettings
{
    public const string DefaultFillColorHex = "#FFFFFF";
    public const double DefaultSizeDip = 48;
    public string FillColorHex { get; init; } = DefaultFillColorHex;
    public bool IdleAnimationsEnabled { get; init; } = true;
    public double SizeDip { get; init; } = DefaultSizeDip;
    public bool AllowOverflow { get; init; } = true;
    public bool FreePlacement { get; init; }
    public IReadOnlyList<FloatingBarPosition> Positions { get; init; } = [];
    public string? OutfitColorHex { get; init; }
    public string Outfit { get; init; } = "original";
    public bool PettingEnabled { get; init; } = true;
    public bool FollowArtworkColor { get; init; }
    public bool AudioStatusEnabled { get; init; } = true;
    public bool VoiceEnabled { get; init; }
    public double VoiceVolume { get; init; } = 20;
    public bool NotificationsEnabled { get; init; }
    public bool FollowPlayback { get; init; } = true;
    public bool MusicMotionEnabled { get; init; } = true;
    public double MusicSensitivity { get; init; } = 100;
    public string Animation { get; init; } = "auto";

    private static bool IsValidPosition(FloatingBarPosition? p) => p is not null && !string.IsNullOrWhiteSpace(p.MonitorDeviceId)
        && double.IsFinite(p.CenterXDip) && double.IsFinite(p.CenterYDip);

    public PetComponentSettings Normalize() => this with
    {
        FreePlacement = AllowOverflow && FreePlacement,
        Positions = Positions is not { Count: > 0 } ? [] : Positions.All(IsValidPosition) ? Positions : Positions.Where(IsValidPosition).ToArray(),
        OutfitColorHex = ColorHex.TryParse(OutfitColorHex, out var outfitColor) ? ColorHex.Format(outfitColor) : null,
        Outfit = Outfit is "headphones" or "badge" or "spectrum" ? Outfit : "original",
        VoiceVolume = double.IsFinite(VoiceVolume) ? Math.Clamp(VoiceVolume, 0, 100) : 20,
        MusicSensitivity = double.IsFinite(MusicSensitivity) ? Math.Clamp(MusicSensitivity, 25, 200) : 100,
        FillColorHex = ColorHex.TryParse(FillColorHex, out var color) ? ColorHex.Format(color) : DefaultFillColorHex,
        SizeDip = double.IsFinite(SizeDip) ? Math.Clamp(SizeDip, 20, 160) : DefaultSizeDip,
        Animation = PetAnimationCatalog.Ids.Contains(Animation, StringComparer.Ordinal) ? Animation : "auto"
    };
}
