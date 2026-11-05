// 完整面板入口、拖动手柄与贴边提示共用黑白配色；不持有采样或窗口资源。
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Components;

/// <summary>栏外入口的背景自适应外观；无采样结果时使用浅色，高对比度使用系统文字色。</summary>
internal static class HandleIndicatorAppearance
{
    private static readonly Brush _darkLine = CreateBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));

    internal static Brush Resolve(PlayerForegroundDecision? decision)
    {
        if (SystemParameters.HighContrast) return SystemColors.WindowTextBrush;
        return decision?.UsesLightText != false ? Brushes.White : _darkLine;
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
