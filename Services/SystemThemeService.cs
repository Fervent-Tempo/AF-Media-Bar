using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using Microsoft.Win32;

namespace AFMediaBar.Services;

/// <summary>
/// Keeps taskbar and popup resources synchronized with Windows theme settings.
/// </summary>
internal sealed class SystemThemeService : IDisposable
{
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKeyPath = @"Software\Microsoft\Windows\DWM";

    private static readonly Color LightTaskbarColor = Color.FromRgb(243, 243, 243);
    private static readonly Color DarkTaskbarColor = Color.FromRgb(32, 32, 32);
    private static readonly Color DefaultAccentColor = Color.FromRgb(0, 120, 212);

    private readonly Application _application;
    private int _updatePending;
    private bool _disposed;

    internal SystemThemeService(Application application)
    {
        _application = application;
        SystemEvents.UserPreferenceChanged += SystemEvents_OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += SystemParameters_OnStaticPropertyChanged;
        ApplyTheme();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= SystemEvents_OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= SystemParameters_OnStaticPropertyChanged;
    }

    internal void Refresh()
    {
        QueueThemeUpdate();
    }

    private void SystemEvents_OnUserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        QueueThemeUpdate();
    }

    private void SystemParameters_OnStaticPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.HighContrast) or
            nameof(SystemParameters.WindowGlassColor))
        {
            QueueThemeUpdate();
        }
    }

    private void QueueThemeUpdate()
    {
        if (_disposed || _application.Dispatcher.HasShutdownStarted ||
            Interlocked.Exchange(ref _updatePending, 1) != 0)
        {
            return;
        }

        try
        {
            _application.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () =>
                {
                    Interlocked.Exchange(ref _updatePending, 0);
                    if (!_disposed && !_application.Dispatcher.HasShutdownStarted)
                    {
                        ApplyTheme();
                    }
                });
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _updatePending, 0);
        }
    }

    private void ApplyTheme()
    {
        if (SystemParameters.HighContrast)
        {
            ApplyHighContrastTheme();
            return;
        }

        var systemUsesLightTheme = ReadBoolean(
            PersonalizeKeyPath,
            "SystemUsesLightTheme",
            defaultValue: false);
        var appsUseLightTheme = ReadBoolean(
            PersonalizeKeyPath,
            "AppsUseLightTheme",
            defaultValue: true);
        var colorPrevalence = ReadBoolean(
            PersonalizeKeyPath,
            "ColorPrevalence",
            ReadBoolean(DwmKeyPath, "ColorPrevalence", defaultValue: false));
        var themeSettings = ThemeSettingsService.Load();

        var accentColor = ReadAccentColor(out var accentOpacity);
        ApplyTaskbarTheme(
            systemUsesLightTheme,
            colorPrevalence,
            accentColor,
            accentOpacity,
            themeSettings);
        var menuUsesLightTheme = themeSettings.MenuThemeMode switch
        {
            MenuThemeMode.Light => true,
            MenuThemeMode.Dark => false,
            _ => appsUseLightTheme
        };
        ApplyMenuTheme(menuUsesLightTheme, accentColor);
    }

    private void ApplyTaskbarTheme(
        bool usesLightTheme,
        bool colorPrevalence,
        Color accentColor,
        double accentOpacity,
        ThemeSettings themeSettings)
    {
        var defaultBackground = usesLightTheme ? LightTaskbarColor : DarkTaskbarColor;
        var background = colorPrevalence
            ? Blend(accentColor, defaultBackground, accentOpacity)
            : defaultBackground;
        var foreground = themeSettings.TaskbarForegroundMode switch
        {
            TaskbarForegroundMode.LightText => Color.FromRgb(255, 255, 255),
            TaskbarForegroundMode.DarkText => Color.FromRgb(15, 15, 15),
            _ => BestContrastingTextColor(background)
        };
        var usesDarkForeground = GetRelativeLuminance(foreground) < 0.5;
        var overlay = usesDarkForeground ? Colors.White : Colors.Black;

        SetBrush("TaskbarPrimaryTextBrush", foreground);
        SetBrush("TaskbarSecondaryTextBrush", WithAlpha(foreground, 0xB8));
        SetBrush("TaskbarSurfaceBrush", WithAlpha(overlay, usesDarkForeground ? (byte)0x18 : (byte)0x2A));
        SetBrush("TaskbarHoverBrush", WithAlpha(overlay, usesDarkForeground ? (byte)0x14 : (byte)0x24));
        SetBrush("TaskbarPressedBrush", WithAlpha(overlay, usesDarkForeground ? (byte)0x24 : (byte)0x38));
        SetBrush("TaskbarDividerBrush", WithAlpha(overlay, 0x28));
        SetBrush("TaskbarHighlightTextBrush", foreground);
        SetBrush(
            "TaskbarReadabilityBrush",
             themeSettings.EnhancedReadability
                ? WithAlpha(overlay, 0x58)
                : Color.FromArgb(0, 0, 0, 0));
        SetTextEffect(overlay, 0.82);
    }

    private void ApplyMenuTheme(bool usesLightTheme, Color accentColor)
    {
        var background = usesLightTheme
            ? Color.FromRgb(249, 249, 249)
            : Color.FromRgb(32, 33, 36);
        var primary = usesLightTheme
            ? Color.FromRgb(24, 24, 24)
            : Color.FromRgb(247, 247, 247);
        var secondary = WithAlpha(primary, usesLightTheme ? (byte)0xB0 : (byte)0xC0);
        var accessibleAccent = EnsureContrast(accentColor, background, minimumRatio: 3.0);
        var menuBackground = usesLightTheme
            ? WithAlpha(background, 0xFF)
            : WithAlpha(background, 0xFA);
        var border = WithAlpha(
            usesLightTheme ? Colors.Black : Colors.White,
            usesLightTheme ? (byte)0x2B : (byte)0x45);
        var hover = WithAlpha(
            usesLightTheme ? Colors.Black : Colors.White,
            usesLightTheme ? (byte)0x12 : (byte)0x20);
        var pressed = WithAlpha(
            usesLightTheme ? Colors.Black : Colors.White,
            usesLightTheme ? (byte)0x20 : (byte)0x32);
        var selection = WithAlpha(accessibleAccent, usesLightTheme ? (byte)0x38 : (byte)0x55);
        var separator = WithAlpha(
            usesLightTheme ? Colors.Black : Colors.White,
            usesLightTheme ? (byte)0x20 : (byte)0x2F);
        var sliderTrack = WithAlpha(
            usesLightTheme ? Colors.Black : Colors.White,
            usesLightTheme ? (byte)0x24 : (byte)0x30);
        var sliderThumb = usesLightTheme ? Colors.White : Color.FromRgb(247, 247, 247);

        var backgroundBrush = SetBrush("MenuBackgroundBrush", menuBackground);
        var primaryBrush = SetBrush("MenuPrimaryTextBrush", primary);
        SetBrush("MenuSecondaryTextBrush", secondary);
        SetBrush("MenuBorderBrush", border);
        SetBrush("MenuHoverBrush", hover);
        SetBrush("MenuPressedBrush", pressed);
        var selectionBrush = SetBrush("MenuSelectionBrush", selection);
        SetBrush("MenuHighlightTextBrush", primary);
        SetBrush("MenuDisabledBrush", WithAlpha(primary, 0x66));
        SetBrush("MenuSeparatorBrush", separator);
        SetBrush("SliderFillBrush", accessibleAccent);
        SetBrush("SliderTrackBrush", sliderTrack);
        SetBrush("SliderThumbBrush", sliderThumb);
        SetBrush("SliderThumbBorderBrush", accessibleAccent);

        _application.Resources[SystemColors.MenuBrushKey] = backgroundBrush;
        _application.Resources[SystemColors.MenuTextBrushKey] = primaryBrush;
        _application.Resources[SystemColors.HighlightBrushKey] = selectionBrush;
        _application.Resources[SystemColors.HighlightTextBrushKey] = primaryBrush;
        _application.Resources[SystemColors.ControlBrushKey] = backgroundBrush;
    }

    private void ApplyHighContrastTheme()
    {
        SetBrush("TaskbarPrimaryTextBrush", SystemColors.WindowTextBrush);
        SetBrush("TaskbarSecondaryTextBrush", SystemColors.WindowTextBrush);
        SetBrush("TaskbarSurfaceBrush", SystemColors.WindowBrush);
        SetBrush("TaskbarHoverBrush", SystemColors.HighlightBrush);
        SetBrush("TaskbarPressedBrush", SystemColors.HighlightBrush);
        SetBrush("TaskbarDividerBrush", SystemColors.WindowTextBrush);
        SetBrush("TaskbarHighlightTextBrush", SystemColors.HighlightTextBrush);
        SetBrush("TaskbarReadabilityBrush", Colors.Transparent);
        SetTextEffect(SystemColors.WindowColor, 1.0);

        SetBrush("MenuBackgroundBrush", SystemColors.MenuBrush);
        SetBrush("MenuPrimaryTextBrush", SystemColors.MenuTextBrush);
        SetBrush("MenuSecondaryTextBrush", SystemColors.MenuTextBrush);
        SetBrush("MenuBorderBrush", SystemColors.WindowTextBrush);
        SetBrush("MenuHoverBrush", SystemColors.HighlightBrush);
        SetBrush("MenuPressedBrush", SystemColors.HighlightBrush);
        SetBrush("MenuSelectionBrush", SystemColors.HighlightBrush);
        SetBrush("MenuHighlightTextBrush", SystemColors.HighlightTextBrush);
        SetBrush("MenuDisabledBrush", SystemColors.GrayTextBrush);
        SetBrush("MenuSeparatorBrush", SystemColors.WindowTextBrush);
        SetBrush("SliderFillBrush", SystemColors.HighlightBrush);
        SetBrush("SliderTrackBrush", SystemColors.ControlDarkBrush);
        SetBrush("SliderThumbBrush", SystemColors.HighlightTextBrush);
        SetBrush("SliderThumbBorderBrush", SystemColors.HighlightBrush);

        _application.Resources[SystemColors.MenuBrushKey] = SystemColors.MenuBrush;
        _application.Resources[SystemColors.MenuTextBrushKey] = SystemColors.MenuTextBrush;
        _application.Resources[SystemColors.HighlightBrushKey] = SystemColors.HighlightBrush;
        _application.Resources[SystemColors.HighlightTextBrushKey] = SystemColors.HighlightTextBrush;
        _application.Resources[SystemColors.ControlBrushKey] = SystemColors.ControlBrush;
    }

    private SolidColorBrush SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _application.Resources[key] = brush;
        return brush;
    }

    private void SetBrush(string key, SolidColorBrush brush)
    {
        _application.Resources[key] = brush;
    }

    private void SetTextEffect(Color color, double opacity)
    {
        var effect = new DropShadowEffect
        {
            Color = color,
            BlurRadius = 1,
            Direction = 0,
            ShadowDepth = 0,
            Opacity = opacity
        };
        effect.Freeze();
        _application.Resources["TaskbarTextEffect"] = effect;
    }

    private static bool ReadBoolean(string keyPath, string name, bool defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            return key?.GetValue(name) switch
            {
                int value => value != 0,
                long value => value != 0,
                _ => defaultValue
            };
        }
        catch
        {
            return defaultValue;
        }
    }

    private static Color ReadAccentColor(out double opacity)
    {
        try
        {
            if (NativeMethods.DwmGetColorizationColor(out var colorization, out var opaqueBlend) == 0)
            {
                var alpha = (byte)(colorization >> 24);
                opacity = opaqueBlend ? 1.0 : Math.Clamp(alpha / 255.0, 0.45, 1.0);
                return Color.FromRgb(
                    (byte)(colorization >> 16),
                    (byte)(colorization >> 8),
                    (byte)colorization);
            }
        }
        catch (DllNotFoundException)
        {
            // DWM is present on supported Windows versions; retain a defensive fallback.
        }
        catch (EntryPointNotFoundException)
        {
            // Retain a fallback for unusual compatibility environments.
        }

        var fallback = SystemParameters.WindowGlassColor;
        if (fallback.A != 0)
        {
            opacity = Math.Clamp(fallback.A / 255.0, 0.45, 1.0);
            return Color.FromRgb(fallback.R, fallback.G, fallback.B);
        }

        opacity = 1.0;
        return DefaultAccentColor;
    }

    private static Color BestContrastingTextColor(Color background)
    {
        var black = Color.FromRgb(15, 15, 15);
        var white = Color.FromRgb(255, 255, 255);
        return ContrastRatio(background, black) >= ContrastRatio(background, white)
            ? black
            : white;
    }

    private static Color EnsureContrast(Color foreground, Color background, double minimumRatio)
    {
        if (ContrastRatio(foreground, background) >= minimumRatio)
        {
            return foreground;
        }

        var target = GetRelativeLuminance(background) > 0.5 ? Colors.Black : Colors.White;
        for (var step = 1; step <= 10; step++)
        {
            var candidate = Blend(target, foreground, step / 10.0);
            if (ContrastRatio(candidate, background) >= minimumRatio)
            {
                return candidate;
            }
        }

        return target;
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var lighter = Math.Max(GetRelativeLuminance(first), GetRelativeLuminance(second));
        var darker = Math.Min(GetRelativeLuminance(first), GetRelativeLuminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double GetRelativeLuminance(Color color)
    {
        static double ToLinear(byte component)
        {
            var channel = component / 255.0;
            return channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * ToLinear(color.R)) +
            (0.7152 * ToLinear(color.G)) +
            (0.0722 * ToLinear(color.B));
    }

    private static Color Blend(Color foreground, Color background, double opacity)
    {
        opacity = Math.Clamp(opacity, 0.0, 1.0);
        return Color.FromRgb(
            (byte)Math.Round((foreground.R * opacity) + (background.R * (1.0 - opacity))),
            (byte)Math.Round((foreground.G * opacity) + (background.G * (1.0 - opacity))),
            (byte)Math.Round((foreground.B * opacity) + (background.B * (1.0 - opacity))));
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
