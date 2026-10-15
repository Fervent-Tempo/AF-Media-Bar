// 竖向实际宽度布局与快捷控制弹层；控件在卸载时关闭 Popup 并归还共享视觉树。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private Popup? _verticalHoverPopup;
    private bool _verticalHostConfigured;
    private bool _verticalTaskbarOnLeft;
    private double _verticalPerformanceLength = 44;
    private double _verticalSpectrumLength = 28;
    private bool VerticalTextRequired => _isVertical && _snapshot.IsConnected;
    private double VerticalPrimaryFontSize => VerticalTaskbarPresentationPolicy.PrimaryFontSizeDip *
        ExperimentalAudioPolicy.Experience(PresentationSettings.TaskbarExperience, PresentationSettings.Experiments.NewAudioControl).Normalize().MediaFontSizePercent / 100d;
    private double VerticalSecondaryFontSize => VerticalTaskbarPresentationPolicy.SecondaryFontSizeDip *
        ExperimentalAudioPolicy.Experience(PresentationSettings.TaskbarExperience, PresentationSettings.Experiments.NewAudioControl).Normalize().MediaFontSizePercent / 100d;

    public void ApplyVerticalTaskbarSide(bool onLeft)
    {
        if (_verticalTaskbarOnLeft == onLeft) return;
        HideTaskbarHoverLayer(immediate: true);
        AnimateArtworkHover(false, immediate: true);
        _verticalTaskbarOnLeft = onLeft;
    }

    private void ConfigureVerticalHost(bool vertical)
    {
        if (_verticalHostConfigured == vertical) return;
        HideTaskbarHoverLayer(immediate: true);
        AnimateArtworkHover(false, immediate: true);
        _verticalHostConfigured = vertical;
        TaskbarHoverActions.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        TaskbarTransportButtons.Orientation = TaskbarHoverActions.Orientation;
        TaskbarHoverActionsOffset.X = 0;
        TaskbarHoverActionsOffset.Y = 0;
        TaskbarHoverLayer.MouseEnter -= TaskbarHoverLayer_MouseEnter;
        TaskbarHoverLayer.MouseLeave -= TaskbarHoverLayer_MouseLeave;
        if (vertical)
        {
            TaskbarHoverLayer.MouseEnter += TaskbarHoverLayer_MouseEnter;
            TaskbarHoverLayer.MouseLeave += TaskbarHoverLayer_MouseLeave;
        }
        TaskbarPerformanceItems.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        if (vertical)
        {
            TaskbarHoverLayer.Resources = Resources;
            TaskbarHoverLayer.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
            TaskbarHoverLayer.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            TaskbarHoverLayer.BorderThickness = new Thickness(1);
        }
        else
        {
            TaskbarHoverLayer.ClearValue(Border.BackgroundProperty);
            TaskbarHoverLayer.ClearValue(Border.BorderBrushProperty);
            TaskbarHoverLayer.BorderThickness = new Thickness(0);
        }
        if (!vertical)
        {
            _wheelTooltip.Placement = PlacementMode.Top;
            _artworkWheelTooltip.Placement = PlacementMode.Top;
            _quickLaunchTooltip.Placement = PlacementMode.Top;
            TaskbarRestProgress.RenderTransform = Transform.Identity;
            TaskbarRestProgress.HorizontalAlignment = HorizontalAlignment.Stretch;
            TaskbarRestProgress.VerticalAlignment = VerticalAlignment.Bottom;
            TaskbarRestProgress.Width = double.NaN;
            TaskbarRestProgress.Height = 2;
            TaskbarDirectFullPanelHandle.Height = 8;
            TaskbarDirectFullPanelHandle.HorizontalAlignment = HorizontalAlignment.Left;
            TaskbarDirectFullPanelHandle.VerticalAlignment = VerticalAlignment.Top;
            if (TaskbarDirectFullPanelHandle.Content is Border handle) { handle.Width = 28; handle.Height = 4; }
            foreach (var surface in new FrameworkElement[] { TaskbarSpectrumHoverSurface, TaskbarPerformanceHoverSurface, TaskbarOutputDeviceHoverSurface, TaskbarInputDeviceHoverSurface, TaskbarVolumeHoverSurface })
                surface.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private LyricsPresentationFrame BuildVerticalMetadataFrame() => new(
        true, _actualTitle + "\n" + _actualArtist, -1,
        TaskbarExperiencePolicy.FormatMetadataTitle(_actualTitle, _actualArtist, PresentationSettings.TaskbarExperience.ContentLayout), PresentationSettings.TaskbarExperience.ContentLayout == TaskbarContentLayout.CompactInline
            ? string.Empty : _actualArtist,
        string.Empty, string.Empty, false, 0, null, _snapshot.IsPlaying);

    private TaskbarRestLayout ResolveVerticalLayout(double primaryLength)
    {
        var experience = ExperimentalAudioPolicy.Experience(PresentationSettings.TaskbarExperience, PresentationSettings.Experiments.NewAudioControl).Normalize();
        var visibility = ResolveRestVisibility(experience);
        var metrics = TaskbarDensityMetrics.From(experience.Density);
        return TaskbarRestLayoutPolicy.Arrange(
            TaskbarRestLayoutPolicy.ResolveOrder(experience.RestComponentOrder),
            component => VerticalComponentLength(component, metrics),
            component => TaskbarRestLayoutPolicy.IsVisible(component, visibility),
            2, primaryLength, VerticalSectionGap, 2,
            ResolvedTaskbarArrangement == TaskbarContentArrangement.Right);
    }

    private double VerticalSectionGap => TaskbarExperiencePolicy.ResolveSectionGap(
        PresentationSettings.TaskbarExperience.Density,
        PresentationSettings.TaskbarExperience.ComponentSpacingDip) *
        PresentationSettings.LayoutLengthScalePercent / 100d;

    private double VerticalComponentLength(TaskbarRestComponent component, TaskbarDensityMetrics metrics) => component switch
    {
        TaskbarRestComponent.Artwork => Math.Max(1, MainBorder.Width - 4),
        TaskbarRestComponent.Spectrum => _verticalSpectrumLength,
        TaskbarRestComponent.Performance => _verticalPerformanceLength,
        TaskbarRestComponent.OutputDevice or TaskbarRestComponent.InputDevice or TaskbarRestComponent.Volume => metrics.ButtonSize + 2,
        _ => 0
    };

    private MediaBarSizeRequest BuildVerticalSizeRequest(MediaBarSizeRequest request, bool lyricsVisible, string current, string secondary)
    {
        var experience = ExperimentalAudioPolicy.Experience(PresentationSettings.TaskbarExperience, PresentationSettings.Experiments.NewAudioControl).Normalize();
        double Measure(string text, double fontSize) => VerticalTaskbarPresentationPolicy.MeasureText(
            text, fontSize, lyricsVisible ? PresentationSettings.LyricsCharacterSpacingPercent : 0,
            sideways => MeasureTextWidthExact(sideways, SongTitle) * fontSize / Math.Max(1, SongTitle.FontSize));
        if (!lyricsVisible)
            current = TaskbarExperiencePolicy.FormatMetadataTitle(_actualTitle, _actualArtist, experience.ContentLayout);
        var textLength = lyricsVisible && PresentationSettings.LyricsFixedWidthEnabled
            ? LyricsFixedWidth.Normalize(PresentationSettings.LyricsFixedWidthDip)
            : Math.Max(Measure(current, VerticalPrimaryFontSize),
                Measure(lyricsVisible ? secondary : _actualArtist, lyricsVisible ? VerticalPrimaryFontSize : VerticalSecondaryFontSize));
        var visibility = ResolveRestVisibility(experience);
        var components = TaskbarRestLayoutPolicy.ResolveOrder(experience.RestComponentOrder)
            .Where(component => TaskbarRestLayoutPolicy.IsVisible(component, visibility)).ToArray();
        var metrics = TaskbarDensityMetrics.From(experience.Density);
        double Content(double text) => TaskbarExperiencePolicy.CalculateRestWidth(
            components, 2, text, 2, VerticalSectionGap,
            component => VerticalComponentLength(component, metrics), double.PositiveInfinity);
        _minimumPrimaryLength = Content(24);
        var length = _snapshot.IsConnected
            ? TaskbarExperiencePolicy.ResolvePrimaryLength(Content(textLength), _minimumPrimaryLength,
                double.PositiveInfinity, experience.LengthMode, experience.FixedLengthDip)
            : Content(0);
        return request with { Width = MainBorder.Width, Height = Math.Max(1, length) };
    }

    private void ApplyVerticalSectionGeometry(double primaryLength)
    {
        _verticalSpectrumLength = Math.Min(24, PresentationSettings.SpectrumComponent.Normalize().ContentHeightDip) + 4;
        var width = Math.Max(1, MainBorder.Width);
        var innerWidth = Math.Max(1, width - 4);
        var experience = ExperimentalAudioPolicy.Experience(PresentationSettings.TaskbarExperience, PresentationSettings.Experiments.NewAudioControl).Normalize();
        var tooltipPlacement = _verticalTaskbarOnLeft ? PlacementMode.Right : PlacementMode.Left;
        _wheelTooltip.Placement = tooltipPlacement;
        _artworkWheelTooltip.Placement = tooltipPlacement;
        _quickLaunchTooltip.Placement = tooltipPlacement;
        SongTitle.FontSize = VerticalPrimaryFontSize;
        SongArtist.FontSize = VerticalSecondaryFontSize;
        SongImageBorder.Width = innerWidth;
        SongImageBorder.Height = innerWidth;
        SongImagePlaceholder.FontSize = Math.Max(12, innerWidth * 0.6);
        var layout = ResolveVerticalLayout(primaryLength);
        _isRestLayerEmpty = layout.IsEmpty;
        ApplyRestComponentVisibility(layout);
        void Place(FrameworkElement element, TaskbarRestComponent component)
        {
            if (layout.Find(component) is not { } placement) return;
            element.Width = innerWidth;
            element.Height = placement.Width;
            element.HorizontalAlignment = HorizontalAlignment.Left;
            element.VerticalAlignment = VerticalAlignment.Top;
            if (element.Parent is Canvas)
            {
                Canvas.SetLeft(element, 2);
                Canvas.SetTop(element, placement.Left);
            }
            else element.Margin = new Thickness(2, placement.Left, 0, 0);
        }
        if (!IsArtworkZoomActive) Place(SongImageBorder, TaskbarRestComponent.Artwork);
        if (_artworkZoomSubstitute is { } substitute) Place(substitute, TaskbarRestComponent.Artwork);
        Place(SongInfoStackPanel, TaskbarRestComponent.MediaText);
        Place(SongInfoHoverOverlay, TaskbarRestComponent.MediaText);
        Place(TaskbarSpectrumHoverSurface, TaskbarRestComponent.Spectrum);
        Place(TaskbarPerformanceHoverSurface, TaskbarRestComponent.Performance);
        Place(TaskbarOutputDeviceHoverSurface, TaskbarRestComponent.OutputDevice);
        Place(TaskbarInputDeviceHoverSurface, TaskbarRestComponent.InputDevice);
        Place(TaskbarVolumeHoverSurface, TaskbarRestComponent.Volume);
        var textLength = layout.TextWidth;
        SongInfoSurface.Width = innerWidth;
        SongInfoSurface.Height = textLength;
        SongLyricsPanel.Width = innerWidth;
        SongLyricsPanel.Height = textLength;
        // Web 不可用时保留未旋转的简化元数据。
        SongTitleContainer.Width = innerWidth;
        SongArtistContainer.Width = innerWidth;
        SongTitle.Width = innerWidth;
        SongArtist.Width = innerWidth;
        SongArtistContainer.Visibility = string.IsNullOrEmpty(_actualArtist) ? Visibility.Collapsed : Visibility.Visible;
        SongArtistContainer.Margin = new Thickness(0, 2, 0, 0);
        TaskbarPerformanceSurface.Width = innerWidth;
        TaskbarPerformanceSurface.Padding = new Thickness(1);
        foreach (var cell in _performanceCells.Values) cell.Width = Math.Max(1, innerWidth - 2);
        var buttonSize = Math.Min(innerWidth, Math.Max(24, TaskbarDensityMetrics.From(experience.Density).ButtonSize));
        foreach (var surface in new[] { TaskbarOutputDeviceSurface, TaskbarInputDeviceSurface, TaskbarVolumeSurface })
        {
            surface.Width = buttonSize;
            surface.Height = buttonSize;
        }
        ConfigureVerticalHoverGeometry();
        var progress = _snapshot.Duration > 0 && experience.RestProgressVisible && layout.Find(TaskbarRestComponent.MediaText) is not null;
        TaskbarRestProgress.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
        TaskbarRestProgress.HorizontalAlignment = HorizontalAlignment.Left;
        TaskbarRestProgress.VerticalAlignment = VerticalAlignment.Top;
        TaskbarRestProgress.Width = textLength;
        TaskbarRestProgress.Height = 2;
        TaskbarRestProgress.Margin = new Thickness(_verticalTaskbarOnLeft ? width - 2 : 2, layout.TextLeft, 0, 0);
        TaskbarRestProgress.RenderTransform = new RotateTransform(90);
        TaskbarDirectFullPanelHandle.HorizontalAlignment = HorizontalAlignment.Left;
        TaskbarDirectFullPanelHandle.VerticalAlignment = VerticalAlignment.Top;
        TaskbarDirectFullPanelHandle.Width = 8;
        TaskbarDirectFullPanelHandle.Height = Math.Max(24, textLength);
        TaskbarDirectFullPanelHandle.Margin = new Thickness(_verticalTaskbarOnLeft ? width - 8 : 0, layout.TextLeft, 0, 0);
        if (TaskbarDirectFullPanelHandle.Content is Border handle)
        {
            handle.Width = 4;
            handle.Height = 28;
        }
        ApplyWebLyricsStyle();
        if (_verticalHoverPopup?.IsOpen == true) RefreshArtworkPopupPosition(_verticalHoverPopup);
    }

    private void ConfigureVerticalHoverGeometry()
    {
        var experience = ExperimentalAudioPolicy.Experience(PresentationSettings.TaskbarExperience, PresentationSettings.Experiments.NewAudioControl).Normalize();
        var size = Math.Max(24, TaskbarDensityMetrics.From(experience.Density).ButtonSize);
        var count = 0;
        foreach (FrameworkElement group in TaskbarHoverActions.Children)
        {
            var items = group is StackPanel panel ? panel.Children.Cast<FrameworkElement>() : [group];
            foreach (var item in items)
            {
                item.Width = size;
                item.Height = size;
                item.Margin = new Thickness(0, item.Visibility == Visibility.Visible && count++ > 0 ? experience.HoverButtonSpacingDip : 0, 0, 0);
            }
        }
        TaskbarHoverLayer.Width = size + 12;
        TaskbarHoverActionsOffset.X = 0;
        TaskbarHoverActionsOffset.Y = 0;
        TaskbarHoverActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        TaskbarHoverLayer.Height = Math.Max(24, TaskbarHoverActions.DesiredSize.Height + 18);
    }

    private void ShowVerticalHoverLayer()
    {
        ConfigureVerticalHoverGeometry();
        _verticalHoverPopup ??= new Popup
        {
            AllowsTransparency = true,
            StaysOpen = true,
            Placement = PlacementMode.Custom,
            PlacementTarget = SongInfoStackPanel,
            CustomPopupPlacementCallback = (_, _, _) =>
            {
                var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var origin = SongInfoStackPanel.PointToScreen(new Point());
                var work = GetPopupMonitorWorkArea(origin);
                var x = _verticalTaskbarOnLeft ? origin.X + SongInfoStackPanel.Width * dpi : origin.X - TaskbarHoverLayer.Width * dpi;
                var y = origin.Y + (SongInfoStackPanel.Height - TaskbarHoverLayer.Height) * dpi / 2;
                x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - TaskbarHoverLayer.Width * dpi));
                y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - TaskbarHoverLayer.Height * dpi));
                return [new CustomPopupPlacement(new Point(x - origin.X, y - origin.Y), PopupPrimaryAxis.None)];
            }
        };
        HoverRevealHost.Children.Remove(TaskbarHoverLayer);
        _verticalHoverPopup.Child = TaskbarHoverLayer;
        _isTaskbarHoverVisible = true;
        _hoverHideFallbackTimer.Stop();
        TaskbarHoverLayer.Opacity = 1;
        _verticalHoverPopup.IsOpen = true;
        RaisePopupAboveTaskbar(TaskbarHoverLayer);
        if (CurrentMotion.UseTransitions)
            TaskbarHoverLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, CurrentMotion.FastDuration));
    }

    private void CloseVerticalHoverLayer()
    {
        if (_verticalHoverPopup is null) return;
        TaskbarHoverLayer.BeginAnimation(OpacityProperty, null);
        TaskbarHoverLayer.Opacity = 1;
        _verticalHoverPopup.IsOpen = false;
        _verticalHoverPopup.Child = null;
        if (!HoverRevealHost.Children.Contains(TaskbarHoverLayer)) HoverRevealHost.Children.Add(TaskbarHoverLayer);
        _isTaskbarHoverVisible = false;
    }

    private static RECT GetPopupMonitorWorkArea(Point physicalPoint)
    {
        var monitor = MonitorFromPoint(new POINT { X = (int)physicalPoint.X, Y = (int)physicalPoint.Y }, 2);
        var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
        return GetMonitorInfo(monitor, ref info) ? info.rcWork : new RECT
        {
            Left = (int)physicalPoint.X - 4096,
            Top = (int)physicalPoint.Y - 4096,
            Right = (int)physicalPoint.X + 4096,
            Bottom = (int)physicalPoint.Y + 4096
        };
    }

    private static void RaisePopupAboveTaskbar(Visual child)
    {
        if (PresentationSource.FromVisual(child) is HwndSource source)
            SetWindowPos(source.Handle, -1, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002);
    }
}
