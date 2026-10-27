// 设置子页只恢复自身拥有的字段；调用方负责当前模式几何的投影和一次性发布。
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>独立设置子页的重置边界，不执行平台操作或持久化。</summary>
internal static class SettingsSectionResetPolicy
{
    public static bool Handles(SettingsResetScope scope) => scope is SettingsResetScope.BarAppearance or SettingsResetScope.TextAndLyrics
        or SettingsResetScope.QuickControls or SettingsResetScope.FullPanel or SettingsResetScope.QuickLaunch
        or SettingsResetScope.Spectrum or SettingsResetScope.Performance or SettingsResetScope.BluetoothBattery
        or SettingsResetScope.MediaSource or SettingsResetScope.TrackNotifications;

    public static AppSettings Apply(AppSettings current, AppSettings defaults, SettingsResetScope scope)
    {
        var next = current.Clone();
        var rest = next.TaskbarExperience;
        var target = defaults.TaskbarExperience;
        switch (scope)
        {
            case SettingsResetScope.BarAppearance:
                next.Appearance = next.Appearance with
                {
                    TaskbarBackgroundMaterial = defaults.Appearance.TaskbarBackgroundMaterial,
                    TaskbarBackgroundOpacityPercent = defaults.Appearance.TaskbarBackgroundOpacityPercent,
                    ShowTaskbarBackgroundWhenIdle = defaults.Appearance.ShowTaskbarBackgroundWhenIdle,
                    ShowTaskbarBackgroundTint = defaults.Appearance.ShowTaskbarBackgroundTint,
                    ShowTaskbarBackgroundBorder = defaults.Appearance.ShowTaskbarBackgroundBorder,
                    ShowTaskbarBackgroundTexture = defaults.Appearance.ShowTaskbarBackgroundTexture,
                    TaskbarFrostedStyle = defaults.Appearance.TaskbarFrostedStyle
                };
                next.TaskbarExperience = rest with { LengthMode = target.LengthMode, FixedLengthDip = target.FixedLengthDip,
                    ComponentSpacingDip = target.ComponentSpacingDip, Arrangement = target.Arrangement };
                break;
            case SettingsResetScope.QuickControls:
                next.TaskbarExperience = rest with { HoverLayerEnabled = target.HoverLayerEnabled, HoverControls = target.HoverControls,
                    Density = target.Density, HoverButtonSpacingDip = target.HoverButtonSpacingDip };
                break;
            case SettingsResetScope.FullPanel:
                next.TaskbarExperience = rest with { FullLayerEnabled = target.FullLayerEnabled, FullPanel = target.FullPanel,
                    FullPanelEntryVisible = target.FullPanelEntryVisible, FullPanelEntryTrigger = target.FullPanelEntryTrigger,
                    FullPanelEntryHoverDelayMilliseconds = target.FullPanelEntryHoverDelayMilliseconds };
                break;
            case SettingsResetScope.TextAndLyrics:
                next.Appearance = next.Appearance with { PlayerForegroundMode = defaults.Appearance.PlayerForegroundMode };
                next.TaskbarExperience = rest with { MediaFontSizePercent = target.MediaFontSizePercent,
                    MediaTextAlignment = target.MediaTextAlignment, ContentLayout = target.ContentLayout };
                next.LyricsEnabled = defaults.LyricsEnabled;
                next.TwoLineLyricsEnabled = defaults.TwoLineLyricsEnabled;
                next.LyricsSecondaryLine = defaults.LyricsSecondaryLine;
                next.LyricsTextAlignment = defaults.LyricsTextAlignment;
                next.LyricsFixedWidthEnabled = defaults.LyricsFixedWidthEnabled;
                next.LyricsFixedWidthDip = defaults.LyricsFixedWidthDip;
                next.LyricsLongLineScrollEnabled = defaults.LyricsLongLineScrollEnabled;
                next.LyricsSyllableHighlightEnabled = defaults.LyricsSyllableHighlightEnabled;
                next.LyricsUnsungOpacityPercent = defaults.LyricsUnsungOpacityPercent;
                next.LyricsCharacterSpacingPercent = defaults.LyricsCharacterSpacingPercent;
                next.LyricsLineGapPercent = defaults.LyricsLineGapPercent;
                next.LyricsInfoLineFilterEnabled = defaults.LyricsInfoLineFilterEnabled;
                next.LyricsFuriganaFilterEnabled = defaults.LyricsFuriganaFilterEnabled;
                next.LyricsBracketSymbolsHidden = defaults.LyricsBracketSymbolsHidden;
                next.LyricsChineseConversion = defaults.LyricsChineseConversion;
                next.AllowBrowserAndVideoLyrics = defaults.AllowBrowserAndVideoLyrics;
                next.LyricsSource = defaults.LyricsSource;
                next.LyricsArtistSeparators = defaults.LyricsArtistSeparators;
                break;
            case SettingsResetScope.QuickLaunch:
                next.QuickLaunch = defaults.QuickLaunch;
                next.Appearance = next.Appearance with { IdleNoteColor = defaults.Appearance.IdleNoteColor };
                break;
            case SettingsResetScope.Spectrum: next.SpectrumComponent = defaults.SpectrumComponent; break;
            case SettingsResetScope.Performance: next.PerformanceComponent = defaults.PerformanceComponent; break;
            case SettingsResetScope.BluetoothBattery:
                next.BluetoothBatteryWidget = defaults.BluetoothBatteryWidget with { Enabled = current.BluetoothBatteryWidget.Enabled };
                break;
            case SettingsResetScope.MediaSource:
                next.SmtcSourceFilter = defaults.SmtcSourceFilter;
                next.MediaSelection = defaults.MediaSelection;
                break;
            case SettingsResetScope.TrackNotifications: next.TrackChangeNotification = defaults.TrackChangeNotification; break;
            default: throw new ArgumentOutOfRangeException(nameof(scope));
        }
        return next;
    }
}
