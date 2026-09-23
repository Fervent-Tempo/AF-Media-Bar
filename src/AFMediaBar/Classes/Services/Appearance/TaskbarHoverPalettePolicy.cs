using System.Windows.Media;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 任务栏指针反馈使用的自适应色板。
/// Adaptive palette used by taskbar pointer feedback.
/// </summary>
public readonly record struct TaskbarHoverPalette(
    Color Foreground,
    Color Surface,
    double SurfaceOpacity,
    Color Border,
    double BorderOpacity,
    Color ButtonHover,
    Color ButtonPressed,
    Color Handle);

/// <summary>
/// 从已经确定的播放器前景色生成同色系悬停反馈，避免浅色任务栏仍叠加固定白色。
/// Builds same-hue hover feedback from the resolved player foreground so a light taskbar never receives a fixed white wash.
/// </summary>
public static class TaskbarHoverPalettePolicy
{
    /// <summary>生成任务栏悬停色板。/ Creates the taskbar hover palette.</summary>
    public static TaskbarHoverPalette Resolve(Color foreground)
    {
        var opaque = Color.FromRgb(foreground.R, foreground.G, foreground.B);
        return new TaskbarHoverPalette(
            opaque,
            opaque,
            0.075,
            opaque,
            0.10,
            Color.FromArgb(0x20, opaque.R, opaque.G, opaque.B),
            Color.FromArgb(0x38, opaque.R, opaque.G, opaque.B),
            Color.FromArgb(0xB8, opaque.R, opaque.G, opaque.B));
    }
}
