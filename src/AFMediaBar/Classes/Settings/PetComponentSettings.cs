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
    public bool AllowOverflow { get; init; }
    public string Animation { get; init; } = "auto";

    public PetComponentSettings Normalize() => this with
    {
        FillColorHex = ColorHex.TryParse(FillColorHex, out var color) ? ColorHex.Format(color) : DefaultFillColorHex,
        SizeDip = double.IsFinite(SizeDip) ? Math.Clamp(SizeDip, 20, 160) : DefaultSizeDip,
        Animation = PetAnimationCatalog.Ids.Contains(Animation, StringComparer.Ordinal) ? Animation : "auto"
    };
}
