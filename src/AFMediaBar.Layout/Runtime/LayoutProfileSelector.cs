using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Runtime;

/// <summary>
/// Pure selection helpers for the horizontal and vertical layout profiles.
/// </summary>
public static class LayoutProfileSelector
{
    public static LayoutProfileKey ResolveProfileKey(bool vertical) =>
        vertical ? LayoutProfileKey.Vertical : LayoutProfileKey.Horizontal;

    public static LayoutProfile ResolveProfile(LayoutDocument document, bool vertical) =>
        document.Get(ResolveProfileKey(vertical));
}
