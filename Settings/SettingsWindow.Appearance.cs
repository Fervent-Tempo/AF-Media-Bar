using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Adapters;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Settings;
/// <summary>
/// 处理任务栏/菜单主题、字体预览和字体权重防抖保存。
/// Handles taskbar/menu themes, font previews, and debounced font-weight persistence.
/// </summary>
public partial class SettingsWindow
{
    private void ThemeRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var mode = LightForegroundRadioButton.IsChecked == true
            ? TaskbarForegroundMode.LightText
            : DarkForegroundRadioButton.IsChecked == true
                ? TaskbarForegroundMode.DarkText
                : TaskbarForegroundMode.Automatic;
        var current = _coordinator.Current.Theme;
        TryUpdate(() => _coordinator.UpdateTheme(current with
        {
            TaskbarForegroundMode = mode
        }));
    }

    private void MenuThemeRadio_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var mode = LightMenuThemeRadioButton.IsChecked == true
            ? MenuThemeMode.Light
            : DarkMenuThemeRadioButton.IsChecked == true
                ? MenuThemeMode.Dark
                : MenuThemeMode.Automatic;
        var current = _coordinator.Current.Theme;
        TryUpdate(() => _coordinator.UpdateTheme(current with
        {
            MenuThemeMode = mode
        }));
    }

    private void ThemeCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var current = _coordinator.Current.Theme;
        TryUpdate(() => _coordinator.UpdateTheme(current with
        {
            EnhancedReadability = EnhancedReadabilityCheckBox.IsChecked == true
        }));
    }

    private void FontComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        if (FontLatinComboBox.SelectedIndex < 0 ||
            FontCjkComboBox.SelectedIndex < 0 ||
            !Enum.IsDefined(typeof(LatinFontPreset), FontLatinComboBox.SelectedIndex) ||
            !Enum.IsDefined(typeof(CjkFontPreset), FontCjkComboBox.SelectedIndex))
        {
            return;
        }

        var latin = (LatinFontPreset)FontLatinComboBox.SelectedIndex;
        var cjk = (CjkFontPreset)FontCjkComboBox.SelectedIndex;
        var weight = _coordinator.Current.Font.Weight;
        TryUpdate(() => _coordinator.UpdateFont(new FontSettings(latin, cjk, weight)));
        FontPreviewText.FontFamily = new FontFamily(FontSettings.ResolveText(latin, cjk));
        FontPreviewText.FontWeight = WpfFontSettingsAdapter.ResolveTitleWeight(weight);
    }

    private void FontWeightSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized)
        {
            return;
        }

        var weight = FontSettings.NormalizeWeight((int)Math.Round(e.NewValue));
        FontWeightValueText.Text = FormatFontWeight(weight);
        FontPreviewText.FontWeight = WpfFontSettingsAdapter.ResolveTitleWeight(weight);
        if (_isSyncing)
        {
            return;
        }

        _fontSaveTimer.Stop();
        _fontSaveTimer.Start();
    }

    private void FontWeightSaveTimer_OnTick(object? sender, EventArgs e)
    {
        _fontSaveTimer.Stop();
        var current = _coordinator.Current.Font;
        var weight = FontSettings.NormalizeWeight((int)Math.Round(FontWeightSlider.Value));
        TryUpdate(() => _coordinator.UpdateFont(current with { Weight = weight }));
    }

    private static string FormatFontWeight(int weight)
    {
        var normalizedWeight = FontSettings.NormalizeWeight(weight);
        var nameKey = normalizedWeight switch
        {
            < 350 => "Settings.Appearance.FontWeightThin",
            < 450 => "Settings.Appearance.FontWeightLight",
            < 550 => "Settings.Appearance.FontWeightStandard",
            < 650 => "Settings.Appearance.FontWeightMedium",
            < 750 => "Settings.Appearance.FontWeightSemiBold",
            < 850 => "Settings.Appearance.FontWeightBold",
            _ => "Settings.Appearance.FontWeightBlack"
        };
        return Loc.Get(
            "Settings.Appearance.FontWeightValueFormat",
            normalizedWeight,
            Loc.Get(nameKey));
    }

}
