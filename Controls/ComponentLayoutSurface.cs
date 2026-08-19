using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Controls;

internal sealed class LayoutCommandEventArgs(
    MediaCommandKind command,
    FrameworkElement? placementTarget) : EventArgs
{
    internal MediaCommandKind Command { get; } = command;
    internal FrameworkElement? PlacementTarget { get; } = placementTarget;
}

internal sealed class LayoutMetricsEventArgs(bool openTaskManager) : EventArgs
{
    internal bool OpenTaskManager { get; } = openTaskManager;
}

/// <summary>
/// 根据不可变布局档案生成轻量 WPF 组件树；不读取注册表、不创建系统会话，业务动作通过事件交给窗口协调器。
/// Builds a lightweight WPF tree from an immutable layout profile without registry or system-session access; actions return to the window coordinator through events.
/// </summary>
internal sealed class ComponentLayoutSurface : Grid, IDisposable
{
    internal static readonly DependencyProperty IsInteractiveElementProperty =
        DependencyProperty.RegisterAttached(
            "IsInteractiveElement",
            typeof(bool),
            typeof(ComponentLayoutSurface),
            new FrameworkPropertyMetadata(false));

    private readonly Dictionary<string, FrameworkElement> _widgetViews =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ContainerVisual> _containerViews =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MediaTextKind> _mediaTextKinds =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MarqueeState> _marqueeStates =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetricViewState> _metricStates =
        new(StringComparer.Ordinal);
    private readonly float[] _spectrum = new float[AudioMonitorService.BandCount];
    private readonly DispatcherTimer _marqueeTimer;
    private LayoutProfile? _profile;
    private MediaSnapshot _mediaSnapshot = MediaSnapshot.Disconnected;
    private string _metricsText = string.Empty;
    private bool _pointerNear;
    private int _gapDip;
    private bool _disposed;

