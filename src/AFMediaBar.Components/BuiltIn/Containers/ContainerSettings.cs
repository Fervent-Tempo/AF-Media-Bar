namespace AFMediaBar.Components.BuiltIn.Containers;

public enum ComponentFlowOrientation { Automatic = 0, Horizontal = 1, Vertical = 2 }
public enum ComponentContentAlignment { Center = 0, Start = 1, End = 2, Stretch = 3 }
public enum ComponentEasingKind { Linear = 0, EaseOut = 1, EaseInOut = 2 }

public sealed record ComponentAnimationSettings(
    bool Enabled = true,
    int DurationMilliseconds = 220,
    int DelayMilliseconds = 0,
    ComponentEasingKind Easing = ComponentEasingKind.EaseOut);
