using System.IO;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 将旧版扁平设置转换为四套独立布局，并对布局树做读取后的标准化。
/// Converts legacy flat settings into four independent layouts and normalizes the tree after loading.
/// </summary>
internal static class LayoutMigrationService
{
    private const int MinimumScalePercent = 70;
    private const int MaximumScalePercent = 125;
    private const int MaximumAnimationMilliseconds = 2_000;
    private const int MaximumProximityDip = 256;

    internal static LayoutDocument CreateFromLegacy(
        WindowSettings window,
        MetricSettings metrics)
    {
        var horizontalTaskbar = CreateProfile(
            LayoutProfileKey.TaskbarHorizontal,
            WindowHostMode.Taskbar,
            PlayerLayoutMode.Horizontal,
            window,
            metrics,
            vertical: false);
        var verticalTaskbar = CreateProfile(
            LayoutProfileKey.TaskbarVertical,
            WindowHostMode.Taskbar,
            PlayerLayoutMode.Vertical,
            window,
            metrics,
            vertical: true);
        var horizontalFloating = CreateProfile(
            LayoutProfileKey.FloatingHorizontal,
            WindowHostMode.Floating,
            PlayerLayoutMode.Horizontal,
            window,
            metrics,
            vertical: false);
        var verticalFloating = CreateProfile(
            LayoutProfileKey.FloatingVertical,
            WindowHostMode.Floating,
            PlayerLayoutMode.Vertical,
            window,
            metrics,
            vertical: true);

        return new LayoutDocument(
            LayoutDocument.CurrentSchemaVersion,
            horizontalTaskbar,
            verticalTaskbar,
            horizontalFloating,
            verticalFloating);
    }

    internal static LayoutDocument Normalize(LayoutDocument document)
    {
        if (document.SchemaVersion > LayoutDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported layout schema version: {document.SchemaVersion}.");
        }

        return document with
        {
            SchemaVersion = LayoutDocument.CurrentSchemaVersion,
            TaskbarHorizontal = NormalizeProfile(document.TaskbarHorizontal),
            TaskbarVertical = NormalizeProfile(document.TaskbarVertical),
            FloatingHorizontal = NormalizeProfile(document.FloatingHorizontal),
            FloatingVertical = NormalizeProfile(document.FloatingVertical)
        };
    }

    private static LayoutProfile CreateProfile(
        LayoutProfileKey key,
        WindowHostMode hostMode,
        PlayerLayoutMode layoutMode,
        WindowSettings window,
        MetricSettings metrics,
        bool vertical)
    {
        var rootChildren = new List<LayoutElement>();
        if (window.ShowArtwork)
        {
            rootChildren.Add(CreateWidget(
                "artwork",
                BuiltInWidgetTypeIds.Artwork,
                new ArtworkWidgetSettings(
                    Math.Clamp(window.ArtworkCornerRadius, 0, 20),
                    false)));
        }

        rootChildren.Add(CreateHoverContainer(window, metrics, vertical));

        if (metrics.OutputDeviceSwitcherEnabled)
        {
            rootChildren.Add(CreateCommand(
                "output-device",
                MediaCommandKind.SelectOutputDevice));
        }

        if (metrics.VolumeControlEnabled)
        {
            rootChildren.Add(CreateCommand("volume", MediaCommandKind.AdjustVolume));
        }

        var cycleMetrics = GetSelectedMetrics(metrics);
        if (cycleMetrics.Count > 0)
        {
            rootChildren.Add(CreateWidget(
                "metrics",
                BuiltInWidgetTypeIds.Metrics,
                new MetricsWidgetSettings(
                    cycleMetrics[0],
                    metrics.OpenTaskManagerOnMetricsClick,
                    2500,
                    cycleMetrics)));
        }

        if (!vertical)
        {
            rootChildren.Add(CreateWidget(
                "divider",
                BuiltInWidgetTypeIds.Separator,
                new SeparatorWidgetSettings(1, 22)));
        }

        var root = new LayoutContainerElement(
            "root",
            true,
            LayoutGeometry.Auto,
            LayoutContainerKind.Static,
            vertical ? LayoutFlowOrientation.Vertical : LayoutFlowOrientation.Horizontal,
            LayoutTriggerMode.Always,
            0,
            new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
            new LayoutSlot("content", rootChildren),
            LayoutSlot.Empty("secondary"),
            LayoutSlot.Empty("collapsed"));

        var surface = LayoutSurfaceSettings.Default with
        {
            LengthScalePercent = Math.Clamp(
                window.LengthScalePercent,
                MinimumScalePercent,
                MaximumScalePercent),
            ThicknessScalePercent = Math.Clamp(
                window.ThicknessScalePercent,
                MinimumScalePercent,
                MaximumScalePercent),
            EdgeCollapseEnabled = hostMode == WindowHostMode.Floating &&
                window.EdgeAutoCollapse
        };

        return new LayoutProfile(key, hostMode, layoutMode, surface, root);
    }