    internal ComponentLayoutSurface()
    {
        _marqueeTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(260),
            DispatcherPriority.Render,
            OnMarqueeTimerTick,
            Dispatcher);
        _marqueeTimer.Stop();
    }

    internal event EventHandler<LayoutCommandEventArgs>? CommandRequested;
    internal event EventHandler<LayoutMetricsEventArgs>? MetricsRequested;
    internal event EventHandler? SourceRequested;

    internal static bool GetIsInteractiveElement(DependencyObject element) =>
        (bool)element.GetValue(IsInteractiveElementProperty);

    private static void SetIsInteractiveElement(DependencyObject element, bool value) =>
        element.SetValue(IsInteractiveElementProperty, value);

    internal void Apply(LayoutProfile profile, bool pointerNear)
    {
        _profile = profile;
        _pointerNear = pointerNear;
        _gapDip = Math.Clamp(profile.Surface.GapDip, 0, 32);
        _widgetViews.Clear();
        _containerViews.Clear();
        _mediaTextKinds.Clear();
        _marqueeStates.Clear();
        _metricStates.Clear();
        _marqueeTimer.Stop();
        Children.Clear();

        var root = BuildInlineContainers(profile);
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        var vertical = profile.LayoutMode == PlayerLayoutMode.Vertical;
        var lengthScale = Math.Clamp(profile.Surface.LengthScalePercent, 70, 125) / 100d;
        var thicknessScale = Math.Clamp(profile.Surface.ThicknessScalePercent, 70, 125) / 100d;
        root.LayoutTransform = new ScaleTransform(
            vertical ? thicknessScale : lengthScale,
            vertical ? lengthScale : thicknessScale);
        Width = profile.Surface.WidthDip ?? double.NaN;
        Height = profile.Surface.HeightDip ?? double.NaN;
        ClipToBounds = profile.Surface.WidthDip.HasValue || profile.Surface.HeightDip.HasValue;
        Children.Add(root);
        RefreshAllData();
        if (_marqueeStates.Count > 0)
        {
            _marqueeTimer.Start();
        }
    }

    internal void ApplyEdge(LayoutProfile profile, LayoutEdgeContainer edgeContainer)
    {
        _profile = profile;
        _pointerNear = true;
        _gapDip = Math.Clamp(profile.Surface.GapDip, 0, 32);
        _widgetViews.Clear();
        _containerViews.Clear();
        _mediaTextKinds.Clear();
        _marqueeStates.Clear();
        _metricStates.Clear();
        _marqueeTimer.Stop();
        Children.Clear();

        var root = BuildSlot(edgeContainer.ExpandedSlot, LayoutFlowOrientation.Automatic);
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(root);
        RefreshAllData();
        if (_marqueeStates.Count > 0)
        {
            _marqueeTimer.Start();
        }
    }

    private FrameworkElement BuildInlineContainers(LayoutProfile profile)
    {
        var panel = new StackPanel
        {
            Orientation = profile.LayoutMode == PlayerLayoutMode.Vertical
                ? Orientation.Vertical
                : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var visibleIndex = 0;
        foreach (var container in profile.InlineContainers.Where(container => container.Enabled))
        {
            var view = BuildContainer(container);
            if (visibleIndex > 0 && _gapDip > 0)
            {
                view.Margin = panel.Orientation == Orientation.Horizontal
                    ? new Thickness(_gapDip, 0, 0, 0)
                    : new Thickness(0, _gapDip, 0, 0);
            }

            panel.Children.Add(view);
            visibleIndex++;
        }

        return panel;
    }

    internal void SetPointerNear(bool pointerNear)
    {
        _pointerNear = pointerNear;
        foreach (var visual in _containerViews.Values)
        {
            ApplyContainerState(visual, animate: true);
        }
    }

    internal void SetMediaSnapshot(MediaSnapshot snapshot)
    {
        _mediaSnapshot = snapshot;
        RefreshMediaViews();
    }

    internal void SetMetricsText(string text)
    {
        _metricsText = text;
        foreach (var view in _widgetViews.Values)
        {
            if (view is Border { Child: TextBlock textBlock } &&
                textBlock.Tag is string tag &&
                tag == BuiltInWidgetTypeIds.Metrics)
            {
                textBlock.Text = text;
            }
        }
    }

    internal void SetMetricsSnapshot(SystemMetricsSnapshot snapshot)
    {
        var now = Environment.TickCount64;
        foreach (var state in _metricStates.Values)
        {
            var interval = Math.Clamp(
                state.Settings.RefreshIntervalMilliseconds,
                250,
                30_000);
            if (state.LastUpdateTick != 0 && now - state.LastUpdateTick < interval)
            {
                continue;
            }

            state.LastUpdateTick = now;
            var cycle = state.Settings.CycleMetrics is { Count: > 0 }
                ? state.Settings.CycleMetrics
                : [state.Settings.Metric];
            state.CycleIndex = Math.Clamp(state.CycleIndex, 0, cycle.Count - 1);
            state.Text.Text = MetricTextFormatter.Format(snapshot, cycle[state.CycleIndex]);
            state.CycleIndex = (state.CycleIndex + 1) % cycle.Count;
        }
    }

    internal void SetSpectrum(IReadOnlyList<float> values)
    {
        var count = Math.Min(values.Count, _spectrum.Length);
        for (var index = 0; index < _spectrum.Length; index++)
        {
            _spectrum[index] = index < count
                ? Math.Clamp(values[index], 0, 1)
                : 0;
        }

        foreach (var view in _widgetViews.Values)
        {
            if (view is SpectrumView spectrum)
            {
                spectrum.SetValues(_spectrum);
            }
        }
    }

    private FrameworkElement BuildContainer(LayoutContainerElement container)
    {
        if (container.ContainerKind == LayoutContainerKind.Static)
        {
            var staticSlot = BuildSlot(container.PrimarySlot, container.Orientation);
            ApplyGeometry(staticSlot, container.Geometry);
            return staticSlot;
        }

        var visual = new ContainerVisual(container);
        _containerViews[container.InstanceId] = visual;
        visual.Slots[0].Children.Add(BuildSlot(container.PrimarySlot, container.Orientation));
        visual.Slots[1].Children.Add(BuildSlot(container.SecondarySlot, container.Orientation));
        visual.Slots[2].Children.Add(BuildSlot(container.CollapsedSlot, container.Orientation));
        ApplyContainerState(visual, animate: false);
        ApplyGeometry(visual.Host, container.Geometry);
        return visual.Host;
    }

    private FrameworkElement BuildSlot(LayoutSlot slot, LayoutFlowOrientation orientation)
    {
        var panel = new StackPanel
        {
            Orientation = ResolveOrientation(orientation),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var visibleIndex = 0;
        foreach (var child in slot.Children)
        {
            if (!child.Enabled)
            {
                continue;
            }

            var view = child switch
            {
                LayoutWidgetElement widget => BuildWidget(widget),
                LayoutContainerElement nested => BuildContainer(nested),
                _ => new FrameworkElement()
            };
            if (visibleIndex > 0 && _gapDip > 0)
            {
                var margin = view.Margin;
                view.Margin = panel.Orientation == Orientation.Horizontal
                    ? new Thickness(margin.Left + _gapDip, margin.Top, margin.Right, margin.Bottom)
                    : new Thickness(margin.Left, margin.Top + _gapDip, margin.Right, margin.Bottom);
            }

            panel.Children.Add(view);
            visibleIndex++;
        }

        return panel;
    }

    private FrameworkElement BuildWidget(LayoutWidgetElement widget)
    {
        FrameworkElement view = widget.TypeId switch
        {
            BuiltInWidgetTypeIds.Artwork => BuildArtwork(widget),
            BuiltInWidgetTypeIds.MediaText => BuildMediaText(widget),
            BuiltInWidgetTypeIds.MediaSource => BuildMediaSource(widget),
            BuiltInWidgetTypeIds.Command => BuildCommand(widget),
            BuiltInWidgetTypeIds.Metrics => BuildMetrics(widget),
            BuiltInWidgetTypeIds.Spectrum => BuildSpectrum(widget),
            BuiltInWidgetTypeIds.Separator => BuildSeparator(widget),
            _ => BuildUnknown(widget)
        };

        ApplyGeometry(view, widget.Geometry);
        _widgetViews[widget.InstanceId] = view;
        return view;
    }

    private FrameworkElement BuildArtwork(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as ArtworkWidgetSettings ??
            new ArtworkWidgetSettings(6, false, true);
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Source = _mediaSnapshot.Artwork,
            IsHitTestVisible = false
        };
        var placeholder = new TextBlock
        {
            Text = "\uE8D6",
            FontFamily = GetResource<FontFamily>("AppIconFontFamily") ?? new FontFamily("Segoe MDL2 Assets"),
            FontSize = 18,
            Foreground = GetBrush("TaskbarSecondaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var grid = new Grid();
        grid.Children.Add(placeholder);
        grid.Children.Add(image);
        var border = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(Math.Clamp(settings.CornerRadiusDip, 0, 32)),
            Background = ResolveArtworkBackground(settings),
            Child = grid,
            Cursor = settings.OpenSourceOnClick ? Cursors.Hand : Cursors.Arrow,
            ToolTip = settings.OpenSourceOnClick ? Loc.Get("Main.Menu.ShowSource") : null
        };
        if (settings.OpenSourceOnClick)
        {
            SetIsInteractiveElement(border, true);
            border.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                SourceRequested?.Invoke(this, EventArgs.Empty);
            };
        }
        border.Tag = (image, placeholder, settings);
        return border;
    }

    private FrameworkElement BuildMediaText(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as MediaTextWidgetSettings ??
            new MediaTextWidgetSettings(MediaTextKind.Title, true, 14, 1);
        var text = new TextBlock
        {
            FontFamily = GetResource<FontFamily>("AppDisplayFontFamily") ?? new FontFamily("Segoe UI"),
            FontSize = Math.Clamp(settings.FontSizeDip, 6, 72),
            FontWeight = GetFontWeight("PlayerTitleFontWeight"),
            Foreground = GetBrush("TaskbarPrimaryTextBrush"),
            TextWrapping = settings.MaxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 210,
            VerticalAlignment = VerticalAlignment.Center
        };
        _mediaTextKinds[widget.InstanceId] = settings.TextKind;
        if (settings.EnableMarquee)
        {
            _marqueeStates[widget.InstanceId] = new(text, string.Empty, 0);
        }
        return text;
    }

    private FrameworkElement BuildMediaSource(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as MediaTextWidgetSettings ??
            new MediaTextWidgetSettings(MediaTextKind.Source, false, 11, 1);
        var text = BuildMediaText(widget with
        {
            Settings = settings with { TextKind = MediaTextKind.Source }
        });
        if (text is TextBlock textBlock)
        {
            textBlock.Foreground = GetBrush("TaskbarSecondaryTextBrush");
            textBlock.Cursor = Cursors.Hand;
            textBlock.ToolTip = Loc.Get("Main.Menu.ShowSource");
            SetIsInteractiveElement(textBlock, true);
            textBlock.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                SourceRequested?.Invoke(this, EventArgs.Empty);
            };
        }

        return text;
    }

    private FrameworkElement BuildCommand(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as CommandWidgetSettings ??
            new CommandWidgetSettings(MediaCommandKind.PlayPause, 36);
        var button = new Button
        {
            Width = Math.Clamp(settings.ButtonSizeDip, 20, 96),
            Height = Math.Clamp(settings.ButtonSizeDip, 20, 96),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = GetBrush("TaskbarPrimaryTextBrush"),
            Cursor = Cursors.Hand,
            Tag = settings.Command,
            ToolTip = GetCommandTooltip(settings.Command),
            Content = new TextBlock
            {
                Text = GetCommandGlyph(settings.Command),
                FontFamily = GetResource<FontFamily>("AppIconFontFamily") ?? new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (_, _) => CommandRequested?.Invoke(
            this,
            new LayoutCommandEventArgs(settings.Command, button));
        SetIsInteractiveElement(button, true);
        return button;
    }

    private FrameworkElement BuildMetrics(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as MetricsWidgetSettings ??
            new MetricsWidgetSettings(MetricKind.SystemMemory, false, 2500, [MetricKind.SystemMemory]);
        var text = new TextBlock
        {
            Tag = BuiltInWidgetTypeIds.Metrics,
            Text = _metricsText,
            FontFamily = GetResource<FontFamily>("AppTextFontFamily") ?? new FontFamily("Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetBrush("TaskbarSecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var border = new Border
        {
            Width = 74,
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(12),
            Background = GetBrush("TaskbarHoverBrush"),
            Cursor = settings.OpenTaskManagerOnClick ? Cursors.Hand : Cursors.Arrow,
            Child = text
        };
        SetIsInteractiveElement(border, settings.OpenTaskManagerOnClick);
        border.MouseLeftButtonUp += (_, args) =>
        {
            if (!settings.OpenTaskManagerOnClick)
            {
                return;
            }

            args.Handled = true;
            MetricsRequested?.Invoke(
                this,
                new LayoutMetricsEventArgs(settings.OpenTaskManagerOnClick));
        };
        _metricStates[widget.InstanceId] = new(text, settings);
        return border;
    }

    private FrameworkElement BuildSpectrum(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as SpectrumWidgetSettings ??
            new SpectrumWidgetSettings(9, 20, 100);
        return new SpectrumView(
            Math.Clamp(settings.BandCount, 1, AudioMonitorService.BandCount),
            Math.Clamp(settings.RefreshRateHz, 5, 30),
            Math.Clamp(settings.SensitivityPercent, 1, 400),
            GetBrush("TaskbarSecondaryTextBrush"));
    }

    private static FrameworkElement BuildSeparator(LayoutWidgetElement widget)
    {
        var settings = widget.Settings as SeparatorWidgetSettings ??
            new SeparatorWidgetSettings(1, 22);
        return new Border
        {
            Width = Math.Clamp(settings.ThicknessDip, 1, 8),
            Height = Math.Clamp(settings.LengthDip, 4, 256),
            Background = GetBrush("TaskbarDividerBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
    }

    private static FrameworkElement BuildUnknown(LayoutWidgetElement widget)
    {
        return new Border
        {
            Width = 24,
            Height = 24,
            Opacity = 0.4,
            Child = new TextBlock
            {
                Text = "?",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            ToolTip = Loc.Get("Settings.Layout.UnknownWidget", widget.TypeId)
        };
    }

    private void RefreshAllData()
    {
        RefreshMediaViews();
        SetMetricsText(_metricsText);
        SetSpectrum(_spectrum);
    }

    private void RefreshMediaViews()
    {
        foreach (var pair in _mediaTextKinds)
        {
            if (_widgetViews[pair.Key] is not TextBlock text)
            {
                continue;
            }

            var value = pair.Value switch
            {
                MediaTextKind.Title => GetDisplayText(_mediaSnapshot.Title, "Main.Placeholder.Title"),
                MediaTextKind.Artist => GetDisplayText(_mediaSnapshot.Artist, "Main.Placeholder.Subtitle"),
                MediaTextKind.Source => GetDisplayText(_mediaSnapshot.SourceName, "Main.TitleIdle"),
                _ => string.Empty
            };
            text.Text = IsVertical
                ? FormatVerticalText(value)
                : value;
            text.ToolTip = value;
            if (_marqueeStates.TryGetValue(pair.Key, out var marquee))
            {
                marquee.Content = value;
                marquee.Offset = 0;
            }
        }

        foreach (var view in _widgetViews.Values)
        {
            if (view is not Border
                {
                    Child: Grid grid,
                    Tag: ValueTuple<Image, TextBlock, ArtworkWidgetSettings> artwork
                } border)
            {
                continue;
            }

            artwork.Item1.Source = _mediaSnapshot.Artwork;
            artwork.Item2.Visibility = _mediaSnapshot.Artwork is null
                ? Visibility.Visible
                : Visibility.Collapsed;
            border.Background = ResolveArtworkBackground(artwork.Item3);
        }
    }

    private void ApplyContainerState(ContainerVisual visual, bool animate)
    {
        var container = visual.Model;
        var activeSlot = container.ContainerKind == LayoutContainerKind.AutoCollapse
            ? (_pointerNear ? 0 : 2)
            : container.Trigger == LayoutTriggerMode.Always || _pointerNear
                ? 1
                : 0;
        for (var index = 0; index < visual.Slots.Count; index++)
        {
            var slot = visual.Slots[index];
            var visible = index == activeSlot;
            slot.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            slot.BeginAnimation(UIElement.OpacityProperty, null);
            slot.Opacity = visible ? 1 : 0;
        }

        if (!animate || !container.Animation.Enabled || container.Animation.DurationMilliseconds <= 0)
        {
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(
            Math.Clamp(container.Animation.DurationMilliseconds, 0, 2_000)));
        var easing = container.Animation.Easing switch
        {
            LayoutEasingKind.Linear => null,
            LayoutEasingKind.EaseInOut => new CubicEase { EasingMode = EasingMode.EaseInOut },
            _ => new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        visual.Slots[activeSlot].BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = duration,
                BeginTime = TimeSpan.FromMilliseconds(
                    Math.Clamp(container.Animation.DelayMilliseconds, 0, 2_000)),
                EasingFunction = easing
            });
    }

    private bool IsVertical => _profile?.LayoutMode == PlayerLayoutMode.Vertical;

    private Orientation ResolveOrientation(LayoutFlowOrientation orientation)
    {
        return orientation switch
        {
            LayoutFlowOrientation.Vertical => Orientation.Vertical,
            LayoutFlowOrientation.Horizontal => Orientation.Horizontal,
            _ => IsVertical ? Orientation.Vertical : Orientation.Horizontal
        };
    }

    private static void ApplyGeometry(FrameworkElement view, LayoutGeometry geometry)
    {
        if (geometry.WidthDip.HasValue)
        {
            view.Width = geometry.WidthDip.Value;
        }
        if (geometry.HeightDip.HasValue)
        {
            view.Height = geometry.HeightDip.Value;
        }
        if (geometry.MinWidthDip.HasValue)
        {
            view.MinWidth = geometry.MinWidthDip.Value;
        }
        if (geometry.MaxWidthDip.HasValue)
        {
            view.MaxWidth = geometry.MaxWidthDip.Value;
        }
        if (geometry.MinHeightDip.HasValue)
        {
            view.MinHeight = geometry.MinHeightDip.Value;
        }
        if (geometry.MaxHeightDip.HasValue)
        {
            view.MaxHeight = geometry.MaxHeightDip.Value;
        }
        var margin = geometry.Margin ?? LayoutThickness.Zero;
        var existingMargin = view.Margin;
        view.Margin = new Thickness(
            existingMargin.Left + margin.Left,
            existingMargin.Top + margin.Top,
            existingMargin.Right + margin.Right,
            existingMargin.Bottom + margin.Bottom);
    }

    private static string GetDisplayText(string value, string fallbackKey)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Loc.Get(fallbackKey)
            : value;
    }

    private Brush ResolveArtworkBackground(ArtworkWidgetSettings settings)
    {
        if (!settings.UseMediaPrimaryColor || _mediaSnapshot.Artwork is null)
        {
            return GetBrush("TaskbarSurfaceBrush");
        }

        try
        {
            var source = _mediaSnapshot.Artwork;
            var width = Math.Max(1, source.PixelWidth);
            var height = Math.Max(1, source.PixelHeight);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);
            long red = 0;
            long green = 0;
            long blue = 0;
            var count = 0;
            var sampleStep = Math.Max(1, Math.Max(width, height) / 32);
            for (var y = 0; y < height; y += sampleStep)
            {
                for (var x = 0; x < width; x += sampleStep)
                {
                    var index = y * stride + x * 4;
                    blue += pixels[index];
                    green += pixels[index + 1];
                    red += pixels[index + 2];
                    count++;
                }
            }

            if (count > 0)
            {
                return new SolidColorBrush(Color.FromRgb(
                    (byte)(red / count),
                    (byte)(green / count),
                    (byte)(blue / count)));
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("layout-artwork-accent", exception);
        }

        return GetBrush("TaskbarSurfaceBrush");
    }

    private void OnMarqueeTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || IsVertical)
        {
            return;
        }

        foreach (var state in _marqueeStates.Values)
        {
            var content = state.Content;
            if (content.Length <= 18)
            {
                state.Text.Text = content;
                continue;
            }

            var text = content + "   ";
            var offset = state.Offset % text.Length;
            state.Text.Text = text[offset..] + text[..offset];
            state.Offset++;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _marqueeTimer.Stop();
        _marqueeTimer.Tick -= OnMarqueeTimerTick;
        CommandRequested = null;
        MetricsRequested = null;
        SourceRequested = null;
        _widgetViews.Clear();
        _containerViews.Clear();
        _mediaTextKinds.Clear();
        _marqueeStates.Clear();
        _metricStates.Clear();
    }

    private static string FormatVerticalText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var starts = StringInfo.ParseCombiningCharacters(value);
        return string.Join(
            Environment.NewLine,
            starts.Select((start, index) =>
            {
                var end = index + 1 < starts.Length
                    ? starts[index + 1]
                    : value.Length;
                return value[start..end];
            }));
    }

    private static string GetCommandGlyph(MediaCommandKind command) => command switch
    {
        MediaCommandKind.Previous => "\uE892",
        MediaCommandKind.PlayPause => "\uE768",
        MediaCommandKind.Next => "\uE893",
        MediaCommandKind.SelectSource => "\uE8D6",
        MediaCommandKind.AdjustVolume => "\uE767",
        MediaCommandKind.SelectOutputDevice => "\uE7F5",
        _ => "\uE710"
    };

    private static string GetCommandTooltip(MediaCommandKind command) => command switch
    {
        MediaCommandKind.Previous => Loc.Get("Main.Control.Previous"),
        MediaCommandKind.PlayPause => Loc.Get("Main.Control.Play"),
        MediaCommandKind.Next => Loc.Get("Main.Control.Next"),
        MediaCommandKind.SelectSource => Loc.Get("Main.Menu.ShowSource"),
        MediaCommandKind.AdjustVolume => Loc.Get("Main.Volume.Current"),
        MediaCommandKind.SelectOutputDevice => Loc.Get("Main.Device.Output"),
        _ => string.Empty
    };

    private static T? GetResource<T>(string key)
        where T : class
    {
        return Application.Current?.TryFindResource(key) as T;
    }

    private static Brush GetBrush(string key)
    {
        return GetResource<Brush>(key) ?? Brushes.Transparent;
    }

    private static FontWeight GetFontWeight(string key)
    {
        return Application.Current?.TryFindResource(key) is FontWeight weight
            ? weight
            : FontWeights.Normal;
    }

    private sealed class ContainerVisual
    {
        internal ContainerVisual(LayoutContainerElement model)
        {
            Model = model;
            for (var index = 0; index < 3; index++)
            {
                var slot = new Grid();
                Slots.Add(slot);
                Host.Children.Add(slot);
            }
        }

        internal LayoutContainerElement Model { get; }
        internal Grid Host { get; } = new();
        internal List<Grid> Slots { get; } = [];
    }

    private sealed class MarqueeState(TextBlock text, string content, int offset)
    {
        internal TextBlock Text { get; } = text;
        internal string Content { get; set; } = content;
        internal int Offset { get; set; } = offset;
    }

    private sealed class MetricViewState(TextBlock text, MetricsWidgetSettings settings)
    {
        internal TextBlock Text { get; } = text;
        internal MetricsWidgetSettings Settings { get; } = settings;
        internal long LastUpdateTick { get; set; }
        internal int CycleIndex { get; set; }
    }

    private sealed class SpectrumView(
        int bandCount,
        int refreshRateHz,
        int sensitivityPercent,
        Brush brush) : FrameworkElement
    {
        private readonly float[] _values = new float[AudioMonitorService.BandCount];
        private long _lastRenderTick;

        internal void SetValues(IReadOnlyList<float> values)
        {
            var now = Environment.TickCount64;
            if (now - _lastRenderTick < 1_000 / refreshRateHz)
            {
                return;
            }

            _lastRenderTick = now;
            var count = Math.Min(values.Count, _values.Length);
            for (var index = 0; index < count; index++)
            {
                _values[index] = Math.Clamp(
                    values[index] * sensitivityPercent / 100f,
                    0,
                    1);
            }
            for (var index = count; index < _values.Length; index++)
            {
                _values[index] = 0;
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var width = ActualWidth > 0 ? ActualWidth : 68;
            var height = ActualHeight > 0 ? ActualHeight : 24;
            var gap = 3d;
            var barWidth = Math.Max(2, (width - gap * (bandCount - 1)) / bandCount);
            for (var index = 0; index < bandCount; index++)
            {
                var barHeight = Math.Clamp(3 + Math.Sqrt(_values[index]) * (height - 3), 3, height);
                var x = index * (barWidth + gap);
                drawingContext.DrawRoundedRectangle(
                    brush,
                    null,
                    new Rect(x, (height - barHeight) / 2, barWidth, barHeight),
                    2,
                    2);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(Math.Min(88, availableSize.Width), 24);
        }
    }
}
