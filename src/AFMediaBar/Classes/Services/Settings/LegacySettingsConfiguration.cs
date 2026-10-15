// 连接设置编辑上下文与持久化配置；任务栏横竖共用原字段，悬浮配置独立保存。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>按宿主目标读写设置，非活动页面不能提交旧编辑结果。</summary>
public sealed class LegacySettingsConfiguration(SettingsPageContext context) : ISettingsConfiguration
{
    public SettingsContext Context => context.Snapshot;
    public bool IsActive => context.IsActive && (Context.Mode != SettingsMode.FloatingBar ||
        SettingsManager.Current.Experiments.FloatingBar);
    public CancellationToken CancellationToken => context.CancellationToken;
    public BarConfigurationTarget Target => Context.Mode == SettingsMode.FloatingBar ? BarConfigurationTarget.FloatingBar : BarConfigurationTarget.Taskbar;
    public AppSettings Current => Target == BarConfigurationTarget.Taskbar ? SettingsManager.Current
        : (SettingsManager.Current.FloatingBar.Profile ?? BarDisplaySettings.Capture(SettingsManager.Current)).ApplyTo(SettingsManager.Current);
    public bool HasIndependentProfiles => true;
    public event EventHandler? Activated { add => context.Activated += value; remove => context.Activated -= value; }

