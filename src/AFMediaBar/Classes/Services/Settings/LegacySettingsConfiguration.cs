// Adapts existing SettingsManager values to a page scope. Horizontal and vertical contexts still share the legacy persisted data.
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>Phase-one access boundary, intentionally without schema or persisted mode-profile changes.</summary>
public sealed class LegacySettingsConfiguration(SettingsPageContext context) : ISettingsConfiguration
{
    /// <inheritdoc />
    public SettingsContext Context => context.Snapshot;
    /// <inheritdoc />
    public bool IsActive => context.IsActive;
    /// <inheritdoc />
    public CancellationToken CancellationToken => context.CancellationToken;
    /// <inheritdoc />
    public AppSettings Current => SettingsManager.Current;
    /// <inheritdoc />
    public bool HasIndependentProfiles => false;
    /// <inheritdoc />
    public void SetTaskbarExperience(TaskbarExperienceSettings settings) { if (IsActive) SettingsManager.SetTaskbarExperienceSettings(settings.Normalize()); }
    /// <inheritdoc />
    public void SetAppearance(AppearanceSettings settings) { if (IsActive) SettingsManager.SetAppearanceSettings(settings.Normalize()); }
    /// <inheritdoc />
    public void SetSpectrum(SpectrumComponentSettings settings) { if (IsActive) SettingsManager.SetSpectrumComponentSettings(settings.Normalize()); }
    /// <inheritdoc />
    public void SetPerformance(PerformanceComponentSettings settings) { if (IsActive) SettingsManager.SetPerformanceComponentSettings(settings.Normalize()); }
}
