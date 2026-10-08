// Resolves settings-page entrance parameters without owning WPF elements or changing application-wide motion.
namespace AFMediaBar.Classes.Services;

/// <summary>Entrance parameters for a settings content surface.</summary>
public readonly record struct SettingsReveal(bool ShouldAnimate, TimeSpan Delay, TimeSpan Duration, double OffsetY);

/// <summary>Uses an upward entrance for normal motion, a fade for reduced motion, and no clocks for instant motion.</summary>
public static class SettingsRevealPolicy
{
    /// <summary>Small vertical offset in DIP; content enters as one surface without staggered cards.</summary>
    public const double EntranceOffsetDip = 28d;

    /// <summary>Resolves one entrance; the block index is retained for callers and adds no delay.</summary>
    public static SettingsReveal Resolve(int blockIndex, MotionProfile motion) => motion.Mode switch
    {
        MotionMode.Full => new(true, TimeSpan.Zero, TimeSpan.FromMilliseconds(280), EntranceOffsetDip),
        MotionMode.Reduced => new(true, TimeSpan.Zero, motion.StandardDuration, 0d),
        _ => default
    };
}
