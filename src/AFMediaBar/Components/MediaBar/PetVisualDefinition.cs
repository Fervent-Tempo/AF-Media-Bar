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
    internal const string BodyPath = "M 13,16 L 15,5 Q 15.3,3.5 16.5,5 L 23,11 C 28.5,9 36,9 41,11 L 48,5 Q 49.2,3.5 49.5,5 L 51,16 C 57,21 60,28 60,35 C 60,50 47.5,61 32,61 C 16.5,61 4,50 4,35 C 4,27 7,21 13,16 Z";

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
