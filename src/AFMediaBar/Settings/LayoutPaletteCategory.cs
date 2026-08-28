namespace AFMediaBar.Settings;

/// <summary>
/// Presentation grouping used by the legacy WPF settings palette.
/// This is intentionally separate from the component-domain category because
/// the palette uses the user-facing label "Controls" and includes containers.
/// </summary>
internal enum ComponentCategory
{
    Media = 0,
    Controls = 1,
    Audio = 2,
    System = 3,
    Layout = 4
}
