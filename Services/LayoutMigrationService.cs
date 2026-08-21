using System.IO;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 将旧版扁平设置或四档案文档转换为横竖两套布局，并对布局树做读取后的标准化。
/// Converts legacy flat settings or four-profile documents into two orientation layouts and normalizes the tree after loading.
/// </summary>
internal static class LayoutMigrationService
{
    private const int MinimumScalePercent = 70;
    private const int MaximumScalePercent = 125;
    private const int MaximumAnimationMilliseconds = 2_000;
    private const int MaximumProximityDip = 256;
    private const int MaximumMediaTextLines = 2;

    internal static LayoutDocument CreateFromLegacy(
        WindowSettings window,
        MetricSettings metrics)
    {
        var horizontal = CreateProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            window,
            metrics,
            vertical: false);
        var vertical = CreateProfile(
            LayoutProfileKey.Vertical,
            PlayerLayoutMode.Vertical,
            window,
            metrics,
            vertical: true);

        return new LayoutDocument(
            LayoutDocument.CurrentSchemaVersion,
            horizontal,
            vertical);
    }

    /// <summary>
    /// schema 1/2 曾为任务栏和悬浮各保存一份布局；迁移时优先保留当前宿主模式对应的两份，避免静默拼接产生重复组件。
    /// Schema 1/2 stored separate taskbar and floating layouts; migration keeps the current host pair to avoid silently merging duplicate widgets.
    /// </summary>
    internal static LayoutDocument MigrateLegacyDocument(
        LegacyLayoutDocument legacy,
        WindowHostMode preferredHostMode)
    {
        if (legacy.SchemaVersion > 2)
        {
            throw new InvalidDataException(
                $"Unsupported legacy layout schema version: {legacy.SchemaVersion}.");
        }

        var horizontal = preferredHostMode == WindowHostMode.Floating
            ? legacy.FloatingHorizontal ?? legacy.TaskbarHorizontal
            : legacy.TaskbarHorizontal ?? legacy.FloatingHorizontal;
        var vertical = preferredHostMode == WindowHostMode.Floating
            ? legacy.FloatingVertical ?? legacy.TaskbarVertical
            : legacy.TaskbarVertical ?? legacy.FloatingVertical;
        if (horizontal is null || vertical is null)
        {
            throw new InvalidDataException("Legacy orientation layouts are missing.");
        }

        // 旧版封面和整个媒体区域都可跳转来源；schema 3 将该行为收敛到封面属性，迁移时默认保留原有可达性。
        // Legacy artwork and the whole media area opened the source; schema 3 moves that behavior to artwork and preserves reachability on migration.
        horizontal = MigrateLegacyArtworkInteraction(horizontal) with
        {
            Key = LayoutProfileKey.Horizontal,
            LayoutMode = PlayerLayoutMode.Horizontal
        };
        vertical = MigrateLegacyArtworkInteraction(vertical) with
        {
            Key = LayoutProfileKey.Vertical,
            LayoutMode = PlayerLayoutMode.Vertical
        };
        return Normalize(new LayoutDocument(
            LayoutDocument.CurrentSchemaVersion,
            horizontal,
            vertical));
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
            Horizontal = NormalizeProfile(document.Horizontal with
            {
                Key = LayoutProfileKey.Horizontal,
                LayoutMode = PlayerLayoutMode.Horizontal
            }),
            Vertical = NormalizeProfile(document.Vertical with
            {
                Key = LayoutProfileKey.Vertical,
                LayoutMode = PlayerLayoutMode.Vertical
            })
        };
    }

    private static LayoutProfile CreateProfile(
        LayoutProfileKey key,
        PlayerLayoutMode layoutMode,
        WindowSettings window,
        MetricSettings metrics,
        bool vertical)
    {
        var inlineContainers = new List<LayoutContainerElement>();
        if (window.ShowArtwork)
        {
            inlineContainers.Add(CreateStaticContainer(
                "always-leading",
                LayoutFlowOrientation.Automatic,
                [CreateWidget(
                    "artwork",
                    BuiltInWidgetTypeIds.Artwork,
                    new ArtworkWidgetSettings(
                        Math.Clamp(window.ArtworkCornerRadius, 0, 20),
                        false,
                        true))]));
        }

        inlineContainers.Add(CreateHoverContainer(window, metrics, vertical));

        var trailingChildren = new List<LayoutElement>();

        if (metrics.OutputDeviceSwitcherEnabled)
        {
            trailingChildren.Add(CreateCommand(
                "output-device",
                MediaCommandKind.SelectOutputDevice));
        }

        if (metrics.VolumeControlEnabled)
        {
            trailingChildren.Add(CreateCommand("volume", MediaCommandKind.AdjustVolume));
        }

        var cycleMetrics = GetSelectedMetrics(metrics);
        if (cycleMetrics.Count > 0)
        {
            trailingChildren.Add(CreateWidget(
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
            trailingChildren.Add(CreateWidget(
                "divider",
                BuiltInWidgetTypeIds.Separator,
                new SeparatorWidgetSettings(1, 22)));
        }

        if (trailingChildren.Count > 0)
        {
            inlineContainers.Add(CreateStaticContainer(
                "always-trailing",
                LayoutFlowOrientation.Automatic,
                trailingChildren));
        }

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
            EdgeCollapseEnabled = false
        };

        return new LayoutProfile(
            key,
            layoutMode,
            surface,
            inlineContainers,
            []);
    }

    private static LayoutProfile MigrateLegacyArtworkInteraction(LayoutProfile profile)
    {
        return profile with
        {
            InlineContainers = (profile.InlineContainers ?? [])
                .Select(MigrateLegacyArtworkInteraction)
                .ToArray(),
            EdgeContainers = (profile.EdgeContainers ?? [])
                .Select(edge => edge with
                {
                    ExpandedSlot = MigrateLegacyArtworkInteraction(edge.ExpandedSlot)
                })
                .ToArray(),
            Root = profile.Root is null
                ? null
                : MigrateLegacyArtworkInteraction(profile.Root)
        };
    }

    private static LayoutContainerElement MigrateLegacyArtworkInteraction(
        LayoutContainerElement container)
    {
        return container with
        {
            PrimarySlot = MigrateLegacyArtworkInteraction(container.PrimarySlot),
            SecondarySlot = MigrateLegacyArtworkInteraction(container.SecondarySlot),
            CollapsedSlot = MigrateLegacyArtworkInteraction(container.CollapsedSlot)
        };
    }

    private static LayoutSlot MigrateLegacyArtworkInteraction(LayoutSlot slot)
    {
        if (slot is null)
        {
            return LayoutSlot.Empty("migrated");
        }

        return slot with
        {
            Children = (slot.Children ?? []).Select(element => element switch
            {
                LayoutWidgetElement
                {
                    TypeId: BuiltInWidgetTypeIds.Artwork,
                    Settings: ArtworkWidgetSettings artwork
                } widget => widget with
                {
                    Settings = artwork with { OpenSourceOnClick = true }
                },
                LayoutContainerElement container => MigrateLegacyArtworkInteraction(container),
                _ => element
            }).ToArray()
        };
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
                CreateMediaText("title", MediaTextKind.Title, 14, heightDip: 20),
                CreateMediaText("artist", MediaTextKind.Artist, 11, heightDip: 20)
            };
            if (vertical)
            {
                idleChildren.Add(mediaTextChildren[0]);
            }
            else
            {
                // 离开槽只保留歌曲名；歌手在靠近槽与控制按钮一起显示，避免低密度状态占满长条。
                // The leave slot keeps only the title; artist joins the near slot with controls for a denser state.
                idleChildren.Add(mediaTextChildren[0]);
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
            // 组合信息组件把歌手放在歌曲名下方；固定列宽让长标题截断，不会把右侧控制按钮推出长条。
            // The combined media widget places artist below title; a fixed width keeps long titles from pushing controls out of the strip.
            activeChildren.Add(CreateMediaText(
                "media-active-text",
                MediaTextKind.TitleAndArtist,
                14,
                vertical ? null : 150));
        }

        activeChildren.Add(CreateCommand("previous", MediaCommandKind.Previous));
        activeChildren.Add(CreateCommand("play-pause", MediaCommandKind.PlayPause));
        activeChildren.Add(CreateCommand("next", MediaCommandKind.Next));

        return new LayoutContainerElement(
            "media-interaction",
            true,
            LayoutGeometry.Auto,
            LayoutContainerKind.HoverSwitch,
            vertical ? LayoutFlowOrientation.Vertical : LayoutFlowOrientation.Horizontal,
            LayoutContentAlignment.Center,
            LayoutContentAlignment.Center,
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
        int fontSizeDip,
        int? widthDip = null,
        int? heightDip = null)
    {
        return CreateWidget(
            id,
            BuiltInWidgetTypeIds.MediaText,
            new MediaTextWidgetSettings(kind, true, fontSizeDip, 1)) with
        {
            Geometry = LayoutGeometry.Auto with
            {
                WidthDip = widthDip,
                HeightDip = heightDip
            }
        };
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
            LayoutContentAlignment.Center,
            LayoutContentAlignment.Center,
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

        var inline = (profile.InlineContainers ?? [])
            .Select(container => NormalizeContainer(container, parentAllowsInteractive: true))
            .ToList();
        var edges = (profile.EdgeContainers ?? [])
            .Select(NormalizeEdgeContainer)
            .ToList();

        if (profile.Root is not null)
        {
            MigrateLegacyRoot(profile.Root, inline, edges, profile.LayoutMode);
        }

        // 自动折叠容器只能存在于边缘；读取旧版或手工 JSON 时将其移出长条，而不是静默禁用。
        // Auto-collapse containers are edge-only; move legacy or hand-authored entries out of the strip instead of silently disabling them.
        for (var index = inline.Count - 1; index >= 0; index--)
        {
            if (inline[index].ContainerKind != LayoutContainerKind.AutoCollapse)
            {
                continue;
            }

            edges.Add(ConvertLegacyAutoCollapse(inline[index], profile.LayoutMode, edges.Count));
            inline.RemoveAt(index);
        }

        return profile with
        {
            Surface = surface with { EdgeCollapseEnabled = false },
            InlineContainers = EnsureUniqueTopLevelIds(inline),
            EdgeContainers = EnsureUniqueEdgeIds(edges),
            Root = null
        };
    }

    private static void MigrateLegacyRoot(
        LayoutContainerElement root,
        ICollection<LayoutContainerElement> inline,
        ICollection<LayoutEdgeContainer> edges,
        PlayerLayoutMode layoutMode)
    {
        var normalizedRoot = NormalizeContainer(root, parentAllowsInteractive: true);
        if (normalizedRoot.ContainerKind != LayoutContainerKind.Static)
        {
            if (normalizedRoot.ContainerKind == LayoutContainerKind.AutoCollapse)
            {
                edges.Add(ConvertLegacyAutoCollapse(normalizedRoot, layoutMode, edges.Count));
            }
            else
            {
                inline.Add(normalizedRoot with { Orientation = LayoutFlowOrientation.Automatic });
            }
            return;
        }

        var pendingWidgets = new List<LayoutElement>();
        foreach (var child in normalizedRoot.PrimarySlot.Children)
        {
            if (child is LayoutContainerElement container)
            {
                FlushLegacyWidgets(pendingWidgets, inline);
                if (container.ContainerKind == LayoutContainerKind.AutoCollapse)
                {
                    edges.Add(ConvertLegacyAutoCollapse(container, layoutMode, edges.Count));
                }
                else
                {
                    inline.Add(container with { Orientation = LayoutFlowOrientation.Automatic });
                }
            }
            else
            {
                pendingWidgets.Add(child);
            }
        }

        FlushLegacyWidgets(pendingWidgets, inline);
    }

    private static void FlushLegacyWidgets(
        List<LayoutElement> widgets,
        ICollection<LayoutContainerElement> inline)
    {
        if (widgets.Count == 0)
        {
            return;
        }

        inline.Add(CreateStaticContainer(
            $"migrated-inline-{inline.Count + 1}",
            LayoutFlowOrientation.Automatic,
            widgets.ToArray()));
        widgets.Clear();
    }

    private static LayoutEdgeContainer ConvertLegacyAutoCollapse(
        LayoutContainerElement container,
        PlayerLayoutMode layoutMode,
        int index)
    {
        var expandedChildren = container.PrimarySlot.Children.Count > 0
            ? container.PrimarySlot.Children
            : container.CollapsedSlot.Children;
        return new LayoutEdgeContainer(
            string.IsNullOrWhiteSpace(container.InstanceId)
                ? $"migrated-edge-{index + 1}"
                : container.InstanceId,
            container.Enabled,
            layoutMode == PlayerLayoutMode.Vertical ? LayoutEdge.Right : LayoutEdge.Top,
            0,
            6,
            Math.Clamp(container.ProximityDip, 0, MaximumProximityDip),
            NormalizeAnimation(container.Animation),
            NormalizeSlot(new LayoutSlot("expanded", expandedChildren), allowInteractive: true));
    }

    private static LayoutEdgeContainer NormalizeEdgeContainer(LayoutEdgeContainer container)
    {
        return container with
        {
            InstanceId = string.IsNullOrWhiteSpace(container.InstanceId)
                ? $"edge-{Guid.NewGuid():N}"
                : container.InstanceId,
            Edge = Enum.IsDefined(container.Edge) ? container.Edge : LayoutEdge.Top,
            OffsetDip = Math.Clamp(container.OffsetDip, -2_000, 2_000),
            TriggerThicknessDip = Math.Clamp(container.TriggerThicknessDip, 2, 24),
            ProximityDip = Math.Clamp(container.ProximityDip, 0, MaximumProximityDip),
            Animation = NormalizeAnimation(container.Animation),
            ExpandedSlot = NormalizeSlot(container.ExpandedSlot, allowInteractive: true)
        };
    }

    private static IReadOnlyList<LayoutContainerElement> EnsureUniqueTopLevelIds(
        IReadOnlyList<LayoutContainerElement> containers)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        return containers.Select((container, index) =>
        {
            var id = string.IsNullOrWhiteSpace(container.InstanceId)
                ? $"inline-{index + 1}"
                : container.InstanceId;
            while (!used.Add(id))
            {
                id = $"{id}-{index + 1}";
            }

            return container with
            {
                InstanceId = id,
                ContainerKind = container.ContainerKind == LayoutContainerKind.HoverSwitch
                    ? LayoutContainerKind.HoverSwitch
                    : LayoutContainerKind.Static,
                Orientation = LayoutFlowOrientation.Automatic,
                Trigger = container.ContainerKind == LayoutContainerKind.HoverSwitch
                    ? LayoutTriggerMode.PointerNear
                    : LayoutTriggerMode.Always,
                CollapsedSlot = LayoutSlot.Empty("collapsed")
            };
        }).ToArray();
    }

    private static IReadOnlyList<LayoutEdgeContainer> EnsureUniqueEdgeIds(
        IReadOnlyList<LayoutEdgeContainer> containers)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        return containers.Select((container, index) =>
        {
            var id = container.InstanceId;
            while (!used.Add(id))
            {
                id = $"{id}-{index + 1}";
            }

            return container with { InstanceId = id };
        }).ToArray();
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
            // 新布局的主轴只能由横向/竖向档案决定；旧容器级方向在规范化时清除，避免同一档案出现混合排列。
            // The profile owns the layout axis; clear legacy per-container orientation so one profile cannot mix directions.
            Orientation = LayoutFlowOrientation.Automatic,
            ContentAlignment = Enum.IsDefined(container.ContentAlignment)
                ? container.ContentAlignment
                : LayoutContentAlignment.Center,
            SecondaryContentAlignment = Enum.IsDefined(container.SecondaryContentAlignment)
                ? container.SecondaryContentAlignment
                : LayoutContentAlignment.Center,
            Geometry = NormalizeGeometry(container.Geometry),
            ProximityDip = Math.Clamp(container.ProximityDip, 0, MaximumProximityDip),
            Animation = NormalizeAnimation(container.Animation),
            PrimarySlot = NormalizeSlot(container.PrimarySlot, primaryAllowsInteractive),
            SecondarySlot = NormalizeSlot(container.SecondarySlot, secondaryAllowsInteractive),
            CollapsedSlot = NormalizeSlot(container.CollapsedSlot, allowInteractive: false)
        };
        return normalized.ContainerKind == LayoutContainerKind.HoverSwitch &&
            string.Equals(normalized.InstanceId, "media-interaction", StringComparison.Ordinal)
            ? NormalizeDefaultMediaInteraction(normalized)
            : normalized;
    }

    private static LayoutContainerElement NormalizeDefaultMediaInteraction(
        LayoutContainerElement container)
    {
        var normalized = container;
        // 旧默认档案把标题和歌手作为两个横向控件；合并为稳定宽度的两行信息，避免控制按钮被长标题推出窗口。
        // Older defaults placed title and artist as two horizontal widgets; merge them into a stable two-line block so long titles cannot push controls out.
        var title = normalized.SecondarySlot.Children
            .OfType<LayoutWidgetElement>()
            .FirstOrDefault(widget =>
                widget.Settings is MediaTextWidgetSettings { TextKind: MediaTextKind.Title });
        var artist = normalized.SecondarySlot.Children
            .OfType<LayoutWidgetElement>()
            .FirstOrDefault(widget =>
                widget.Settings is MediaTextWidgetSettings { TextKind: MediaTextKind.Artist });
        if (title is null || artist is null)
        {
            return normalized;
        }

        var combined = title with
        {
            Settings = title.Settings is MediaTextWidgetSettings text
                ? text with { TextKind = MediaTextKind.TitleAndArtist }
                : title.Settings,
            Geometry = (title.Geometry ?? LayoutGeometry.Auto) with
            {
                WidthDip = null,
                HeightDip = 40
            }
        };
        return normalized with
        {
            SecondarySlot = normalized.SecondarySlot with
            {
                Children = normalized.SecondarySlot.Children
                    .Where(child => !string.Equals(child.InstanceId, artist.InstanceId, StringComparison.Ordinal))
                    .Select(child => string.Equals(child.InstanceId, title.InstanceId, StringComparison.Ordinal)
                        ? combined
                        : child)
                    .ToArray()
            }
        };
    }

    private static LayoutSlot NormalizeSlot(LayoutSlot slot, bool allowInteractive)
    {
        if (slot is null)
        {
            return LayoutSlot.Empty("recovered");
        }

        var children = slot.Children ?? [];
        var normalizedChildren = children
            .Select(child => NormalizeElement(child, allowInteractive))
            .Where(child => child is not null)
            .Cast<LayoutElement>()
            .ToArray();
        return slot with
        {
            // 新布局不允许容器拥有独立方向；旧版仅用于分组的静态嵌套容器在迁移时展开。
            // New layouts do not allow per-container orientation; flatten legacy static grouping containers during migration.
            Children = normalizedChildren
                .SelectMany(child => child is LayoutContainerElement
                    {
                        ContainerKind: LayoutContainerKind.Static
                    } nested
                        ? nested.PrimarySlot.Children
                        : [child])
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
        if (enabled && (!ComponentCatalog.TryGet(widget.TypeId, out _) ||
                (!allowInteractive && ComponentCatalog.IsInteractive(widget))))
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
                    MaxLines = Math.Clamp(text.MaxLines, 1, MaximumMediaTextLines)
                },
            BuiltInWidgetTypeIds.MediaSource when settings is MediaTextWidgetSettings source =>
                source with
                {
                    TextKind = MediaTextKind.Source,
                    FontSizeDip = Math.Clamp(source.FontSizeDip, 6, 72),
                    MaxLines = Math.Clamp(source.MaxLines, 1, MaximumMediaTextLines)
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
                    BandCount = Math.Clamp(spectrum.BandCount, 1, AudioMonitorService.BandCount),
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
            return ComponentCatalog.IsInteractive(widget);
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
