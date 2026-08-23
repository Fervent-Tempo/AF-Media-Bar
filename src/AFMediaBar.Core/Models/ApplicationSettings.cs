namespace AFMediaBar.Models;

[Flags]
public enum SettingsSection
{
    None = 0,
    General = 1,
    Window = 4,
    Placement = 8,
    Appearance = 16,
    Interaction = 32,
    Performance = 64,
    Font = 128,
    Language = 256,
    Layout = 512,
    All = General | Window | Placement | Appearance | Interaction | Performance | Font | Language | Layout
}

public readonly record struct ApplicationSettings(
    MetricSettings Metrics,
    ThemeSettings Theme,
    FontSettings Font,
    AppLanguage Language,
    WindowSettings Window,
    PlacementSettings Placement,
    LayoutDocument Layout,
    bool StartupEnabled);

public sealed class SettingsChangedEventArgs(
    ApplicationSettings settings,
    SettingsSection sections) : EventArgs
{
    public ApplicationSettings Settings { get; } = settings;

    public SettingsSection Sections { get; } = sections;
}