    private static LayoutContainerElement CreateHoverContainer(
        WindowSettings window,
        MetricSettings metrics,
        bool vertical)
    {
        var idleChildren = new List<LayoutElement>();
        var activeChildren = new List<LayoutElement>();

        if (window.ShowMediaInfo && !(metrics.AudioMonitorEnabled && !vertical))
        {
            var mediaTextChildren = new LayoutElement[]
            {
                CreateMediaText("title", MediaTextKind.Title, 14),
                CreateMediaText("artist", MediaTextKind.Artist, 11)
            };
            if (vertical)
            {
                idleChildren.AddRange(mediaTextChildren);
            }
            else
            {
                idleChildren.Add(CreateStaticContainer(
                    "media-text-stack",
                    LayoutFlowOrientation.Vertical,
                    mediaTextChildren));
            }
        }
        else if (metrics.AudioMonitorEnabled && !vertical)
        {
            idleChildren.Add(CreateWidget(
                "spectrum",
                BuiltInWidgetTypeIds.Spectrum,
                new SpectrumWidgetSettings(9, 20, 100)));
        }

        if (window.ShowMediaInfo)
        {
            activeChildren.Add(CreateMediaText("title-active", MediaTextKind.Title, 14));
            activeChildren.Add(CreateMediaText("artist-active", MediaTextKind.Artist, 11));
        }

        activeChildren.Add(CreateCommand("previous", MediaCommandKind.Previous));
        activeChildren.Add(CreateCommand("play-pause", MediaCommandKind.PlayPause));
        activeChildren.Add(CreateCommand("next", MediaCommandKind.Next));

        return new LayoutContainerElement(
            "media-interaction",
            true,
            new LayoutGeometry(210, null, 0, 420, null, null, LayoutThickness.Zero),
            LayoutContainerKind.HoverSwitch,
            vertical ? LayoutFlowOrientation.Vertical : LayoutFlowOrientation.Horizontal,
            window.AutoCollapse ? LayoutTriggerMode.PointerNear : LayoutTriggerMode.Always,
            0,
            LayoutAnimationSettings.Default,
            new LayoutSlot("idle", idleChildren),
            new LayoutSlot("active", activeChildren),
            LayoutSlot.Empty("collapsed"));
    }

    private static LayoutWidgetElement CreateMediaText(
        string id,
        MediaTextKind kind,
        int fontSizeDip)
    {
        return CreateWidget(
            id,
            BuiltInWidgetTypeIds.MediaText,
            new MediaTextWidgetSettings(kind, true, fontSizeDip, 1));
    }

    private static LayoutWidgetElement CreateCommand(
        string id,
        MediaCommandKind command)
    {
        return CreateWidget(
            id,
            BuiltInWidgetTypeIds.Command,
            new CommandWidgetSettings(command, 36));
    }

    private static LayoutWidgetElement CreateWidget(
        string id,
        string typeId,
        WidgetSettings settings)
    {
        return new LayoutWidgetElement(
            id,
            true,
            LayoutGeometry.Auto,
            typeId,
            settings);
    }

    private static LayoutContainerElement CreateStaticContainer(
        string id,
        LayoutFlowOrientation orientation,
        IReadOnlyList<LayoutElement> children)
    {
        return new LayoutContainerElement(
            id,
            true,
            LayoutGeometry.Auto,
            LayoutContainerKind.Static,
            orientation,
            LayoutTriggerMode.Always,
            0,
            new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
            new LayoutSlot("content", children),
            LayoutSlot.Empty("secondary"),
            LayoutSlot.Empty("collapsed"));
    }

    private static IReadOnlyList<MetricKind> GetSelectedMetrics(MetricSettings settings)
    {
        if (!settings.Enabled)
        {
            return [];
        }

        var result = new List<MetricKind>(4);
        if (settings.ShowSystemMemory)
        {
            result.Add(MetricKind.SystemMemory);
        }
        if (settings.ShowSystemCpu)
        {
            result.Add(MetricKind.SystemCpu);
        }
        if (settings.ShowSystemGpu)
        {
            result.Add(MetricKind.SystemGpu);
        }
        if (settings.ShowProcessMemory)
        {
            result.Add(MetricKind.ProcessMemory);
        }

        return result;
    }

