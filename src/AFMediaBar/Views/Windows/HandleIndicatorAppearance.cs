// 完整面板入口与贴边提示共用黑白取色、反色轮廓及高对比度画刷；不持有采样或窗口资源。
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Views.Windows;

/// <summary>细条入口的背景自适应外观。</summary>
internal static class HandleIndicatorAppearance
{
    private static readonly Brush _darkLine = CreateBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
    private static readonly Brush _darkOutline = CreateBrush(Color.FromArgb(192, 0, 0, 0));
    private static readonly Brush _lightOutline = CreateBrush(Color.FromArgb(216, 255, 255, 255));

    internal static (Brush Line, Brush Outline) Resolve(PlayerForegroundDecision? decision)
    {
        if (SystemParameters.HighContrast) return (SystemColors.WindowTextBrush, SystemColors.WindowBrush);
        return decision?.UsesLightText != false ? (Brushes.White, _darkOutline) : (_darkLine, _lightOutline);
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
