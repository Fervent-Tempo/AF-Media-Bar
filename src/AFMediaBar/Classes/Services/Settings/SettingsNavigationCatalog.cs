namespace AFMediaBar.Classes.Services.Settings;

/// <summary>设置分区与子页的顺序；不保存用户配置。</summary>
public sealed record SettingsSectionDefinition(string Id, string TitleKey, string IconName, IReadOnlyList<SettingsPageKey> Pages);

/// <summary>按任务组织导航，页面身份仍由页面目录唯一维护。</summary>
public static class SettingsNavigationCatalog
{
    /// <summary>设置一级入口及其独立子页。</summary>
    public static IReadOnlyList<SettingsSectionDefinition> Sections { get; } = Array.AsReadOnly(new[]
    {
        new SettingsSectionDefinition("start", "Settings.Section.Start", "Sparkle24", [SettingsPageKey.QuickStart]),
        new SettingsSectionDefinition("bar", "Settings.Section.Bar", "WindowConsole20", [SettingsPageKey.ScreenAndPlacement, SettingsPageKey.Appearance, SettingsPageKey.RestState, SettingsPageKey.Components, SettingsPageKey.FullPanel]),
        new SettingsSectionDefinition("content", "Settings.Section.Content", "Layer24", [SettingsPageKey.Lyrics, SettingsPageKey.Widgets, SettingsPageKey.Spectrum, SettingsPageKey.Performance, SettingsPageKey.BluetoothBattery]),
        new SettingsSectionDefinition("playback", "Settings.Section.Playback", "Headphones24", [SettingsPageKey.MediaAndNotifications, SettingsPageKey.DeviceManagement]),
        new SettingsSectionDefinition("interaction", "Settings.Section.Interaction", "HandDraw24", [SettingsPageKey.Interaction, SettingsPageKey.TrackNotifications]),
        new SettingsSectionDefinition("app", "Settings.Section.App", "Settings24", [SettingsPageKey.ApplicationAppearance, SettingsPageKey.Application, SettingsPageKey.Configuration, SettingsPageKey.Diagnostics, SettingsPageKey.MemoryTools]),
        new SettingsSectionDefinition("experimental", "Common.Page.Experimental", "Beaker24", [SettingsPageKey.Experimental])
    });

    /// <summary>页脚页面没有所属分区。</summary>
    public static SettingsSectionDefinition? SectionFor(SettingsPageKey page) => Sections.FirstOrDefault(section => section.Pages.Contains(page));
}
