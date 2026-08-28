namespace AFMediaBar.Components.Abstractions;

public enum ComponentKind { Container = 0, Functional = 1 }

public enum ComponentCategory { Container = 0, Media = 1, Playback = 2, Audio = 3, System = 4, Layout = 5 }

[Flags]
public enum ComponentCapabilities
{
    None = 0,
    Display = 1,
    Invoke = 2,
    Adjust = 4,
    Popup = 8,
    Interactive = Invoke | Adjust | Popup
}
