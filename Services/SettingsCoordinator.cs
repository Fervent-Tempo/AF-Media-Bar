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
        var metrics = MetricSettingsService.Load();
        var window = WindowSettingsService.Load();
        Current = new ApplicationSettings(
            metrics,
            ThemeSettingsService.Load(),
            FontSettingsService.Load(),
            LanguageSettingsService.Load(),
            window,
            PlacementSettingsService.Load(),
            LayoutSettingsService.Load(window, metrics),
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

        var previousMetrics = Current.Metrics;
        var previousWindow = Current.Window;
        var wasLegacyLayout = IsLegacyLayout(previousWindow, previousMetrics);
        MetricSettingsService.Save(settings);
        Current = Current with { Metrics = settings };
        SynchronizeLegacyLayoutIfUncustomized(wasLegacyLayout);
        Publish(SettingsSection.Components | SettingsSection.Performance | SettingsSection.Layout);
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

        var previousMetrics = Current.Metrics;
        var previousWindow = Current.Window;
        var wasLegacyLayout = IsLegacyLayout(previousWindow, previousMetrics);
        WindowSettingsService.Save(settings);
        var changedSections = SettingsSection.Window |
            SettingsSection.Interaction |
            SettingsSection.Layout;
        if (settings.HideWhenNoMedia != Current.Window.HideWhenNoMedia)
        {
            changedSections |= SettingsSection.General;
        }

        Current = Current with { Window = settings };
        SynchronizeLegacyLayoutIfUncustomized(wasLegacyLayout);
        Publish(changedSections);
    }

    internal void UpdateLayout(LayoutDocument layout)
    {
        var normalized = LayoutMigrationService.Normalize(layout);
        if (normalized == Current.Layout)
        {
            return;
        }

        LayoutSettingsService.Save(normalized);
        Current = Current with { Layout = normalized };
        Publish(SettingsSection.Layout);
    }

    internal void SynchronizeLayout(LayoutDocument layout)
    {
        var normalized = LayoutMigrationService.Normalize(layout);
        if (normalized == Current.Layout)
        {
            return;
        }

        LayoutSettingsService.Save(normalized);
        Current = Current with { Layout = normalized };
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
        var layout = LayoutMigrationService.CreateFromLegacy(
            WindowSettings.Default,
            MetricSettings.Default);
        LayoutSettingsService.Save(layout);
        StartupService.SetEnabled(false);
        Current = new ApplicationSettings(
            MetricSettings.Default,
            ThemeSettings.Default,
            FontSettings.Default,
            AppLanguage.FollowSystem,
            WindowSettings.Default,
            PlacementSettings.Default,
            layout,
            false);
        Publish(SettingsSection.All);
    }

    /// <summary>
    /// 只有布局仍等于旧设置生成的默认文档时才同步旧选项；一旦用户编辑树，旧设置不能覆盖自定义布局。
    /// Synchronize legacy options only while the document still matches their generated defaults, so tree edits cannot be overwritten.
    /// </summary>
    private void SynchronizeLegacyLayoutIfUncustomized(bool wasLegacyLayout)
    {
        if (!wasLegacyLayout)
        {
            return;
        }

        var layout = LayoutMigrationService.CreateFromLegacy(Current.Window, Current.Metrics);
        LayoutSettingsService.Save(layout);
        Current = Current with { Layout = layout };
    }

    private bool IsLegacyLayout(WindowSettings window, MetricSettings metrics)
    {
        try
        {
            return Current.Layout == LayoutMigrationService.CreateFromLegacy(window, metrics);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("legacy-layout-compare", exception);
            return false;
        }
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
