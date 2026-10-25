// SVG 与 WPF 回退共用自绘轮廓和配色，避免两种呈现出现不同角色。
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Components;

internal readonly record struct PetColors(string Body, string Eyes, bool HighContrast);

internal static class PetVisualDefinition
{
    internal const string BodyPath = "M 24,13 C 22.6,13.45 21.8,12.2 20.8,10.7 C 19.5,8.7 18.6,6.8 17.9,6.8 C 17.3,6.8 16.9,8.5 16.2,10 C 14.7,13.4 13.3,15.8 11.6,18.2 C 7.3,24.4 5,29.5 5,36.2 C 5,50.8 17.1,61 32,61 C 46.9,61 59,50.8 59,36.2 C 59,29.1 56.5,23.1 52.3,17.2 C 50.8,15.1 49.4,12.3 48.2,9.3 C 47.6,7.8 47.2,6.1 46.6,6.1 C 45.9,6.1 44.7,8.6 43.4,10.7 C 42.6,12 41.6,13.5 40,13 C 35,11.4 29,11.4 24,13 Z";

    internal static PetColors Colors(PetComponentSettings settings)
    {
        if (SystemParameters.HighContrast)
            return new(ColorHex.Format(SystemColors.WindowColor), ColorHex.Format(SystemColors.WindowTextColor), true);
        var body = settings.Normalize().FillColorHex;
        ColorHex.TryParse(body, out var color);
        var lightEyes = PlayerForegroundPolicy.Resolve([color])?.UsesLightText != false;
        return new(body, lightEyes ? "#FFFFFF" : "#1C1C1C", false);
    }
}
