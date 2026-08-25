using System.Windows;
using AFMediaBar.Models;

namespace AFMediaBar.Adapters;

internal static class WpfFontSettingsAdapter
{
    internal static FontWeight ResolveTitleWeight(int weight) =>
        FontWeight.FromOpenTypeWeight(FontSettings.NormalizeWeight(weight));

    internal static FontWeight ResolveBodyWeight(int weight) =>
        FontWeight.FromOpenTypeWeight(
            FontSettings.NormalizeWeight(weight - 200));
}
