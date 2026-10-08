// Validates an edited setting value without touching controls or persistence. Numeric precision follows the setting, not slider acceleration.
using System.Globalization;

namespace AFMediaBar.Classes.Services;

/// <summary>Parses a finite, in-range number and aligns it to the setting's zero-based step grid.</summary>
public static class SettingsNumericInputPolicy
{
    /// <summary>Rejects invalid text and bounds; a valid value is rounded to the declared precision and kept inside the current range.</summary>
    public static bool TryResolve(string text, CultureInfo culture, double minimum, double maximum, double step, out double value)
    {
        value = 0d;
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum ||
            !double.IsFinite(step) || step <= 0d ||
            !double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, culture, out var parsed) ||
            !double.IsFinite(parsed) || parsed < minimum || parsed > maximum)
            return false;
        // A dynamic minimum may be fractional: it must not offset the grid and turn an entered 200 into 200.25.
        value = Math.Clamp(Math.Round(parsed / step, MidpointRounding.AwayFromZero) * step, minimum, maximum);
        return double.IsFinite(value);
    }
}
