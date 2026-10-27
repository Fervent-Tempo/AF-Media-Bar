// 竖向实际宽度布局与栏内快捷控制；封面弹层沿用控件统一生命周期。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private bool _verticalHostConfigured;
    private bool _verticalTaskbarOnLeft;
    private double _verticalPerformanceLength = 44;
    private double _verticalSpectrumLength = 28;
    private bool VerticalTextRequired => _isVertical && _snapshot.IsConnected;
    private double VerticalPrimaryFontSize => VerticalTaskbarPresentationPolicy.PrimaryFontSizeDip *
        PresentationSettings.TaskbarExperience.Normalize().MediaFontSizePercent / 100d * _textBudget.FontScale;
    private double VerticalSecondaryFontSize => VerticalTaskbarPresentationPolicy.SecondaryFontSizeDip *
        PresentationSettings.TaskbarExperience.Normalize().MediaFontSizePercent / 100d * _textBudget.FontScale;

    public void ApplyVerticalTaskbarSide(bool onLeft)
    {
        if (_verticalTaskbarOnLeft == onLeft) return;
        HideTaskbarHoverLayer(immediate: true);
        _artworkHover.AnimateArtworkHover(false, immediate: true);
        _verticalTaskbarOnLeft = onLeft;
        if (_isVertical)
        {
            ConfigureSpectrum();
            ApplyVerticalSectionGeometry(MainBorder.Height);
        }
    }

    private void ConfigureVerticalHost(bool vertical)
    {
        if (_verticalHostConfigured == vertical) return;
        HideTaskbarHoverLayer(immediate: true);
        _artworkHover.AnimateArtworkHover(false, immediate: true);
        _verticalHostConfigured = vertical;
        TaskbarHoverActions.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        TaskbarTransportButtons.Orientation = TaskbarHoverActions.Orientation;
        TaskbarHoverActionsOffset.X = 0;
        TaskbarHoverActionsOffset.Y = 0;
        TaskbarPerformanceItems.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        TaskbarBatteryContent.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
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
            HoverRevealHost.VerticalAlignment = VerticalAlignment.Center;
            TaskbarHoverContent.Margin = new Thickness(5, 0, 6, 0);
            TaskbarHoverActions.HorizontalAlignment = HorizontalAlignment.Left;
            foreach (var surface in new FrameworkElement[] { TaskbarSpectrumHoverSurface, TaskbarPerformanceHoverSurface, TaskbarBatterySurface, TaskbarOutputDeviceHoverSurface, TaskbarInputDeviceHoverSurface, TaskbarVolumeHoverSurface })
                surface.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private LyricsPresentationFrame BuildVerticalMetadataFrame() => new(
        true, _actualTitle + "\n" + _actualArtist, -1,
        VisibleMetadataTitle(PresentationSettings.TaskbarExperience), _isSmallTaskbar || PresentationSettings.TaskbarExperience.ContentLayout == TaskbarContentLayout.CompactInline
            ? string.Empty : _actualArtist,
        string.Empty, string.Empty, false, 0, null, null, _snapshot.IsPlaying);

    private TaskbarRestLayout ResolveVerticalLayout(double primaryLength)
    {
        var experience = PresentationSettings.TaskbarExperience.Normalize();
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

    private double VerticalArtworkSize
    {
        get
        {
            var size = Math.Max(1, MainBorder.Width - 4);
            return PresentationSettings.TaskbarExperience.SmallArtwork ? Math.Max(1, Math.Round(size * 0.8)) : size;
        }
    }

    private double VerticalComponentLength(TaskbarRestComponent component, TaskbarDensityMetrics metrics) => component switch
    {
        TaskbarRestComponent.Artwork => VerticalArtworkSize,
        TaskbarRestComponent.Spectrum => _verticalSpectrumLength,
        TaskbarRestComponent.Performance => _verticalPerformanceLength,
        TaskbarRestComponent.BluetoothBattery => BatteryVerticalLength,
        TaskbarRestComponent.OutputDevice or TaskbarRestComponent.InputDevice or TaskbarRestComponent.Volume =>
            VerticalAudioButtonSize(metrics) + RestWidgetVerticalInset(TaskbarOutputDeviceHoverSurface),
        _ => 0
    };

    private MediaBarSizeRequest BuildVerticalSizeRequest(MediaBarSizeRequest request, bool lyricsVisible, string current, string secondary)
    {
        var experience = PresentationSettings.TaskbarExperience.Normalize();
        double Measure(string text, double fontSize) => VerticalTaskbarPresentationPolicy.MeasureText(
            text, fontSize, lyricsVisible ? PresentationSettings.LyricsCharacterSpacingPercent : 0,
            sideways => MeasureTextWidthExact(sideways, SongTitle) * fontSize / Math.Max(1, SongTitle.FontSize));
        if (!lyricsVisible)
            current = VisibleMetadataTitle(experience);
        var hoverMinimum = experience.HoverLayerEnabled ? VerticalHoverMinimumLength() : 0;
        var textLength = lyricsVisible && PresentationSettings.LyricsFixedWidthEnabled
            ? LyricsFixedWidth.Normalize(PresentationSettings.LyricsFixedWidthDip)
            : Math.Max(Measure(current, VerticalPrimaryFontSize),
                Measure(lyricsVisible ? secondary : _isSmallTaskbar ? string.Empty : _actualArtist, lyricsVisible ? VerticalPrimaryFontSize : VerticalSecondaryFontSize));
        var visibility = ResolveRestVisibility(experience);
        var components = TaskbarRestLayoutPolicy.ResolveOrder(experience.RestComponentOrder)
            .Where(component => TaskbarRestLayoutPolicy.IsVisible(component, visibility)).ToArray();
        var metrics = TaskbarDensityMetrics.From(experience.Density);
        double Content(double text) => TaskbarExperiencePolicy.CalculateRestWidth(
            components, 2, text, 2, VerticalSectionGap,
            component => VerticalComponentLength(component, metrics), double.PositiveInfinity);
        _minimumPrimaryLength = Content(Math.Max(24, hoverMinimum));
        textLength = Math.Max(textLength, hoverMinimum);
        var length = _snapshot.IsConnected
            ? TaskbarExperiencePolicy.ResolvePrimaryLength(Content(textLength), _minimumPrimaryLength,
                double.PositiveInfinity, experience.LengthMode, experience.FixedLengthDip)
            : Content(0);
        return request with { Width = MainBorder.Width, Height = Math.Max(1, length) };
    }

    private void ApplyVerticalSectionGeometry(double primaryLength)
    {
        _verticalSpectrumLength = TaskbarSpectrum.Width + RestWidgetVerticalInset(TaskbarSpectrumHoverSurface);
        var width = Math.Max(1, MainBorder.Width);
        var innerWidth = Math.Max(1, width - 4);
        var experience = PresentationSettings.TaskbarExperience.Normalize();
        var tooltipPlacement = _verticalTaskbarOnLeft ? PlacementMode.Right : PlacementMode.Left;
        _wheelTooltip.Placement = tooltipPlacement;
        _artworkWheelTooltip.Placement = tooltipPlacement;
        _quickLaunchTooltip.Placement = tooltipPlacement;
        SongTitle.FontSize = VerticalPrimaryFontSize;
        SongArtist.FontSize = VerticalSecondaryFontSize;
        SongImageBorder.Width = VerticalArtworkSize;
        SongImageBorder.Height = VerticalArtworkSize;
        SongImagePlaceholder.FontSize = Math.Max(12, VerticalArtworkSize * 0.6);
        var layout = ResolveVerticalLayout(primaryLength);
        _isRestLayerEmpty = layout.IsEmpty;
        ApplyRestComponentVisibility(layout);
        void Place(FrameworkElement element, TaskbarRestComponent component)
        {
            if (layout.Find(component) is not { } placement) return;
            var isArtwork = component == TaskbarRestComponent.Artwork;
            element.Width = isArtwork ? VerticalArtworkSize : innerWidth;
            element.Height = placement.Width;
            var left = isArtwork ? (width - element.Width) / 2 : 2;
            element.HorizontalAlignment = HorizontalAlignment.Left;
            element.VerticalAlignment = VerticalAlignment.Top;
            if (element.Parent is Canvas)
            {
                Canvas.SetLeft(element, left);
                Canvas.SetTop(element, placement.Left);
            }
            else element.Margin = new Thickness(left, placement.Left, 0, 0);
        }
        if (!IsArtworkZoomActive) Place(SongImageBorder, TaskbarRestComponent.Artwork);
        if (_artworkHover._artworkZoomSubstitute is { } substitute) Place(substitute, TaskbarRestComponent.Artwork);
        Place(SongInfoStackPanel, TaskbarRestComponent.MediaText);
        Place(SongInfoHoverOverlay, TaskbarRestComponent.MediaText);
        Place(TaskbarSpectrumHoverSurface, TaskbarRestComponent.Spectrum);
        Place(TaskbarPerformanceHoverSurface, TaskbarRestComponent.Performance);
        Place(TaskbarBatterySurface, TaskbarRestComponent.BluetoothBattery);
        Place(TaskbarOutputDeviceHoverSurface, TaskbarRestComponent.OutputDevice);
        Place(TaskbarInputDeviceHoverSurface, TaskbarRestComponent.InputDevice);
        Place(TaskbarVolumeHoverSurface, TaskbarRestComponent.Volume);
        var textLength = layout.TextWidth;
        SongLyricsPanel.Width = innerWidth;
        SongLyricsPanel.Height = textLength;
        // Web 不可用时保留未旋转的简化元数据。
        SongTitleContainer.Width = innerWidth;
        SongArtistContainer.Width = innerWidth;
        SongTitle.Width = innerWidth;
        SongArtist.Width = innerWidth;
        SongArtistContainer.Visibility = _isSmallTaskbar || string.IsNullOrEmpty(_actualArtist) ? Visibility.Collapsed : Visibility.Visible;
        SongArtistContainer.Margin = new Thickness(0, 2, 0, 0);
        TaskbarPerformanceSurface.Width = Math.Max(1, innerWidth - TaskbarPerformanceHoverSurface.Padding.Left -
            TaskbarPerformanceHoverSurface.Padding.Right - TaskbarPerformanceHoverSurface.BorderThickness.Left - TaskbarPerformanceHoverSurface.BorderThickness.Right);
        TaskbarPerformanceSurface.Padding = new Thickness(1);
        TaskbarPerformanceSurface.Height = Math.Max(1, _verticalPerformanceLength - RestWidgetVerticalInset(TaskbarPerformanceHoverSurface));
        foreach (var cell in _performanceCells.Values) cell.Width = Math.Max(1, TaskbarPerformanceSurface.Width - 2);
        var buttonSize = VerticalAudioButtonSize(TaskbarDensityMetrics.From(experience.Density));
        foreach (var surface in new[] { TaskbarOutputDeviceSurface, TaskbarInputDeviceSurface, TaskbarVolumeSurface })
        {
            surface.Width = buttonSize;
            surface.Height = buttonSize;
        }
        HoverRevealHost.HorizontalAlignment = HorizontalAlignment.Left;
        HoverRevealHost.VerticalAlignment = VerticalAlignment.Top;
        HoverRevealHost.Margin = new Thickness(2, layout.TextLeft, 0, 0);
        var hoverHeightChanged = Math.Abs(HoverRevealHost.Height - textLength) > 0.01;
        HoverRevealHost.Height = textLength;
        ConfigureVerticalHoverGeometry();
        if (HoverRevealHost.Visibility == Visibility.Visible &&
            (hoverHeightChanged || Math.Abs(HoverRevealHost.Width - innerWidth) > 0.01))
            RetargetHoverRevealWidth(innerWidth);
        var progress = _snapshot.Duration > 0 && experience.RestProgressVisible && layout.Find(TaskbarRestComponent.MediaText) is not null;
        TaskbarRestProgress.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
        TaskbarRestProgress.HorizontalAlignment = HorizontalAlignment.Left;
        TaskbarRestProgress.VerticalAlignment = VerticalAlignment.Top;
        TaskbarRestProgress.Width = textLength;
        TaskbarRestProgress.Height = 2;
        TaskbarRestProgress.Margin = new Thickness(_verticalTaskbarOnLeft ? width - 2 : 2, layout.TextLeft, 0, 0);
        if (TaskbarRestProgress.RenderTransform is not TransformGroup)
            TaskbarRestProgress.RenderTransform = new RotateTransform(90);
        _lyrics.ApplyWebLyricsStyle();
    }

    private double VerticalAudioButtonSize(TaskbarDensityMetrics metrics) => Math.Min(Math.Max(1, MainBorder.Width - 4 -
        TaskbarOutputDeviceHoverSurface.Padding.Left - TaskbarOutputDeviceHoverSurface.Padding.Right -
        TaskbarOutputDeviceHoverSurface.BorderThickness.Left - TaskbarOutputDeviceHoverSurface.BorderThickness.Right), Math.Max(24, metrics.ButtonSize));

    private double VerticalHoverMinimumLength()
    {
        var controls = PresentationSettings.TaskbarExperience.Normalize();
        var count = (controls.HoverControls.PreviousNextVisible ? 2 : 0) +
            (controls.HoverControls.PlayPauseVisible ? 1 : 0) +
            (controls.HoverControls.OutputDeviceVisible ? 1 : 0) +
            (controls.HoverControls.InputDeviceVisible ? 1 : 0) +
            (controls.HoverControls.AudioControlVisible ? 1 : 0);
        return count * VerticalHoverButtonSize() + Math.Max(0, count - 1) * controls.HoverButtonSpacingDip + 8;
    }

    private double VerticalHoverButtonSize() => Math.Min(
        TaskbarDensityMetrics.From(PresentationSettings.TaskbarExperience.Density).ButtonSize,
        Math.Max(1, MainBorder.Width - 16));

    private void ConfigureVerticalHoverGeometry()
    {
        var experience = PresentationSettings.TaskbarExperience.Normalize();
        var size = VerticalHoverButtonSize();
        var count = 0;
        foreach (FrameworkElement group in TaskbarHoverActions.Children)
        {
            if (group.Visibility != Visibility.Visible) continue;
            var items = group is StackPanel panel ? panel.Children.Cast<FrameworkElement>() : [group];
            foreach (var item in items)
            {
                item.Width = size;
                item.Height = size;
                item.Margin = new Thickness(0, item.Visibility == Visibility.Visible && count++ > 0 ? experience.HoverButtonSpacingDip : 0, 0, 0);
            }
        }
        TaskbarHoverContent.Margin = _verticalTaskbarOnLeft ? new Thickness(2, 2, 8, 2) : new Thickness(8, 2, 2, 2);
        TaskbarHoverActions.HorizontalAlignment = HorizontalAlignment.Center;
        TaskbarHoverLayer.Width = Math.Max(1, MainBorder.Width - 4);
        TaskbarHoverLayer.Height = Math.Max(1, SongInfoStackPanel.Height);
        TaskbarHoverActionsOffset.X = 0;
        TaskbarHoverActionsOffset.Y = 0;
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