    private void Update(Action<AppSettings> update)
    {
        if (!IsActive || Context.Mode is not (SettingsMode.Taskbar or SettingsMode.FloatingBar)) return;
        var current = Current;
        update(current);
        if (Target == BarConfigurationTarget.FloatingBar)
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Profile = BarDisplaySettings.Capture(current) };
    }

    public void SetTaskbarExperience(TaskbarExperienceSettings settings) => Update(current => current.TaskbarExperience = settings.Normalize());
    public void SetAppearance(AppearanceSettings settings)
    {
        if (!IsActive) return;
        SettingsManager.SetAppearanceSettings(Target == BarConfigurationTarget.FloatingBar
            ? BarDisplaySettings.MergeAppearance(settings.Normalize(), SettingsManager.Current.Appearance) : settings.Normalize());
    }
    public void SetTaskbarAppearance(AppearanceSettings settings) => Update(current =>
    {
        current.Appearance = current.Appearance with
        {
            PlayerForegroundMode = settings.PlayerForegroundMode,
            TaskbarBackgroundMaterial = settings.TaskbarBackgroundMaterial,
            TaskbarBackgroundOpacityPercent = settings.TaskbarBackgroundOpacityPercent,
            ShowTaskbarBackgroundWhenIdle = settings.ShowTaskbarBackgroundWhenIdle,
            ShowTaskbarBackgroundTint = settings.ShowTaskbarBackgroundTint,
            ShowTaskbarBackgroundBorder = settings.ShowTaskbarBackgroundBorder,
            ShowTaskbarBackgroundTexture = settings.ShowTaskbarBackgroundTexture,
            TaskbarFrostedStyle = settings.TaskbarFrostedStyle,
            IdleNoteColor = settings.IdleNoteColor,
        };
    });
    public void SetSpectrum(SpectrumComponentSettings settings) => Update(current => current.SpectrumComponent = settings.Normalize());
    public void SetPerformance(PerformanceComponentSettings settings) => Update(current => current.PerformanceComponent = settings.Normalize());
    public void SetExperiments(ExperimentalSettings settings)
    {
        if (!context.IsActive) return;
        if (settings.FloatingBar && SettingsManager.Current.FloatingBar.Profile is null)
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Profile = BarDisplaySettings.Capture(SettingsManager.Current) };
        SettingsManager.Current.Experiments = settings;
    }
    public void SetFloatingBar(FloatingBarSettings settings) { if (IsActive) SettingsManager.Current.FloatingBar = settings; }
    public void SetTaskbarTargets(IReadOnlyList<string> deviceIds)
    {
        if (!IsActive) return;
        if (Target == BarConfigurationTarget.FloatingBar)
            SetFloatingBar(SettingsManager.Current.FloatingBar with { MonitorDeviceIds = deviceIds.ToArray() });
        else
        {
            SettingsManager.Current.TaskbarTargetMonitorDeviceIds = deviceIds.ToArray();
            SettingsManager.Current.TaskbarTargetMonitorDeviceId = null;
        }
    }
    public void SetTaskbarPlacement(bool? locked = null, bool? avoidIcons = null, double? crossAxisOffsetDip = null, bool reset = false, TaskbarBarPosition? alignment = null)
    {
        Update(current =>
        {
            if (alignment is { } position && Enum.IsDefined(position) && current.Position != position)
            {
                current.TaskbarBarManualPadding = 0;
                current.TaskbarExperience = current.TaskbarExperience with { Arrangement = null };
                current.Position = position;
            }
            if (locked is { } lockValue) current.TaskbarBarPositionLocked = lockValue;
            if (avoidIcons is { } avoidValue) current.TaskbarBarAvoidIcons = avoidValue;
            if (crossAxisOffsetDip is { } offset) current.TaskbarBarCrossAxisOffsetDip = offset;
            if (reset) { current.Position = TaskbarBarPosition.Start; current.TaskbarBarManualPadding = 0; current.TaskbarBarCrossAxisOffsetDip = 0; }
        });
        if (IsActive && Target == BarConfigurationTarget.Taskbar)
            SettingsManager.RaiseLayoutSettingsChanged(SettingsManager.Current.WindowMode, Current.LayoutOrientationMode);
    }
    public void SetLyricsEnabled(bool value) => Update(current => current.LyricsEnabled = value);
    public void SetTwoLineLyricsEnabled(bool value) => Update(current => current.TwoLineLyricsEnabled = value);
    public void SetLyricsSecondaryLineSettings(LyricsSecondaryLineSettings value) => Update(current => current.LyricsSecondaryLine = value);
    public void SetLyricsTextAlignment(LyricsTextAlignment value) => Update(current => current.LyricsTextAlignment = value);
    public void SetLyricsSyllableHighlightEnabled(bool value) => Update(current => current.LyricsSyllableHighlightEnabled = value);
    public void SetLyricsChineseConversion(LyricsChineseConversionMode value) => Update(current => current.LyricsChineseConversion = value);
    public void SetLyricsUnsungOpacityPercent(int value) => Update(current => current.LyricsUnsungOpacityPercent = value);
    public void SetLyricsCharacterSpacingPercent(int value) => Update(current => current.LyricsCharacterSpacingPercent = value);
    public void SetLyricsLineGapPercent(int value) => Update(current => current.LyricsLineGapPercent = value);
    public void SetLyricsFixedWidthEnabled(bool value) => Update(current => current.LyricsFixedWidthEnabled = value);
    public void SetLyricsFixedWidthDip(int value) => Update(current => current.LyricsFixedWidthDip = value);
    public void SetInteractionSettings(GlobalInteractionSettings value)
    {
        if (!IsActive) return;
        if (Target == BarConfigurationTarget.FloatingBar)
            SettingsManager.Current.Interaction = BarDisplaySettings.MergeInteraction(value, SettingsManager.Current.Interaction);
        Update(current => current.Interaction = value);
    }
    public void SetAllowBrowserAndVideoLyrics(bool value) { if (IsActive) SettingsManager.Current.AllowBrowserAndVideoLyrics = value; }
    public void SetLyricsInfoLineFilterEnabled(bool value) { if (IsActive) SettingsManager.Current.LyricsInfoLineFilterEnabled = value; }
    public void SetLyricsSourceSettings(LyricsSourceSettings value) { if (IsActive) SettingsManager.Current.LyricsSource = value; }
    public void SetLyricsArtistSeparators(string value) { if (IsActive) SettingsManager.Current.LyricsArtistSeparators = value; }
    public void SetTrackChangeNotificationSettings(TrackChangeNotificationSettings value) { if (IsActive) SettingsManager.Current.TrackChangeNotification = value; }
    public void SetAudioDeviceSettings(AudioDeviceSettings value) { if (IsActive) SettingsManager.Current.AudioDevices = value; }

    public void Reset(SettingsResetScope scope)
    {
        if (!IsActive) return;
        if (Target == BarConfigurationTarget.Taskbar)
        {
            switch (scope)
            {
                case SettingsResetScope.Lyrics: SettingsManager.ResetLyrics(); break;
                case SettingsResetScope.Interaction: SettingsManager.ResetInteraction(); break;
                case SettingsResetScope.DisplayModes: SettingsManager.ResetDisplayModes(); break;
                case SettingsResetScope.Components: SettingsManager.ResetComponents(); break;
                case SettingsResetScope.ContentLayout: SettingsManager.ResetContentLayout(); break;
                case SettingsResetScope.Appearance: SettingsManager.ResetTaskbarAppearance(); break;
                case SettingsResetScope.Layout: SettingsManager.ResetScreenAndPlacement(); break;
                default: throw new ArgumentOutOfRangeException(nameof(scope));
            }
            return;
        }
        var defaults = SettingsManager.UserDefaults ?? new AppSettings();
        var profile = defaults.FloatingBar.Profile ?? BarDisplaySettings.Capture(defaults);
        var current = SettingsManager.Current.FloatingBar.Profile ?? BarDisplaySettings.Capture(SettingsManager.Current);
        var next = scope switch
        {
            SettingsResetScope.All => profile,
            SettingsResetScope.Appearance => current with
            {
                Appearance = profile.Appearance,
                TaskbarExperience = current.TaskbarExperience with { MediaFontSizePercent = profile.TaskbarExperience.MediaFontSizePercent }
            },
            SettingsResetScope.ContentLayout => current with
            {
                TaskbarExperience = SettingsResetPolicy.Content(current.TaskbarExperience, profile.TaskbarExperience, SettingsManager.Current.Experiments.NewAudioControl),
                SpectrumComponent = profile.SpectrumComponent,
                PerformanceComponent = profile.PerformanceComponent,
                LyricsTextAlignment = profile.LyricsTextAlignment,
                LyricsFixedWidthEnabled = profile.LyricsFixedWidthEnabled,
                LyricsFixedWidthDip = profile.LyricsFixedWidthDip
            },
            SettingsResetScope.Components => current with { SpectrumComponent = profile.SpectrumComponent, PerformanceComponent = profile.PerformanceComponent },
            SettingsResetScope.Interaction => current with { Interaction = profile.Interaction },
            SettingsResetScope.DisplayModes => current with { TaskbarExperience = profile.TaskbarExperience },
            SettingsResetScope.Lyrics => current with
            {
                LyricsEnabled = profile.LyricsEnabled,
                TwoLineLyricsEnabled = profile.TwoLineLyricsEnabled,
                LyricsSecondaryLine = profile.LyricsSecondaryLine,
                LyricsSyllableHighlightEnabled = profile.LyricsSyllableHighlightEnabled,
                LyricsUnsungOpacityPercent = profile.LyricsUnsungOpacityPercent,
                LyricsCharacterSpacingPercent = profile.LyricsCharacterSpacingPercent,
                LyricsLineGapPercent = profile.LyricsLineGapPercent,
                LyricsChineseConversion = profile.LyricsChineseConversion,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        SetFloatingBar(SettingsManager.Current.FloatingBar with { Profile = next });
        if (scope == SettingsResetScope.Interaction)
        {
            var root = SettingsManager.Current;
            var interaction = SettingsResetPolicy.Interaction(root.Interaction, defaults.Interaction, root.Experiments.NewAudioControl);
            root.Interaction = BarDisplaySettings.MergeInteraction(interaction, root.Interaction);
            if (root.Experiments.NewAudioControl) root.AudioDevices = defaults.AudioDevices;
        }
    }
}
