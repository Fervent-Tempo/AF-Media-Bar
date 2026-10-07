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
    public void SetTaskbarExperience(TaskbarExperienceSettings settings) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetTaskbarExperienceSettings(settings.Normalize()); }
    /// <inheritdoc />
    public void SetAppearance(AppearanceSettings settings) { if (IsActive) SettingsManager.SetAppearanceSettings(settings.Normalize()); }
    /// <inheritdoc />
    public void SetSpectrum(SpectrumComponentSettings settings) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetSpectrumComponentSettings(settings.Normalize()); }
    /// <inheritdoc />
    public void SetPerformance(PerformanceComponentSettings settings) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetPerformanceComponentSettings(settings.Normalize()); }
    /// <inheritdoc />
    public event EventHandler? Activated { add => context.Activated += value; remove => context.Activated -= value; }
    /// <inheritdoc />
    public void Reset(SettingsResetScope scope)
    {
        if (!IsActive || Context.Mode != SettingsMode.Taskbar) return;
        switch (scope)
        {
            case SettingsResetScope.Lyrics: SettingsManager.ResetLyrics(); break;
            case SettingsResetScope.Interaction: SettingsManager.ResetInteraction(); break;
            case SettingsResetScope.DisplayModes: SettingsManager.ResetDisplayModes(); break;
            case SettingsResetScope.Components: SettingsManager.ResetComponents(); break;
            default: throw new ArgumentOutOfRangeException(nameof(scope));
        }
    }
    /// <inheritdoc />
    public void SetTaskbarTargets(IReadOnlyList<string> deviceIds)
    {
        if (!IsActive || Context.Mode != SettingsMode.Taskbar) return;
        if (!(Current.TaskbarTargetMonitorDeviceIds ?? []).SequenceEqual(deviceIds, StringComparer.OrdinalIgnoreCase))
            Current.TaskbarTargetMonitorDeviceIds = deviceIds.ToList();
        Current.TaskbarTargetMonitorDeviceId = null;
    }
    /// <inheritdoc />
    public void SetTaskbarPlacement(bool? locked = null, bool? avoidIcons = null, double? crossAxisOffsetDip = null, bool reset = false)
    {
        if (!IsActive || Context.Mode != SettingsMode.Taskbar) return;
        if (locked is { } lockValue) Current.TaskbarBarPositionLocked = lockValue;
        if (avoidIcons is { } avoidValue) Current.TaskbarBarAvoidIcons = avoidValue;
        if (crossAxisOffsetDip is { } offset) Current.TaskbarBarCrossAxisOffsetDip = offset;
        if (reset) { Current.Position = TaskbarBarPosition.Start; Current.TaskbarBarManualPadding = 0; Current.TaskbarBarCrossAxisOffsetDip = 0; }
        SettingsManager.RaiseLayoutSettingsChanged(Current.WindowMode, Current.LayoutOrientationMode);
    }
    /// <inheritdoc />
    public void SetLyricsEnabled(bool enabled) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsEnabled(enabled); }
    /// <inheritdoc />
    public void SetAllowBrowserAndVideoLyrics(bool enabled) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetAllowBrowserAndVideoLyrics(enabled); }
    /// <inheritdoc />
    public void SetTwoLineLyricsEnabled(bool enabled) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetTwoLineLyricsEnabled(enabled); }
    /// <inheritdoc />
    public void SetLyricsSecondaryLineSettings(LyricsSecondaryLineSettings settings) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsSecondaryLineSettings(settings); }
    /// <inheritdoc />
    public void SetLyricsTextAlignment(LyricsTextAlignment alignment) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsTextAlignment(alignment); }
    /// <inheritdoc />
    public void SetLyricsSyllableHighlightEnabled(bool enabled) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsSyllableHighlightEnabled(enabled); }
    /// <inheritdoc />
    public void SetLyricsUnsungOpacityPercent(int percent) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsUnsungOpacityPercent(percent); }
    /// <inheritdoc />
    public void SetLyricsCharacterSpacingPercent(int percent) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsCharacterSpacingPercent(percent); }
    /// <inheritdoc />
    public void SetLyricsLineGapPercent(int percent) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsLineGapPercent(percent); }
    /// <inheritdoc />
    public void SetLyricsFixedWidthEnabled(bool enabled) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsFixedWidthEnabled(enabled); }
    /// <inheritdoc />
    public void SetLyricsFixedWidthDip(int dip) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsFixedWidthDip(dip); }
    /// <inheritdoc />
    public void SetLyricsInfoLineFilterEnabled(bool enabled) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsInfoLineFilterEnabled(enabled); }
    /// <inheritdoc />
    public void SetLyricsSourceSettings(LyricsSourceSettings settings) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetLyricsSourceSettings(settings); }
    /// <inheritdoc />
    public void SetInteractionSettings(GlobalInteractionSettings settings) { if (IsActive && Context.Mode == SettingsMode.Taskbar) SettingsManager.SetInteractionSettings(settings); }
    /// <inheritdoc />
    public void SetTrackChangeNotificationSettings(TrackChangeNotificationSettings settings) { if (IsActive) SettingsManager.SetTrackChangeNotificationSettings(settings); }
}
