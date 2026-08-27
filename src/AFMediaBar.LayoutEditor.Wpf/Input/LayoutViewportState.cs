using System.Windows;

namespace AFMediaBar.LayoutEditor.Wpf.Input;

/// <summary>
/// Viewport-only state for the WPF layout editor. It contains no controls or
/// rendering objects, so panning and zoom math can be reused and tested apart
/// from SettingsWindow.
/// </summary>
public sealed class LayoutViewportState
{
    public const double MinScale = 0.4;
    public const double MaxScale = 3.0;

    public Point Translate { get; private set; }

    public double Scale { get; private set; } = 1.0;

    public bool IsCentered { get; private set; }

    public void Set(Point translate, double scale)
    {
        Translate = translate;
        Scale = Math.Clamp(scale, MinScale, MaxScale);
    }

    public void MarkCentered() => IsCentered = true;

    public void ResetCentered() => IsCentered = false;

    public Point ZoomAround(Point viewportCenter, int wheelDelta)
    {
        var factor = wheelDelta > 0 ? 1.15 : 1 / 1.15;
        var target = Math.Clamp(Scale * factor, MinScale, MaxScale);
        if (target == Scale)
        {
            return Translate;
        }

        var next = new Point(
            viewportCenter.X - (viewportCenter.X - Translate.X) * (target / Scale),
            viewportCenter.Y - (viewportCenter.Y - Translate.Y) * (target / Scale));
        Set(next, target);
        return next;
    }
}