    private static LayoutProfile NormalizeProfile(LayoutProfile profile)
    {
        var surface = profile.Surface ?? LayoutSurfaceSettings.Default;
        surface = surface with
        {
            LengthScalePercent = Math.Clamp(
                surface.LengthScalePercent,
                MinimumScalePercent,
                MaximumScalePercent),
            ThicknessScalePercent = Math.Clamp(
                surface.ThicknessScalePercent,
                MinimumScalePercent,
                MaximumScalePercent),
            GapDip = Math.Clamp(surface.GapDip, 0, 32),
            CornerRadiusDip = Math.Clamp(surface.CornerRadiusDip, 0, 32),
            WidthDip = ClampNullable(surface.WidthDip, 32, 2_000),
            HeightDip = ClampNullable(surface.HeightDip, 24, 2_000)
        };

        var root = NormalizeContainer(profile.Root, parentAllowsInteractive: true);
        return profile with { Surface = surface, Root = root };
    }

    private static LayoutContainerElement NormalizeContainer(
        LayoutContainerElement container,
        bool parentAllowsInteractive)
    {
        var kind = Enum.IsDefined(container.ContainerKind)
            ? container.ContainerKind
            : LayoutContainerKind.Static;
        var primaryAllowsInteractive = parentAllowsInteractive &&
            kind != LayoutContainerKind.HoverSwitch;
        var secondaryAllowsInteractive = parentAllowsInteractive;
        var normalized = container with
        {
            ContainerKind = kind,
            Geometry = NormalizeGeometry(container.Geometry),
            ProximityDip = Math.Clamp(container.ProximityDip, 0, MaximumProximityDip),
            Animation = NormalizeAnimation(container.Animation),
            PrimarySlot = NormalizeSlot(container.PrimarySlot, primaryAllowsInteractive),
            SecondarySlot = NormalizeSlot(container.SecondarySlot, secondaryAllowsInteractive),
            CollapsedSlot = NormalizeSlot(container.CollapsedSlot, allowInteractive: false)
        };
        return normalized;
    }

    private static LayoutSlot NormalizeSlot(LayoutSlot slot, bool allowInteractive)
    {
        if (slot is null)
        {
            return LayoutSlot.Empty("recovered");
        }

        var children = slot.Children ?? [];
        return slot with
        {
            Children = children
                .Select(child => NormalizeElement(child, allowInteractive))
                .Where(child => child is not null)
                .Cast<LayoutElement>()
                .ToArray()
        };
    }

    private static LayoutElement? NormalizeElement(
        LayoutElement element,
        bool allowInteractive)
    {
        if (element is null)
        {
            return null;
        }

        if (element is LayoutContainerElement container)
        {
            var normalizedContainer = NormalizeContainer(container, allowInteractive);
            return !allowInteractive && ContainsInteractiveElement(normalizedContainer)
                ? normalizedContainer with { Enabled = false }
                : normalizedContainer;
        }

        if (element is not LayoutWidgetElement widget)
        {
            return null;
        }

        var enabled = widget.Enabled;
        if (enabled && (!ComponentCatalog.TryGet(widget.TypeId, out var definition) ||
                (!allowInteractive && definition.Capabilities.HasFlag(WidgetCapabilities.Interactive))))
        {
            enabled = false;
        }

        return widget with
        {
            Enabled = enabled,
            Geometry = NormalizeGeometry(widget.Geometry),
            Settings = NormalizeWidgetSettings(widget.TypeId, widget.Settings)
        };
    }

