using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 持有不可变设置快照，并协调现有注册表服务的读写与精准分区通知。
/// Owns the immutable settings snapshot and coordinates registry stores with precise section notifications.
/// </summary>
internal sealed class SettingsCoordinator
{
    internal SettingsCoordinator()
    {
        Current = new ApplicationSettings(
            MetricSettingsService.Load(),
            ThemeSettingsService.Load(),
            FontSettingsService.Load(),
            LanguageSettingsService.Load(),
            WindowSettingsService.Load(),
            PlacementSettingsService.Load(),
            ReadStartupEnabled());
    }

    internal ApplicationSettings Current { get; private set; }

    internal event EventHandler<SettingsChangedEventArgs>? Changed;

    internal void UpdateMetrics(MetricSettings settings)
    {
        if (settings == Current.Metrics)
        {
            return;
        }

        MetricSettingsService.Save(settings);
        Current = Current with { Metrics = settings };
        Publish(SettingsSection.Components | SettingsSection.Performance);
    }

    internal void UpdateTheme(ThemeSettings settings)
    {
        if (settings == Current.Theme)
        {
            return;
        }

        ThemeSettingsService.Save(settings);
        Current = Current with { Theme = settings };
        Publish(SettingsSection.Appearance);
    }

    internal void UpdateFont(FontSettings settings)
    {
        if (settings == Current.Font)
        {
            return;
        }

        FontSettingsService.Save(settings);
        Current = Current with { Font = settings };
        Publish(SettingsSection.Font);
    }

    internal void UpdateLanguage(AppLanguage language)
    {
        if (language == Current.Language)
        {
            return;
        }

        LanguageSettingsService.Save(language);
        Current = Current with { Language = language };
        Publish(SettingsSection.Language);
    }

    internal void UpdateWindow(WindowSettings settings)
    {
        if (Current.Window.HostMode == WindowHostMode.Floating &&
            settings.HostMode == WindowHostMode.Taskbar)
        {
            settings = settings with { LayoutMode = PlayerLayoutMode.Automatic };
        }

        if (settings == Current.Window)
        {
            return;
        }

        WindowSettingsService.Save(settings);
        var changedSections = SettingsSection.Window | SettingsSection.Interaction;
        if (settings.HideWhenNoMedia != Current.Window.HideWhenNoMedia)
        {
            changedSections |= SettingsSection.General;
        }

        Current = Current with { Window = settings };
        Publish(changedSections);
    }

    internal void SynchronizeWindow(WindowSettings settings)
    {
        if (settings == Current.Window)
        {
            return;
        }

        WindowSettingsService.Save(settings);
        Current = Current with { Window = settings };
    }

    internal void UpdatePlacement(PlacementSettings settings)
    {
        if (settings == Current.Placement)
        {
            return;
        }

        PlacementSettingsService.Save(settings);
        Current = Current with { Placement = settings };
        Publish(SettingsSection.Placement);
    }

    internal void SynchronizePlacement(PlacementSettings settings)
    {
        if (settings == Current.Placement)
        {
            return;
        }

        PlacementSettingsService.Save(settings);
        Current = Current with { Placement = settings };
    }

    internal void UpdateStartup(bool enabled)
    {
        if (enabled == Current.StartupEnabled)
        {
            return;
        }

        StartupService.SetEnabled(enabled);
        Current = Current with { StartupEnabled = enabled };
        Publish(SettingsSection.General);
    }

    internal void ResetAll()
    {
        MetricSettingsService.Save(MetricSettings.Default);
        ThemeSettingsService.Save(ThemeSettings.Default);
        FontSettingsService.Save(FontSettings.Default);
        LanguageSettingsService.Save(AppLanguage.FollowSystem);
        WindowSettingsService.Save(WindowSettings.Default);
        PlacementSettingsService.Save(PlacementSettings.Default);
        StartupService.SetEnabled(false);
        Current = new ApplicationSettings(
            MetricSettings.Default,
            ThemeSettings.Default,
            FontSettings.Default,
            AppLanguage.FollowSystem,
            WindowSettings.Default,
            PlacementSettings.Default,
            false);
        Publish(SettingsSection.All);
    }

    private void Publish(SettingsSection sections)
    {
        Changed?.Invoke(this, new SettingsChangedEventArgs(Current, sections));
    }

    private static bool ReadStartupEnabled()
    {
        try
        {
            return StartupService.IsEnabled;
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("startup-setting-read", exception);
            return false;
        }
    }
}
