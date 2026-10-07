// Defines the UI configuration boundary; phase one adapts existing storage rather than creating per-mode JSON profiles.
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>Configuration access for a context-bound settings view model.</summary>
public interface ISettingsConfiguration
{
    /// <summary>Permanent presentation context of this page scope.</summary>
    SettingsContext Context { get; }
    /// <summary>Whether this scope is active; background callbacks must check before publishing.</summary>
    bool IsActive { get; }
    /// <summary>Cancels context-specific asynchronous work when the page becomes inactive.</summary>
    CancellationToken CancellationToken { get; }
    /// <summary>Existing settings source; caller writes remain on the UI thread.</summary>
    AppSettings Current { get; }
    /// <summary>Whether configuration storage is isolated by mode/axis. False until the release migration is implemented.</summary>
    bool HasIndependentProfiles { get; }
    /// <summary>Replaces the taskbar section through the existing normalization and event path.</summary>
    void SetTaskbarExperience(TaskbarExperienceSettings settings);
    /// <summary>Replaces application appearance through the current storage contract.</summary>
    void SetAppearance(AppearanceSettings settings);
    /// <summary>Replaces spectrum configuration through its domain normalization.</summary>
    void SetSpectrum(SpectrumComponentSettings settings);
    /// <summary>Replaces performance configuration through its domain normalization.</summary>
    void SetPerformance(PerformanceComponentSettings settings);
}
