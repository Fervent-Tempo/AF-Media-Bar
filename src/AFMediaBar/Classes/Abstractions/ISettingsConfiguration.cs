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
    /// <summary>Notifies a cached editor to refresh after reactivation.</summary>
    event EventHandler? Activated;
    /// <summary>Resets one existing settings scope without changing the persisted schema.</summary>
    void Reset(SettingsResetScope scope);
    /// <summary>Updates target preferences; independent of the editor's selected display.</summary>
    void SetTaskbarTargets(IReadOnlyList<string> deviceIds);
    /// <summary>Updates the existing placement record without overriding automatic taskbar orientation.</summary>
    void SetTaskbarPlacement(bool? locked = null, bool? avoidIcons = null, double? crossAxisOffsetDip = null, bool reset = false);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsEnabled(bool enabled);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetAllowBrowserAndVideoLyrics(bool enabled);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetTwoLineLyricsEnabled(bool enabled);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsSecondaryLineSettings(LyricsSecondaryLineSettings settings);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsTextAlignment(LyricsTextAlignment alignment);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsSyllableHighlightEnabled(bool enabled);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsUnsungOpacityPercent(int percent);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsCharacterSpacingPercent(int percent);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsLineGapPercent(int percent);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsFixedWidthEnabled(bool enabled);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsFixedWidthDip(int dip);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsInfoLineFilterEnabled(bool enabled);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsSourceSettings(LyricsSourceSettings settings);
    /// <summary>Saves artist separators through the existing lyrics matching contract.</summary>
    void SetLyricsArtistSeparators(string separators);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetInteractionSettings(GlobalInteractionSettings settings);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetTrackChangeNotificationSettings(TrackChangeNotificationSettings settings);
}
