// Owns the small, interruptible WPF transitions used by settings-page navigation; it does not create or navigate pages.
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AFMediaBar.Classes.Utils;

/// <summary>Animates the incoming settings content and a short-lived snapshot of the departing content with one Bézier curve.</summary>
public static class SettingsNavigationAnimator
{
    /// <summary>Creates an interruptible scalar animation using the supplied WPF Bézier key spline.</summary>
    public static DoubleAnimationUsingKeyFrames CreateSpline(double from, double to, TimeSpan duration, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.Zero,
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.Stop
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
        return animation;
    }

    /// <summary>Slides an element from its prepared horizontal offset without changing its layout or scroll viewport.</summary>
    public static void Slide(UIElement element, double offset, TimeSpan duration, KeySpline spline)
    {
        if (duration <= TimeSpan.Zero || offset == 0d) return;
        var translation = EnsureTranslation(element);
        if (translation is null) return;

        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.X = 0d;
        translation.BeginAnimation(
            TranslateTransform.XProperty,
            CreateSpline(offset, 0d, duration, spline),
            HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Places an incoming viewport offscreen before its first layout frame, or restores it when navigation is interrupted.</summary>
    public static void PrepareSlide(UIElement element, double offset)
    {
        var translation = EnsureTranslation(element);
        if (translation is null) return;
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.X = offset;
    }

    private static TranslateTransform? EnsureTranslation(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform translation) return translation;
        if (element.RenderTransform is not null && element.RenderTransform != Transform.Identity) return null;
        translation = new TranslateTransform();
        element.RenderTransform = translation;
        return translation;
    }

    /// <summary>Moves a departing snapshot out of its viewport and releases it when the animation finishes.</summary>
    public static void SlideOut(UIElement element, double offset, TimeSpan duration, KeySpline spline, Action completed)
    {
        var translation = new TranslateTransform();
        element.RenderTransform = translation;
        if (duration <= TimeSpan.Zero || offset == 0d)
        {
            completed();
            return;
        }

        var animation = CreateSpline(0d, offset, duration, spline);
        animation.Completed += (_, _) => completed();
        translation.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    /// <summary>Uses opacity alone when the system asks for reduced motion.</summary>
    public static void Fade(UIElement element, TimeSpan duration, KeySpline spline)
    {
        if (duration <= TimeSpan.Zero) return;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1d;
        element.BeginAnimation(
            UIElement.OpacityProperty,
            CreateSpline(0d, 1d, duration, spline),
            HandoffBehavior.SnapshotAndReplace);
    }
}
