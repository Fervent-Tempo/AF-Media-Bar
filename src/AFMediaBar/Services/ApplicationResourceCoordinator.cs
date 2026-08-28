using System.Windows;
using System.Windows.Media;
using AFMediaBar.Adapters;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>Applies persisted language, font, and theme settings to application resources.</summary>
internal sealed class ApplicationResourceCoordinator(
    Application application,
    SettingsCoordinator settings,
    SystemThemeService themeService,
    Func<MainWindow?> getMainWindow) : IDisposable
{
    private bool _disposed;

    internal void Initialize()
    {
        ApplyLanguage(settings.Current.Language);
        ApplyFont(settings.Current.Font);
        settings.Changed += Settings_OnChanged;
    }

    private void Settings_OnChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (_disposed || application.Dispatcher.HasShutdownStarted) return;
        if (e.Sections.HasFlag(SettingsSection.Font)) ApplyFont(e.Settings.Font);
        if (e.Sections.HasFlag(SettingsSection.Appearance)) themeService.Refresh(e.Settings.Theme);
        if (e.Sections.HasFlag(SettingsSection.Language))
        {
            ApplyLanguage(e.Settings.Language);
            getMainWindow()?.RefreshLocalizedText();
        }
    }

    private void ApplyFont(FontSettings font)
    {
        var textFamily = new FontFamily(FontSettings.ResolveText(font.Latin, font.Cjk));
        application.Resources["AppTextFontFamily"] = textFamily;
        application.Resources["AppDisplayFontFamily"] = textFamily;
        application.Resources["PlayerTitleFontWeight"] = WpfFontSettingsAdapter.ResolveTitleWeight(font.Weight);
        application.Resources["PlayerTextFontWeight"] = WpfFontSettingsAdapter.ResolveBodyWeight(font.Weight);
    }

    private void ApplyLanguage(AppLanguage language)
    {
        var dictionaryName = LanguageSettingsService.ResolveDictionaryName(language);
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Resources/Languages/{dictionaryName}.xaml", UriKind.Relative)
        };
        if (application.Resources.MergedDictionaries.Count > 0)
            application.Resources.MergedDictionaries[0] = dictionary;
        else
            application.Resources.MergedDictionaries.Add(dictionary);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        settings.Changed -= Settings_OnChanged;
    }
}