    private static WidgetSettings NormalizeWidgetSettings(
        string typeId,
        WidgetSettings settings)
    {
        return typeId switch
        {
            BuiltInWidgetTypeIds.Artwork when settings is ArtworkWidgetSettings artwork =>
                artwork with
                {
                    CornerRadiusDip = Math.Clamp(artwork.CornerRadiusDip, 0, 32)
                },
            BuiltInWidgetTypeIds.MediaText when settings is MediaTextWidgetSettings text =>
                text with
                {
                    TextKind = Enum.IsDefined(text.TextKind)
                        ? text.TextKind
                        : MediaTextKind.Title,
                    FontSizeDip = Math.Clamp(text.FontSizeDip, 6, 72),
                    MaxLines = Math.Clamp(text.MaxLines, 1, 8)
                },
            BuiltInWidgetTypeIds.Command when settings is CommandWidgetSettings command =>
                command with
                {
                    Command = Enum.IsDefined(command.Command)
                        ? command.Command
                        : MediaCommandKind.PlayPause,
                    ButtonSizeDip = Math.Clamp(command.ButtonSizeDip, 20, 96)
                },
            BuiltInWidgetTypeIds.Metrics when settings is MetricsWidgetSettings metrics =>
                metrics with
                {
                    Metric = Enum.IsDefined(metrics.Metric)
                        ? metrics.Metric
                        : MetricKind.SystemMemory,
                    RefreshIntervalMilliseconds = Math.Clamp(
                        metrics.RefreshIntervalMilliseconds,
                        250,
                        30_000),
                    CycleMetrics = metrics.CycleMetrics is { Count: > 0 }
                        ? metrics.CycleMetrics
                            .Where(Enum.IsDefined)
                            .Distinct()
                            .Take(4)
                            .ToArray()
                        : [Enum.IsDefined(metrics.Metric)
                            ? metrics.Metric
                            : MetricKind.SystemMemory]
                },
            BuiltInWidgetTypeIds.Spectrum when settings is SpectrumWidgetSettings spectrum =>
                spectrum with
                {
                    BandCount = Math.Clamp(spectrum.BandCount, 1, 32),
                    RefreshRateHz = Math.Clamp(spectrum.RefreshRateHz, 5, 30),
                    SensitivityPercent = Math.Clamp(spectrum.SensitivityPercent, 1, 400)
                },
            BuiltInWidgetTypeIds.Separator when settings is SeparatorWidgetSettings separator =>
                separator with
                {
                    ThicknessDip = Math.Clamp(separator.ThicknessDip, 1, 8),
                    LengthDip = Math.Clamp(separator.LengthDip, 4, 256)
                },
            _ => ComponentCatalog.CreateDefaultSettings(typeId)
        };
    }

    private static bool ContainsInteractiveElement(LayoutElement element)
    {
        if (element is LayoutWidgetElement widget)
        {
            return widget.Enabled &&
                ComponentCatalog.TryGet(widget.TypeId, out var definition) &&
                definition.Capabilities.HasFlag(WidgetCapabilities.Interactive);
        }

        return element is LayoutContainerElement { Enabled: true } container &&
            container.PrimarySlot.Children.Concat(container.SecondarySlot.Children)
                .Concat(container.CollapsedSlot.Children)
                .Any(ContainsInteractiveElement);
    }

    private static LayoutGeometry NormalizeGeometry(LayoutGeometry geometry)
    {
        geometry ??= LayoutGeometry.Auto;
        var normalized = geometry with
        {
            WidthDip = ClampNullable(geometry.WidthDip, 1, 2_000),
            HeightDip = ClampNullable(geometry.HeightDip, 1, 2_000),
            MinWidthDip = ClampNullable(geometry.MinWidthDip, 0, 2_000),
            MaxWidthDip = ClampNullable(geometry.MaxWidthDip, 1, 2_000),
            MinHeightDip = ClampNullable(geometry.MinHeightDip, 0, 2_000),
            MaxHeightDip = ClampNullable(geometry.MaxHeightDip, 1, 2_000),
            Margin = NormalizeThickness(geometry.Margin)
        };

        return normalized with
        {
            MaxWidthDip = normalized.MaxWidthDip.HasValue &&
                normalized.MinWidthDip.HasValue
                ? Math.Max(normalized.MaxWidthDip.Value, normalized.MinWidthDip.Value)
                : normalized.MaxWidthDip,
            MaxHeightDip = normalized.MaxHeightDip.HasValue &&
                normalized.MinHeightDip.HasValue
                ? Math.Max(normalized.MaxHeightDip.Value, normalized.MinHeightDip.Value)
                : normalized.MaxHeightDip
        };
    }

    private static LayoutThickness NormalizeThickness(LayoutThickness thickness)
    {
        thickness ??= LayoutThickness.Zero;
        return thickness with
        {
            Left = Math.Clamp(thickness.Left, -256, 256),
            Top = Math.Clamp(thickness.Top, -256, 256),
            Right = Math.Clamp(thickness.Right, -256, 256),
            Bottom = Math.Clamp(thickness.Bottom, -256, 256)
        };
    }

    private static LayoutAnimationSettings NormalizeAnimation(
        LayoutAnimationSettings animation)
    {
        animation ??= LayoutAnimationSettings.Default;
        return animation with
        {
            DurationMilliseconds = Math.Clamp(
                animation.DurationMilliseconds,
                0,
                MaximumAnimationMilliseconds),
            DelayMilliseconds = Math.Clamp(animation.DelayMilliseconds, 0, 2_000),
            Easing = Enum.IsDefined(animation.Easing)
                ? animation.Easing
                : LayoutEasingKind.EaseOut
        };
    }

    private static int? ClampNullable(int? value, int minimum, int maximum)
    {
        return value.HasValue
            ? Math.Clamp(value.Value, minimum, maximum)
            : null;
    }
}
