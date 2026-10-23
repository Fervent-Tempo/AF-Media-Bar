// 设置编辑上下文的读写边界，任务栏保留原字段，悬浮几何独立保存。
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
    /// <summary>本页写入的配置目标；朝向不产生额外配置。</summary>
    BarConfigurationTarget Target => Context.Mode == SettingsMode.FloatingBar
        ? BarConfigurationTarget.FloatingBar : BarConfigurationTarget.Taskbar;
    /// <summary>修改实验开关，保存的实验偏好保持不变。</summary>
    void SetExperiments(ExperimentalSettings settings)
    {
        if (IsActive) SettingsManager.Current.Experiments = settings;
    }
    /// <summary>明确选择运行宿主，首次选择悬浮时初始化独立几何。</summary>
    void SetWindowMode(AFMediaBar.Classes.Models.Layout.WindowMode mode)
    {
        if (!IsActive) return;
        if (mode == AFMediaBar.Classes.Models.Layout.WindowMode.FloatingBar && SettingsManager.Current.FloatingBar.Profile is null)
            SettingsManager.Current.FloatingBar = SettingsManager.Current.FloatingBar with { Profile = BarDisplaySettings.Capture(SettingsManager.Current) };
        SettingsManager.Current.WindowMode = mode == AFMediaBar.Classes.Models.Layout.WindowMode.FloatingBar
            ? mode : AFMediaBar.Classes.Models.Layout.WindowMode.Taskbar;
    }
    /// <summary>修改独立悬浮宿主偏好。</summary>
    void SetFloatingBar(FloatingBarSettings settings)
    {
        if (IsActive) SettingsManager.Current.FloatingBar = settings;
    }
    /// <summary>Replaces the taskbar section through the existing normalization and event path.</summary>
    void SetTaskbarExperience(TaskbarExperienceSettings settings);
    /// <summary>Replaces application appearance through the current storage contract.</summary>
    void SetAppearance(AppearanceSettings settings);
    /// <summary>Updates taskbar foreground and background while retaining application theme and fonts.</summary>
    void SetTaskbarAppearance(AppearanceSettings settings);
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
    void SetTaskbarPlacement(bool? locked = null, bool? avoidIcons = null, double? crossAxisOffsetDip = null, bool reset = false, TaskbarBarPosition? alignment = null);
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
    /// <summary>通过当前配置上下文更新歌词简繁转换方向。</summary>
    void SetLyricsChineseConversion(LyricsChineseConversionMode mode);
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
    /// <summary>通过当前配置上下文更新显示用的假名过滤。</summary>
    void SetLyricsFuriganaFilterEnabled(bool enabled);
    void SetLyricsLongLineScrollEnabled(bool enabled);
    /// <summary>通过当前配置上下文更新显示用的整行括号隐藏。</summary>
    void SetLyricsBracketSymbolsHidden(bool hidden);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetLyricsSourceSettings(LyricsSourceSettings settings);
    /// <summary>Saves artist separators through the existing lyrics matching contract.</summary>
    void SetLyricsArtistSeparators(string separators);
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetInteractionSettings(GlobalInteractionSettings settings);
    /// <summary>更新跨入口的全局设备策略，不改变系统设备或路由。</summary>
    void SetAudioDeviceSettings(AudioDeviceSettings settings)
    {
        if (IsActive) Current.AudioDevices = settings.Normalize();
    }
    /// <summary>Publishes this setting through the existing normalization contract.</summary>
    void SetTrackChangeNotificationSettings(TrackChangeNotificationSettings settings);
}
