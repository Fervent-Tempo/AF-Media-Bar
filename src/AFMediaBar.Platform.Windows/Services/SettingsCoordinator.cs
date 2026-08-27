using AFMediaBar.Models;
using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Serialization;

namespace AFMediaBar.Services;

/// <summary>
/// 持有不可变设置快照，并协调现有注册表服务的读写与精准分区通知。
/// Owns the immutable settings snapshot and coordinates registry stores with precise section notifications.
/// </summary>
public sealed class SettingsCoordinator
{
    public SettingsCoordinator()
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

    public ApplicationSettings Current { get; private set; }

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public void UpdateMetrics(MetricSettings settings)
    {
        if (settings == Current.Metrics)
        {
            return;
        }

        MetricSettingsService.Save(settings);
        Current = Current with { Metrics = settings };
        Publish(SettingsSection.Performance);
    }

    public void UpdateTheme(ThemeSettings settings)
    {
        if (settings == Current.Theme)
        {
            return;
        }

        ThemeSettingsService.Save(settings);
        Current = Current with { Theme = settings };
        Publish(SettingsSection.Appearance);
    }

    public void UpdateFont(FontSettings settings)
    {
        if (settings == Current.Font)
        {
            return;
        }

        FontSettingsService.Save(settings);
        Current = Current with { Font = settings };
        Publish(SettingsSection.Font);
    }

    public void UpdateLanguage(AppLanguage language)
    {
        if (language == Current.Language)
        {
            return;
        }

        LanguageSettingsService.Save(language);
        Current = Current with { Language = language };
        Publish(SettingsSection.Language);
    }

    public void UpdateWindow(WindowSettings settings)
    {
        // Whole-window edge collapse is represented by layout edge containers.
        // Clear the legacy field so stale callers cannot re-enable that animation.
        settings = settings with { EdgeAutoCollapse = false };
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
        SynchronizeLayoutSurfaceScale(settings);
        Publish(changedSections);
    }

    public void UpdateLayout(LayoutDocument layout)
    {
        var normalized = LayoutDocumentNormalizer.Normalize(layout);
        if (normalized == Current.Layout)
        {
            return;
        }

        LayoutSettingsService.Save(normalized);
        Current = Current with { Layout = normalized };
        Publish(SettingsSection.Layout);
    }

    public void SynchronizeLayout(LayoutDocument layout)
    {
        var normalized = LayoutDocumentNormalizer.Normalize(layout);
        if (normalized == Current.Layout)
        {
            return;
        }

        LayoutSettingsService.Save(normalized);
        Current = Current with { Layout = normalized };
    }

    public void SynchronizeWindow(WindowSettings settings)
    {
        settings = settings with { EdgeAutoCollapse = false };
        if (settings == Current.Window)
        {
            return;
        }

        WindowSettingsService.Save(settings);
        Current = Current with { Window = settings };
    }

    public void UpdatePlacement(PlacementSettings settings)
    {
        if (settings == Current.Placement)
        {
            return;
        }

        PlacementSettingsService.Save(settings);
        Current = Current with { Placement = settings };
        Publish(SettingsSection.Placement);
    }

    public void SynchronizePlacement(PlacementSettings settings)
    {
        if (settings == Current.Placement)
        {
            return;
        }

        PlacementSettingsService.Save(settings);
        Current = Current with { Placement = settings };
    }

    public void UpdateStartup(bool enabled)
    {
        if (enabled == Current.StartupEnabled)
        {
            return;
        }

        StartupService.SetEnabled(enabled);
        Current = Current with { StartupEnabled = enabled };
        Publish(SettingsSection.General);
    }

    public void ResetAll()
    {
        MetricSettingsService.Save(MetricSettings.Default);
        ThemeSettingsService.Save(ThemeSettings.Default);
        FontSettingsService.Save(FontSettings.Default);
        LanguageSettingsService.Save(AppLanguage.FollowSystem);
        WindowSettingsService.Save(WindowSettings.Default);
        PlacementSettingsService.Save(PlacementSettings.Default);
        var layout = LayoutDefaultTemplates.LoadDocument();
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
    /// Synchronize legacy options only while the document still matches their generated defaults.
    /// </summary>
    private void SynchronizeLegacyLayoutIfUncustomized(bool wasLegacyLayout)
    {
        if (!wasLegacyLayout)
        {
            return;
        }

        var layout = LayoutDefaultTemplates.LoadDocument();
        LayoutSettingsService.Save(layout);
        Current = Current with { Layout = layout };
    }

    /// <summary>
    /// Preserve an edited layout tree while keeping window-level scales effective for both profiles.
    /// </summary>
    private void SynchronizeLayoutSurfaceScale(WindowSettings window)
    {
        var layout = Current.Layout;
        var updated = layout with
        {
            Horizontal = layout.Horizontal with
            {
                Surface = layout.Horizontal.Surface with
                {
                    LengthScalePercent = window.LengthScalePercent,
                    ThicknessScalePercent = window.ThicknessScalePercent
                }
            },
            Vertical = layout.Vertical with
            {
                Surface = layout.Vertical.Surface with
                {
                    LengthScalePercent = window.LengthScalePercent,
                    ThicknessScalePercent = window.ThicknessScalePercent
                }
            }
        };
        updated = LayoutDocumentNormalizer.Normalize(updated);
        if (updated == layout)
        {
            return;
        }

        LayoutSettingsService.Save(updated);
        Current = Current with { Layout = updated };
    }

    private bool IsLegacyLayout(WindowSettings window, MetricSettings metrics)
    {
        try
        {
            return Current.Layout == LayoutDefaultTemplates.LoadDocument();
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
