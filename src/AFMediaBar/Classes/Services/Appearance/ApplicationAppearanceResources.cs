// 根据已解析的主题、字体和强调色生成应用资源；只更新传入的 UI 线程资源字典。
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Settings;
using Wpf.Ui.Appearance;

namespace AFMediaBar.Classes.Services;

/// <summary>统一生成应用外观资源，组合根仅连接主题协调器与资源字典。</summary>
public sealed class ApplicationAppearanceResources(ResourceDictionary resources)
{
    private readonly ResourceDictionary _resources = resources;

    public void Apply(AppearanceSettings appearance, ApplicationTheme theme, AccentPalette accent)
    {
        var fontFamily = InstalledFontCatalog.CreateCompositeFont(appearance, SystemFonts.MessageFontFamily.Source);
        var fontWeight = FontWeight.FromOpenTypeWeight(appearance.FontWeight);
        _resources["AppTextFontFamily"] = fontFamily;
        _resources["ContentControlThemeFontFamily"] = fontFamily;
        _resources["AppTextFontWeight"] = fontWeight;
        _resources["AppTextMediumFontWeight"] = FontWeight.FromOpenTypeWeight(Math.Clamp(appearance.FontWeight + 100, 100, 999));
        _resources["AppTextStrongFontWeight"] = FontWeight.FromOpenTypeWeight(Math.Clamp(appearance.FontWeight + 200, 100, 999));

        // 强调色只在这里发布一次：所有界面（任务栏媒体栏、菜单、完整层、设置页与图示）都引用这几个应用级画刷，
        // 因此系统强调色变化后不需要逐个界面刷新，也不会再出现"设置页是粉色、媒体栏是默认蓝"的分裂。
        // The accent is published exactly once, here: every surface (taskbar bar, menus, full panel, settings pages, and
        // diagrams) references these application-level brushes, so an accent change needs no per-surface refresh and the
        // settings-pink / media-blue split cannot come back.
        _resources["AfAccentBrush"] = CreateFrozenBrush(accent.Accent);
        _resources["AfAccentHoverBrush"] = CreateFrozenBrush(accent.Hover);
        _resources["AfAccentPressedBrush"] = CreateFrozenBrush(accent.Pressed);
        _resources["AfAccentTintBrush"] = CreateFrozenBrush(accent.Tint);
        _resources["AfOnAccentBrush"] = CreateFrozenBrush(accent.OnAccent);

        var dark = theme == ApplicationTheme.Dark || theme == ApplicationTheme.HighContrast && SystemParameters.HighContrast;
        var highContrast = SystemParameters.HighContrast;
        var showSettingsMaterial = !highContrast && appearance.BackdropMode != ApplicationBackdropMode.FluentSolid;
        var cardColor = TintSettingsSurface(dark ? Color.FromRgb(44, 44, 44) : Colors.White, accent.Accent, dark ? 0.08 : 0.04);
        var groupColor = TintSettingsSurface(dark ? Color.FromRgb(40, 40, 40) : Color.FromRgb(248, 248, 248), accent.Accent, dark ? 0.08 : 0.06);
        var navigationColor = TintSettingsSurface(dark ? Color.FromRgb(30, 30, 30) : Color.FromRgb(236, 236, 236), accent.Accent, dark ? 0.10 : 0.08);
        // Alpha belongs to the surface brushes, never to a whole card: labels and controls stay fully legible.
        _resources["AfSettingsCardBrush"] = highContrast ? SystemColors.WindowBrush : CreateFrozenBrush(WithAlpha(cardColor, showSettingsMaterial ? (byte)(dark ? 240 : 245) : byte.MaxValue));
        _resources["AfSettingsInfoSurfaceBrush"] = highContrast ? SystemColors.WindowBrush : CreateFrozenBrush(WithAlpha(cardColor, showSettingsMaterial ? (byte)(dark ? 235 : 242) : byte.MaxValue));
        _resources["AfSettingsGroupSurfaceBrush"] = highContrast ? SystemColors.WindowBrush : CreateFrozenBrush(WithAlpha(groupColor, showSettingsMaterial ? (byte)(dark ? 235 : 242) : byte.MaxValue));
        _resources["AfSettingsInputBrush"] = highContrast ? SystemColors.WindowBrush : CreateFrozenBrush(TintSettingsSurface(dark ? Color.FromRgb(54, 54, 54) : Color.FromRgb(247, 247, 247), accent.Accent, dark ? 0.08 : 0.04));
        _resources["AfSettingsBorderBrush"] = highContrast ? SystemColors.WindowTextBrush : CreateFrozenBrush(TintSettingsSurface(dark ? Color.FromRgb(72, 72, 72) : Color.FromRgb(216, 216, 216), accent.Accent, 0.08));
        _resources["AfSettingsNavigationBrush"] = highContrast ? SystemColors.WindowBrush : CreateFrozenBrush(navigationColor);
        _resources["AfSettingsSectionBrush"] = highContrast ? SystemColors.WindowBrush : CreateFrozenBrush(groupColor);
        _resources["AfSettingsHeaderBridgeBrush"] = showSettingsMaterial ? CreateSettingsHeaderBridgeBrush(groupColor) : Brushes.Transparent;
        // Context menus always use an opaque Fluent solid surface. Native
        // Mica/Acrylic on Popup HWNDs leaves transparent hit-test regions
        // that can pass clicks through to the window behind the menu.

        var menuColor = dark ? Color.FromRgb(44, 44, 44) : Color.FromRgb(249, 249, 249);
        var menuBrush = new SolidColorBrush(menuColor);
        menuBrush.Freeze();
        _resources["AppMenuBackgroundBrush"] = menuBrush;
        _resources["ContextMenuBackground"] = menuBrush;
    }

    private static Color TintSettingsSurface(Color surface, Color accent, double amount) =>
        Color.FromRgb(
            (byte)Math.Round(surface.R + (accent.R - surface.R) * amount),
            (byte)Math.Round(surface.G + (accent.G - surface.G) * amount),
            (byte)Math.Round(surface.B + (accent.B - surface.B) * amount));

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static LinearGradientBrush CreateSettingsHeaderBridgeBrush(Color color)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 210), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 150), 0.6));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

}
