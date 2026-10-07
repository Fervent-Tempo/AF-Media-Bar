// Applies horizontal component direction and hover anchors; the control owns all referenced visuals.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>媒体栏横向排布呈现。/ Horizontal arrangement of the media bar.</summary>
public partial class TaskBarMediaControl
{
    private TaskbarArrangement ResolvedTaskbarArrangement => TaskbarArrangementPolicy.ResolveContent(
        SettingsManager.Current.TaskbarExperience.Normalize().Arrangement, SettingsManager.Current.Position);

    private TaskbarArrangement ResolvedTaskbarHoverAlignment => TaskbarArrangementPolicy.Resolve(
        SettingsManager.Current.TaskbarExperience.Normalize().HoverAlignment, SettingsManager.Current.Position);

    private void ApplyTaskbarArrangement()
    {
        var right = !_isVertical && ResolvedTaskbarArrangement == TaskbarArrangement.Right;
        Reorder(TaskbarTransportButtons, right
            ? [TaskbarNextButton, TaskbarPlayPauseButton, TaskbarPreviousButton]
            : [TaskbarPreviousButton, TaskbarPlayPauseButton, TaskbarNextButton]);
        Reorder(TaskbarHoverActions, right
            ? [TaskbarHoverProgress, TaskbarVolumeButton, TaskbarDeviceButton, TaskbarTransportButtons]
            : [TaskbarTransportButtons, TaskbarDeviceButton, TaskbarVolumeButton, TaskbarHoverProgress]);
    }

    private static void Reorder(StackPanel panel, UIElement[] children)
    {
        if (panel.Children.Cast<UIElement>().SequenceEqual(children))
            return;
        panel.Children.Clear();
        foreach (var child in children)
            panel.Children.Add(child);
    }

    private void ApplyTaskbarActionsGeometry(double barWidth, double textLeft, double textWidth)
    {
        TaskbarHoverActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Position must not contribute to the next measure. Margin plus layout rounding fed a
        // one-pixel error back into DesiredSize at fractional widths and alternated every refresh.
        var left = TaskbarArrangementPolicy.ActionsLeft(barWidth, textLeft, textWidth,
            TaskbarHoverActions.DesiredSize.Width, ResolvedTaskbarHoverAlignment);
        var dpiScale = VisualTreeHelper.GetDpi(TaskbarHoverActions).DpiScaleX;
        TaskbarHoverActionsOffset.X = Math.Round(Math.Max(0, left - 5) * dpiScale) / dpiScale;
    }

    private Rect TaskbarRevealRect(double revealedWidth) => new(
        TaskbarArrangementPolicy.RevealLeft(TaskbarHoverLayer.Width, revealedWidth, ResolvedTaskbarHoverAlignment),
        0, revealedWidth, HoverRevealHost.Height);
}
