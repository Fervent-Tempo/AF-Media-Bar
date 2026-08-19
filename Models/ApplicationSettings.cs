namespace AFMediaBar.Models;

[Flags]
internal enum SettingsSection
{
    None = 0,
    General = 1,
    Components = 2,
    Window = 4,
    Placement = 8,
    Appearance = 16,
    Interaction = 32,
    Performance = 64,
    Font = 128,
    Language = 256,
    Layout = 512,
    All = General | Components | Window | Placement | Appearance | Interaction | Performance | Font | Language | Layout
}

internal readonly record struct ApplicationSettings(
    MetricSettings Metrics,
    ThemeSettings Theme,
    FontSettings Font,
    AppLanguage Language,
    WindowSettings Window,
    PlacementSettings Placement,
    LayoutDocument Layout,
    bool StartupEnabled);

internal sealed class SettingsChangedEventArgs(
    ApplicationSettings settings,
    SettingsSection sections) : EventArgs
{
    internal ApplicationSettings Settings { get; } = settings;

    internal SettingsSection Sections { get; } = sections;
}
