// 完整面板入口与贴边提示共用黑白取色及高对比度画刷；不持有采样或窗口资源。
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Views.Windows;

/// <summary>细条入口的背景自适应外观。</summary>
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
