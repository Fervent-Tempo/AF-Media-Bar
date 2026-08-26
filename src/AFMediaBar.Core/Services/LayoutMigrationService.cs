using System.IO;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 将旧版扁平设置或 schema 1/2/3 文档确定性迁移到 schema 4 网格模型，并做读取后的标准化。
/// Deterministically migrates legacy flat settings or schema 1/2/3 documents to the schema-4 grid model and normalizes trees after loading.
/// </summary>
public static class LayoutMigrationService
{
    private const int MinimumScalePercent = 70;
    private const int MaximumScalePercent = 125;
    private const int MaximumAnimationMilliseconds = 2_000;
    private const int MaximumProximityDip = 256;
    private const int MaximumMediaTextLines = 2;
    private const int DefaultCellSizeDip = 8;
    private const int DefaultHorizontalTitleWidthDip = 248;
    private const int MinimumTriggerThicknessDip = 2;
    private const int MaximumTriggerThicknessDip = 24;

    /// <summary>
    /// 从旧版扁平设置生成当前版本的默认布局；结果经过完整的确定性迁移与约束验证。
    /// Builds the current default layout from legacy flat settings through the deterministic migration and constraint validation.
    /// </summary>
    public static LayoutDocument CreateFromLegacy(
        WindowSettings window,
        MetricSettings metrics)
    {
        var schema3 = BuildSchema3Document(window, metrics);
        return ApplyHorizontalDefaultTemplate(MigrateSchema3To4(schema3));
    }

    /// <summary>
    /// 应用当前用户确认的横向默认档案微调；只用于新建/重置默认档案，不参与旧文件迁移。
    /// Applies the accepted horizontal default-profile fine tuning for new/reset defaults only, never legacy-file migration.
    /// </summary>
    private static LayoutDocument ApplyHorizontalDefaultTemplate(LayoutDocument document)
    {
        var horizontal = document.Horizontal;
        var trailing = horizontal.Containers.FirstOrDefault(container =>
            string.Equals(container.InstanceId, "always-trailing", StringComparison.Ordinal));
        if (trailing is null)
        {
            return document;
        }

        var children = trailing.PrimarySlot.Children
            .Select(child => child switch
            {
                LayoutWidgetElement { InstanceId: "output-device", GridBounds: { } bounds } widget =>
                    widget with { GridBounds = bounds with { X = 1 } },
                LayoutWidgetElement { InstanceId: "volume", GridBounds: { } bounds } widget =>
                    widget with { GridBounds = bounds with { X = 5 } },
                _ => child
            })
            .ToArray();
        var updatedTrailing = trailing with
        {
            PrimarySlot = trailing.PrimarySlot with { Children = children }
        };
        return document with
        {
            Horizontal = horizontal with
            {
                Containers = horizontal.Containers
                    .Select(container => string.Equals(container.InstanceId, trailing.InstanceId, StringComparison.Ordinal)
                        ? updatedTrailing
                        : container)
                    .ToArray()
            }
        };
    }

    /// <summary>
    /// schema 1/2 的四档案文档先归并为横竖两套 schema 3 档案，再继续走 3 → 4 迁移。
    /// Schema-1/2 four-profile documents are merged into the horizontal/vertical schema-3 pair before the 3 → 4 migration.
    /// </summary>
    public static Schema3LayoutDocument MigrateLegacyDocument(
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
        // Legacy artwork and the whole media area opened the source; schema 3 keeps that behavior on artwork and preserves reachability on migration.
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
        return new Schema3LayoutDocument(3, horizontal, vertical);
    }

    /// <summary>
    /// schema 3 文档到 schema 4 的确定性迁移：测量旧容器/组件 DIP 尺寸、除以单格并向上取整，
    /// 横向沿 X、纵向沿 Y 依次放置非折叠容器，旧边缘容器依附首/尾容器生成唯一公共边。
    /// Deterministic schema-3 → schema-4 migration: measures legacy DIP sizes, converts to cells,
    /// lays non-collapse containers along the profile axis, and attaches edge containers to the first/last anchor.
    /// </summary>
    public static LayoutDocument MigrateSchema3To4(Schema3LayoutDocument document)
    {
        if (document.SchemaVersion != 3)
        {
            throw new InvalidDataException(
                $"Unsupported schema-3 source version: {document.SchemaVersion}.");
        }

        var horizontal = MigrateProfileTo4(
            document.Horizontal,
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal);
        var vertical = MigrateProfileTo4(
            document.Vertical,
            LayoutProfileKey.Vertical,
            PlayerLayoutMode.Vertical);
        var migrated = new LayoutDocument(
            LayoutDocument.CurrentSchemaVersion,
            horizontal,
            vertical);
        var normalized = Normalize(migrated);
        ValidateOrThrow(normalized);
        return normalized;
    }

    public static LayoutDocument Normalize(LayoutDocument document)
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

    /// <summary>
    /// 迁移或读取 schema 4 文件后执行完整约束验证；失败时抛出以触发保留原文件并回退默认布局。
    /// Runs the full constraint validation after migration or schema-4 reads; failures throw so the original file is preserved and the default layout is used.
    /// </summary>
    public static void ValidateOrThrow(LayoutDocument document)
    {
        var failures = new List<string>();
        CollectProfileFailures(document.Horizontal, "horizontal", failures);
        CollectProfileFailures(document.Vertical, "vertical", failures);
        if (failures.Count == 0)
        {
            return;
        }

        throw new InvalidDataException(
            $"Layout validation failed after migration: {string.Join("; ", failures)}.");
    }

    private static void CollectProfileFailures(
        LayoutProfile profile,
        string label,
        ICollection<string> failures)
    {
        foreach (var error in LayoutGridConstraintService.ValidateProfile(profile))
        {
            failures.Add($"{label}:{error.InstanceId}:{error.Failure}");
        }
    }

    // ---------- schema 3 默认布局构建 ----------

    private static Schema3LayoutDocument BuildSchema3Document(
        WindowSettings window,
        MetricSettings metrics)
    {
        var horizontal = BuildSchema3Profile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            window,
            metrics,
            vertical: false);
        var vertical = BuildSchema3Profile(
            LayoutProfileKey.Vertical,
            PlayerLayoutMode.Vertical,
            window,
            metrics,
            vertical: true);
        return new Schema3LayoutDocument(3, horizontal, vertical);
    }

    private static Schema3Profile BuildSchema3Profile(
        LayoutProfileKey key,
        PlayerLayoutMode layoutMode,
        WindowSettings window,
        MetricSettings metrics,
        bool vertical)
    {
        var inlineContainers = new List<Schema3ContainerElement>();
        if (window.ShowArtwork)
        {
            inlineContainers.Add(CreateSchema3StaticContainer(
                "always-leading",
                [CreateSchema3Widget(
                    "artwork",
                    BuiltInWidgetTypeIds.Artwork,
                    new ArtworkWidgetSettings(
                        Math.Clamp(window.ArtworkCornerRadius, 0, 20),
                        false,
                        true))]));
        }

        inlineContainers.Add(CreateSchema3HoverContainer(window, metrics, vertical));

        var trailingChildren = new List<Schema3Element>();
        if (metrics.OutputDeviceSwitcherEnabled)
        {
            trailingChildren.Add(CreateSchema3Command(
                "output-device",
                MediaCommandKind.SelectOutputDevice));
        }

        // 横向默认档案沿用当前用户确认的布局模板，默认保留音量按钮；布局编辑器仍可删除它。
        // The canonical horizontal default keeps the volume button from the user's accepted template; the editor can still remove it.
        if (metrics.VolumeControlEnabled || !vertical)
        {
            trailingChildren.Add(CreateSchema3Command("volume", MediaCommandKind.AdjustVolume));
        }

        var cycleMetrics = GetSelectedMetrics(metrics);
        if (cycleMetrics.Count > 0)
        {
            trailingChildren.Add(CreateSchema3Widget(
                "metrics",
                BuiltInWidgetTypeIds.Metrics,
                new MetricsWidgetSettings(
                    cycleMetrics[0],
                    metrics.OpenTaskManagerOnMetricsClick,
                    2500,
                    cycleMetrics)));
        }

        if (trailingChildren.Count > 0)
        {
            inlineContainers.Add(CreateSchema3StaticContainer(
                "always-trailing",
                trailingChildren,
                heightDip: vertical ? null : 40));
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

        return new Schema3Profile(
            key,
            layoutMode,
            surface,
            inlineContainers,
            []);
    }

    private static Schema3ContainerElement CreateSchema3StaticContainer(
        string id,
        IReadOnlyList<Schema3Element> children,
        int? widthDip = null,
        int? heightDip = null)
    {
        return new Schema3ContainerElement(
            id,
            true,
            LayoutGeometry.Auto with
            {
                WidthDip = widthDip,
                HeightDip = heightDip
            },
            LayoutContainerKind.Static,
            LayoutFlowOrientation.Automatic,
            LayoutContentAlignment.Center,
            LayoutContentAlignment.Center,
            LayoutTriggerMode.Always,
            0,
            new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
            new Schema3Slot("content", children),
            Schema3Slot.Empty("unused"),
            Schema3Slot.Empty("legacy-collapsed"));
    }

    private static Schema3ContainerElement CreateSchema3HoverContainer(
        WindowSettings window,
        MetricSettings metrics,
        bool vertical)
    {
        var idleChildren = new List<Schema3Element>();
        var activeChildren = new List<Schema3Element>();

        if (window.ShowMediaInfo && !(metrics.AudioMonitorEnabled && !vertical))
        {
            idleChildren.Add(CreateSchema3MediaText(
                "title",
                MediaTextKind.Title,
                14,
                widthDip: vertical ? null : DefaultHorizontalTitleWidthDip,
                heightDip: 40,
                enableMarquee: false,
                maxLines: 2));
        }
        else if (metrics.AudioMonitorEnabled && !vertical)
        {
            idleChildren.Add(CreateSchema3Widget(
                "spectrum",
                BuiltInWidgetTypeIds.Spectrum,
                new SpectrumWidgetSettings(9, 20, 100)));
        }

        if (window.ShowMediaInfo)
        {
            // 组合信息组件把歌手放在歌曲名下方；固定列宽让长标题截断，不会把右侧控制按钮推出长条。
            // The combined media widget places artist below title; a fixed width keeps long titles from pushing controls out of the strip.
            activeChildren.Add(CreateSchema3MediaText(
                "media-active-text",
                MediaTextKind.TitleAndArtist,
                14,
                vertical ? null : 150,
                enableMarquee: true,
                maxLines: 1));
        }

        activeChildren.Add(CreateSchema3Command("previous", MediaCommandKind.Previous));
        activeChildren.Add(CreateSchema3Command("play-pause", MediaCommandKind.PlayPause));
        activeChildren.Add(CreateSchema3Command("next", MediaCommandKind.Next));

        return new Schema3ContainerElement(
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
            new Schema3Slot("idle", idleChildren),
            new Schema3Slot("active", activeChildren),
            Schema3Slot.Empty("collapsed"));
    }

    private static Schema3WidgetElement CreateSchema3MediaText(
        string id,
        MediaTextKind kind,
        int fontSizeDip,
        int? widthDip = null,
        int? heightDip = null,
        bool enableMarquee = true,
        int maxLines = 1)
    {
        return CreateSchema3Widget(
            id,
            BuiltInWidgetTypeIds.MediaText,
            new MediaTextWidgetSettings(kind, enableMarquee, fontSizeDip, maxLines)) with
        {
            Geometry = LayoutGeometry.Auto with
            {
                WidthDip = widthDip,
                HeightDip = heightDip
            }
        };
    }

    private static Schema3WidgetElement CreateSchema3Command(
        string id,
        MediaCommandKind command)
    {
        return CreateSchema3Widget(
            id,
            BuiltInWidgetTypeIds.Command,
            new CommandWidgetSettings(command, CommandWidgetSettings.DefaultButtonSizeDip));
    }

    private static Schema3WidgetElement CreateSchema3Widget(
        string id,
        string typeId,
        WidgetSettings settings)
    {
        return new Schema3WidgetElement(
            id,
            true,
            LayoutGeometry.Auto,
            typeId,
            settings);
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

    private static Schema3Profile MigrateLegacyArtworkInteraction(Schema3Profile profile)
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

    private static Schema3ContainerElement MigrateLegacyArtworkInteraction(
        Schema3ContainerElement container)
    {
        return container with
        {
            PrimarySlot = MigrateLegacyArtworkInteraction(container.PrimarySlot),
            SecondarySlot = MigrateLegacyArtworkInteraction(container.SecondarySlot),
            CollapsedSlot = MigrateLegacyArtworkInteraction(container.CollapsedSlot)
        };
    }

    private static Schema3Slot MigrateLegacyArtworkInteraction(Schema3Slot slot)
    {
        if (slot is null)
        {
            return Schema3Slot.Empty("migrated");
        }

        return slot with
        {
            Children = (slot.Children ?? []).Select(element => element switch
            {
                Schema3WidgetElement
                {
                    TypeId: BuiltInWidgetTypeIds.Artwork,
                    Settings: ArtworkWidgetSettings artwork
                } widget => widget with
                {
                    Settings = artwork with { OpenSourceOnClick = true }
                },
                Schema3ContainerElement container => MigrateLegacyArtworkInteraction(container),
                _ => element
            }).ToArray()
        };
    }

    // ---------- schema 3 → schema 4 确定性迁移 ----------

    private static LayoutProfile MigrateProfileTo4(
        Schema3Profile schema3,
        LayoutProfileKey key,
        PlayerLayoutMode layoutMode)
    {
        var surface = NormalizeSurface(schema3.Surface);
        var grid = LayoutGridSettings.Default;
        var vertical = layoutMode == PlayerLayoutMode.Vertical;
        var containers = new List<Schema3ContainerElement>(schema3.InlineContainers ?? []);
        var edges = new List<Schema3EdgeContainer>(schema3.EdgeContainers ?? []);
        if (schema3.Root is not null)
        {
            FlattenLegacyRoot(schema3.Root, containers, edges, layoutMode);
        }

        // 槽位中遗留的嵌套容器提升为档案顶层容器。
        // Hoist any nested containers left in slots to profile top level.
        var hoisted = new List<Schema3ContainerElement>();
        for (var index = 0; index < containers.Count; index++)
        {
            containers[index] = HoistNestedContainers(containers[index], hoisted);
        }
        containers.AddRange(hoisted);

        // 行内 AutoCollapse 容器转换为边缘折叠容器。
        // Inline AutoCollapse containers become edge collapse containers.
        for (var index = containers.Count - 1; index >= 0; index--)
        {
            if (containers[index].ContainerKind != LayoutContainerKind.AutoCollapse)
            {
                continue;
            }

            edges.Add(ConvertInlineAutoCollapse(containers[index], layoutMode, edges.Count));
            containers.RemoveAt(index);
        }

        // 启用的非折叠容器必须连续排列（共享接边）；禁用的排在末尾，避免打断连通图。
        // Enabled non-collapse containers stay contiguous to share edges; disabled ones trail so they cannot break connectivity.
        var ordered = containers
            .OrderByDescending(container => container.Enabled)
            .ThenBy(container => Array.IndexOf(containers.ToArray(), container))
            .ToArray();

        var body = ordered
            .Select(container => new PlacedContainer(
                container,
                MeasureSchema3ContainerCells(container, vertical, surface.GapDip)))
            .ToArray();
        var bodyWidth = 0;
        var bodyHeight = 0;
        if (vertical)
        {
            bodyWidth = body.Length == 0 ? 1 : body.Max(item => item.W);
            bodyHeight = body.Sum(item => item.H);
        }
        else
        {
            bodyWidth = body.Sum(item => item.W);
            bodyHeight = body.Length == 0 ? 1 : body.Max(item => item.H);
        }

        var collapseItems = edges
            .Select(edge => (Edge: edge, Size: MeasureSchema3CollapseCells(edge, vertical, surface.GapDip)))
            .ToArray();

        // 为折叠容器预留身体外的空间；行（横向）在 Y、列（纵向）在 X 方向前导，保证所有坐标非负。
        // Reserve space outside the body so every grid coordinate stays non-negative.
        var leadingX = vertical
            ? collapseItems.Where(item => item.Edge.Edge == LayoutEdge.Left)
                .Select(item => item.Size.W)
                .DefaultIfEmpty(0)
                .Max()
            : 0;
        var leadingY = !vertical
            ? collapseItems.Where(item => item.Edge.Edge == LayoutEdge.Top)
                .Select(item => item.Size.H)
                .DefaultIfEmpty(0)
                .Max()
            : 0;

        LayoutPosition(body, vertical, leadingX, leadingY);

        // 第一阶段：按需求增长锚点容器尺寸，确保折叠容器只与锚点共享一个公共边。
        // Pass one: grow anchor sizes so every collapse touches exactly one anchor edge.
        foreach (var item in collapseItems)
        {
            GrowAnchorForCollapse(body, item.Edge, item.Size, vertical);
        }

        var converted = body
            .Select(item => BuildSchema4Container(item, vertical, surface.GapDip))
            .ToArray();

        var collapseContainers = new List<LayoutCollapseContainer>();
        foreach (var item in collapseItems)
        {
            var attachment = ResolveCollapsePlacement(
                item.Edge,
                item.Size,
                body,
                vertical,
                bodyWidth,
                bodyHeight);
            collapseContainers.Add(new LayoutCollapseContainer(
                ResolveCollapseInstanceId(item.Edge.InstanceId, collapseContainers.Count),
                item.Edge.Enabled,
                attachment.Rect,
                new LayoutAttachment(attachment.AnchorId, attachment.Side),
                Math.Clamp(item.Edge.TriggerThicknessDip, MinimumTriggerThicknessDip, MaximumTriggerThicknessDip),
                Math.Clamp(item.Edge.ProximityDip, 0, MaximumProximityDip),
                NormalizeAnimation(item.Edge.Animation),
                BuildSchema4Slot(item.Edge.ExpandedSlot, vertical, surface.GapDip, allowInteractive: true, attachment.Rect.Width, attachment.Rect.Height)));
        }

        // schema 3 旧布局测量结果可能超过默认 48 x 24 网格；按最终占用动态扩展网格，
        // 保证迁移后的联合边界和折叠矩形都在网格内，避免 OutOfGrid / Overlap 校验失败。
        // Legacy measurements can exceed the default 48 x 24 grid; grow the grid to fit the
        // final occupancy so the union bounds and collapse rects always stay in range.
        grid = ExpandGridToFit(
            grid,
            converted.Select(container => container.GridBounds)
                .Concat(collapseContainers.Select(item => item.GridBounds)));

        return new LayoutProfile(
            key,
            layoutMode,
            surface with { EdgeCollapseEnabled = false },
            grid,
            converted,
            collapseContainers);
    }

    private static LayoutGridSettings ExpandGridToFit(
        LayoutGridSettings grid,
        IEnumerable<LayoutGridRect?> rects)
    {
        var maxRight = LayoutGridSettings.Default.Columns;
        var maxBottom = LayoutGridSettings.Default.Rows;
        foreach (var rect in rects)
        {
            if (rect is null)
            {
                continue;
            }

            maxRight = Math.Max(maxRight, rect.Right);
            maxBottom = Math.Max(maxBottom, rect.Bottom);
        }

        var normalized = LayoutGridSettings.Normalize(grid);
        return new LayoutGridSettings(
            Math.Max(maxRight, normalized.Columns),
            Math.Max(maxBottom, normalized.Rows),
            normalized.CellSizeDip);
    }

    private static void LayoutPosition(
        PlacedContainer[] body,
        bool vertical,
        int leadingX,
        int leadingY)
    {
        var cursorX = leadingX;
        var cursorY = leadingY;
        foreach (var item in body)
        {
            if (vertical)
            {
                item.X = cursorX;
                item.Y = cursorY;
                cursorY += item.H;
            }
            else
            {
                item.X = cursorX;
                item.Y = cursorY;
                cursorX += item.W;
            }
        }
    }

    private static void GrowAnchorForCollapse(
        PlacedContainer[] body,
        Schema3EdgeContainer edge,
        (int W, int H) size,
        bool vertical)
    {
        if (body.Length == 0)
        {
            return;
        }

        var anchorIndex = edge.Edge is LayoutEdge.Bottom or LayoutEdge.Right
            ? body.Length - 1
            : 0;
        var anchor = body[anchorIndex];
        if (vertical)
        {
            if (edge.Edge is LayoutEdge.Top or LayoutEdge.Bottom && size.W > anchor.W)
            {
                anchor.W = size.W;
            }

            if (edge.Edge is LayoutEdge.Left or LayoutEdge.Right && size.H > anchor.H)
            {
                GrowContainerHeight(body, anchorIndex, size.H);
            }
        }
        else
        {
            if (edge.Edge is LayoutEdge.Top or LayoutEdge.Bottom && size.W > anchor.W)
            {
                GrowContainerWidth(body, anchorIndex, size.W);
            }

            if (edge.Edge is LayoutEdge.Left or LayoutEdge.Right && size.H > anchor.H)
            {
                GrowContainerHeight(body, anchorIndex, size.H);
            }
        }
    }

    private static void GrowContainerWidth(
        PlacedContainer[] body,
        int index,
        int width)
    {
        var delta = width - body[index].W;
        if (delta <= 0)
        {
            return;
        }

        body[index].W = width;
        for (var other = index + 1; other < body.Length; other++)
        {
            body[other].X += delta;
        }
    }

    private static void GrowContainerHeight(
        PlacedContainer[] body,
        int index,
        int height)
    {
        var delta = height - body[index].H;
        if (delta <= 0)
        {
            return;
        }

        body[index].H = height;
        for (var other = index + 1; other < body.Length; other++)
        {
            body[other].Y += delta;
        }
    }

    private static LayoutCollapsePlacement ResolveCollapsePlacement(
        Schema3EdgeContainer edge,
        (int W, int H) size,
        PlacedContainer[] body,
        bool vertical,
        int bodyWidth,
        int bodyHeight)
    {
        if (body.Length == 0)
        {
            // 没有锚点时给一个 1x1 占位；约束验证会以 MissingAnchor 拒绝并回退默认布局。
            // Without an anchor, keep a 1x1 placeholder; validation rejects it with MissingAnchor and falls back to defaults.
            return new LayoutCollapsePlacement("", edge.Edge, new LayoutGridRect(0, 0, size.W, size.H));
        }

        var anchorIndex = edge.Edge is LayoutEdge.Bottom or LayoutEdge.Right
            ? body.Length - 1
            : 0;
        var anchor = body[anchorIndex];
        var rect = vertical
            ? edge.Edge switch
            {
                LayoutEdge.Top => new LayoutGridRect(anchor.X, anchor.Y - size.H, size.W, size.H),
                LayoutEdge.Bottom => new LayoutGridRect(anchor.X, anchor.Y + anchor.H, size.W, size.H),
                LayoutEdge.Left => new LayoutGridRect(anchor.X - size.W, anchor.Y, size.W, size.H),
                _ => new LayoutGridRect(anchor.X + anchor.W, anchor.Y, size.W, size.H)
            }
            : edge.Edge switch
            {
                LayoutEdge.Top => new LayoutGridRect(anchor.X, anchor.Y - size.H, size.W, size.H),
                LayoutEdge.Bottom => new LayoutGridRect(anchor.X, anchor.Y + anchor.H, size.W, size.H),
                LayoutEdge.Left => new LayoutGridRect(anchor.X - size.W, anchor.Y, size.W, size.H),
                _ => new LayoutGridRect(anchor.X + anchor.W, anchor.Y, size.W, size.H)
            };
        return new LayoutCollapsePlacement(anchor.InstanceId, edge.Edge, rect);
    }

    private static string ResolveCollapseInstanceId(string instanceId, int index)
    {
        return string.IsNullOrWhiteSpace(instanceId)
            ? $"migrated-collapse-{index + 1}"
            : instanceId;
    }

    private static LayoutContainerElement BuildSchema4Container(
        PlacedContainer placement,
        bool vertical,
        int gapDip)
    {
        var container = placement.Model;
        var primary = BuildSchema4Slot(
            container.PrimarySlot,
            vertical,
            gapDip,
            allowInteractive: container.ContainerKind != LayoutContainerKind.HoverSwitch,
            placement.W,
            placement.H);
        var secondary = BuildSchema4Slot(
            container.SecondarySlot,
            vertical,
            gapDip,
            allowInteractive: true,
            placement.W,
            placement.H);
        return new LayoutContainerElement(
            container.InstanceId,
            container.Enabled,
            LayoutGeometry.Auto,
            container.ContainerKind == LayoutContainerKind.HoverSwitch
                ? LayoutContainerKind.HoverSwitch
                : LayoutContainerKind.Static,
            LayoutFlowOrientation.Automatic,
            LayoutContentAlignment.Center,
            LayoutContentAlignment.Center,
            container.ContainerKind == LayoutContainerKind.HoverSwitch
                ? LayoutTriggerMode.PointerNear
                : LayoutTriggerMode.Always,
            Math.Clamp(container.ProximityDip, 0, MaximumProximityDip),
            NormalizeAnimation(container.Animation),
            primary,
            secondary,
            new LayoutGridRect(placement.X, placement.Y, placement.W, placement.H));
    }

    private static LayoutSlot BuildSchema4Slot(
        Schema3Slot slot,
        bool vertical,
        int gapDip,
        bool allowInteractive,
        int containerWidthCells,
        int containerHeightCells)
    {
        if (slot is null)
        {
            return LayoutSlot.Empty("recovered");
        }

        var children = new List<LayoutElement>();
        var gapCells = gapDip > 0
            ? Math.Max(1, (int)Math.Ceiling(gapDip / (double)DefaultCellSizeDip))
            : 0;
        var cursor = 0;
        foreach (var child in slot.Children)
        {
            if (child is not Schema3WidgetElement widget)
            {
                continue;
            }

            var cells = MeasureSchema3WidgetCells(widget, vertical);
            var localRect = vertical
                ? new LayoutGridRect(
                    Math.Max(0, (containerWidthCells - cells.W) / 2),
                    cursor,
                    cells.W,
                    cells.H)
                : new LayoutGridRect(
                    cursor,
                    Math.Max(0, (containerHeightCells - cells.H) / 2),
                    cells.W,
                    cells.H);
            var skin = ComponentSkinCatalog.Normalize(
                widget.TypeId,
                widget.SkinId,
                widget.SkinVersion,
                widget.SkinSettings);
            children.Add(new LayoutWidgetElement(
                widget.InstanceId,
                widget.Enabled,
                widget.Geometry,
                widget.TypeId,
                widget.Settings,
                skin?.SkinId,
                skin?.Version,
                skin?.Settings,
                localRect));
            cursor += vertical
                ? cells.H + gapCells
                : cells.W + gapCells;
        }

        return new LayoutSlot(slot.SlotId ?? "content", children);
    }

    private static Schema3ContainerElement HoistNestedContainers(
        Schema3ContainerElement container,
        ICollection<Schema3ContainerElement> hoisted)
    {
        return container with
        {
            PrimarySlot = HoistSlot(container.PrimarySlot, hoisted),
            SecondarySlot = HoistSlot(container.SecondarySlot, hoisted),
            CollapsedSlot = HoistSlot(container.CollapsedSlot, hoisted)
        };
    }

    private static Schema3Slot HoistSlot(
        Schema3Slot slot,
        ICollection<Schema3ContainerElement> hoisted)
    {
        if (slot is null)
        {
            return Schema3Slot.Empty("recovered");
        }

        var remaining = new List<Schema3Element>();
        foreach (var child in slot.Children)
        {
            if (child is not Schema3ContainerElement nested)
            {
                remaining.Add(child);
                continue;
            }

            hoisted.Add(nested);
            HoistNestedContainers(nested, hoisted);
        }

        return slot with { Children = remaining };
    }

    private static Schema3EdgeContainer ConvertInlineAutoCollapse(
        Schema3ContainerElement container,
        PlayerLayoutMode layoutMode,
        int index)
    {
        var expandedChildren = container.PrimarySlot.Children.Count > 0
            ? container.PrimarySlot.Children
            : container.CollapsedSlot.Children;
        return new Schema3EdgeContainer(
            string.IsNullOrWhiteSpace(container.InstanceId)
                ? $"migrated-collapse-{index + 1}"
                : container.InstanceId,
            container.Enabled,
            layoutMode == PlayerLayoutMode.Vertical ? LayoutEdge.Right : LayoutEdge.Top,
            0,
            6,
            Math.Clamp(container.ProximityDip, 0, MaximumProximityDip),
            NormalizeAnimation(container.Animation),
            new Schema3Slot("expanded", expandedChildren));
    }

    private static void FlattenLegacyRoot(
        Schema3ContainerElement root,
        ICollection<Schema3ContainerElement> inline,
        ICollection<Schema3EdgeContainer> edges,
        PlayerLayoutMode layoutMode)
    {
        if (root.ContainerKind != LayoutContainerKind.Static)
        {
            if (root.ContainerKind == LayoutContainerKind.AutoCollapse)
            {
                edges.Add(ConvertInlineAutoCollapse(root, layoutMode, edges.Count));
            }
            else
            {
                inline.Add(root);
            }
            return;
        }

        var pendingWidgets = new List<Schema3Element>();
        foreach (var child in root.PrimarySlot.Children)
        {
            if (child is Schema3ContainerElement container)
            {
                FlushLegacyWidgets(pendingWidgets, inline);
                if (container.ContainerKind == LayoutContainerKind.AutoCollapse)
                {
                    edges.Add(ConvertInlineAutoCollapse(container, layoutMode, edges.Count));
                }
                else
                {
                    inline.Add(container);
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
        List<Schema3Element> widgets,
        ICollection<Schema3ContainerElement> inline)
    {
        if (widgets.Count == 0)
        {
            return;
        }

        inline.Add(CreateSchema3StaticContainer(
            $"migrated-inline-{inline.Count + 1}",
            widgets.ToArray()));
        widgets.Clear();
    }

    // ---------- schema 3 测量（DIP → 格） ----------

    private static (int W, int H) MeasureSchema3ContainerCells(
        Schema3ContainerElement container,
        bool vertical,
        int gapDip)
    {
        var primary = MeasureSchema3SlotCells(container.PrimarySlot, vertical, gapDip);
        var secondary = MeasureSchema3SlotCells(container.SecondarySlot, vertical, gapDip);
        var collapsed = MeasureSchema3SlotCells(container.CollapsedSlot, vertical, gapDip);
        return (
            Math.Max(
                1,
                Math.Max(
                    primary.W,
                    Math.Max(
                        secondary.W,
                        Math.Max(collapsed.W, ToCells(container.Geometry?.WidthDip ?? 0))))),
            Math.Max(
                1,
                Math.Max(
                    primary.H,
                    Math.Max(
                        secondary.H,
                        Math.Max(collapsed.H, ToCells(container.Geometry?.HeightDip ?? 0))))));
    }

    private static (int W, int H) MeasureSchema3SlotCells(
        Schema3Slot slot,
        bool vertical,
        int gapDip)
    {
        var gapCells = gapDip > 0
            ? Math.Max(1, (int)Math.Ceiling(gapDip / (double)DefaultCellSizeDip))
            : 0;
        var cells = (slot?.Children ?? [])
            .OfType<Schema3WidgetElement>()
            .Select(widget => MeasureSchema3WidgetCells(widget, vertical))
            .ToArray();
        if (cells.Length == 0)
        {
            return (1, 1);
        }

        if (vertical)
        {
            return (
                Math.Max(1, cells.Max(cell => cell.W)),
                Math.Max(1, cells.Sum(cell => cell.H) + gapCells * Math.Max(0, cells.Length - 1)));
        }

        return (
            Math.Max(1, cells.Sum(cell => cell.W) + gapCells * Math.Max(0, cells.Length - 1)),
            Math.Max(1, cells.Max(cell => cell.H)));
    }

    private static (int W, int H) MeasureSchema3CollapseCells(
        Schema3EdgeContainer edge,
        bool vertical,
        int gapDip) =>
        MeasureSchema3SlotCells(edge.ExpandedSlot, vertical, gapDip);

    private static (int W, int H) MeasureSchema3WidgetCells(
        Schema3WidgetElement widget,
        bool vertical)
    {
        double width;
        double height;
        switch (widget.TypeId)
        {
            case BuiltInWidgetTypeIds.Artwork:
                width = 40;
                height = 40;
                break;
            case BuiltInWidgetTypeIds.MediaText:
            case BuiltInWidgetTypeIds.MediaSource:
            {
                var text = widget.Settings as MediaTextWidgetSettings;
                var combined = text?.TextKind == MediaTextKind.TitleAndArtist;
                width = widget.Geometry?.WidthDip ??
                    (vertical ? 68 : combined ? 150 : 210);
                height = widget.Geometry?.HeightDip ?? 40;
                break;
            }
            case BuiltInWidgetTypeIds.Command:
                var command = widget.Settings as CommandWidgetSettings;
                var buttonSize = Math.Clamp(
                    command?.ButtonSizeDip ?? CommandWidgetSettings.DefaultButtonSizeDip,
                    20,
                    96);
                width = buttonSize;
                height = buttonSize;
                break;
            case BuiltInWidgetTypeIds.Metrics:
                width = 74;
                height = 24;
                break;
            case BuiltInWidgetTypeIds.Spectrum:
                width = 88;
                height = 24;
                break;
            case BuiltInWidgetTypeIds.Separator:
                var separator = widget.Settings as SeparatorWidgetSettings;
                width = (separator?.ThicknessDip ?? 1) + 16;
                height = separator?.LengthDip ?? 22;
                break;
            default:
                width = 24;
                height = 24;
                break;
        }

        return (ToCells(width), ToCells(height));
    }

    private static int ToCells(double dip) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, dip) / DefaultCellSizeDip));

    // ---------- schema 4 标准化 ----------

    private static LayoutProfile NormalizeProfile(LayoutProfile profile)
    {
        var surface = NormalizeSurface(profile.Surface) with { EdgeCollapseEnabled = false };
        var grid = LayoutGridSettings.Normalize(profile.Grid);

        var containers = new List<LayoutContainerElement>();
        foreach (var container in profile.Containers ?? [])
        {
            var normalized = NormalizeContainer(container, containers.Count, grid);
            if (normalized is not null)
            {
                containers.Add(normalized);
            }
        }

        var collapses = new List<LayoutCollapseContainer>();
        foreach (var collapse in profile.CollapseContainers ?? [])
        {
            collapses.Add(NormalizeCollapse(collapse, collapses.Count, grid));
        }

        return profile with
        {
            Surface = surface,
            Grid = grid,
            Containers = EnsureUniqueContainerIds(containers),
            CollapseContainers = EnsureUniqueCollapseIds(collapses)
        };
    }

    private static LayoutSurfaceSettings NormalizeSurface(LayoutSurfaceSettings surface)
    {
        surface ??= LayoutSurfaceSettings.Default;
        return surface with
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
            // schema 4 的窗口外框尺寸来自网格联合边界；固定覆盖会制造第二个事实来源，读取时清除。
            // Schema-4 host size comes from the grid union; fixed overrides would create a second source of truth, so they are cleared.
            WidthDip = null,
            HeightDip = null
        };
    }

    private static LayoutContainerElement? NormalizeContainer(
        LayoutContainerElement container,
        int index,
        LayoutGridSettings grid)
    {
        if (container is null)
        {
            return null;
        }

        var kind = Enum.IsDefined(container.ContainerKind)
            ? container.ContainerKind
            : LayoutContainerKind.Static;
        var normalized = container with
        {
            ContainerKind = kind,
            Orientation = LayoutFlowOrientation.Automatic,
            ContentAlignment = Enum.IsDefined(container.ContentAlignment)
                ? container.ContentAlignment
                : LayoutContentAlignment.Center,
            SecondaryContentAlignment = Enum.IsDefined(container.SecondaryContentAlignment)
                ? container.SecondaryContentAlignment
                : LayoutContentAlignment.Center,
            Trigger = kind == LayoutContainerKind.HoverSwitch
                ? LayoutTriggerMode.PointerNear
                : LayoutTriggerMode.Always,
            ProximityDip = Math.Clamp(container.ProximityDip, 0, MaximumProximityDip),
            Animation = NormalizeAnimation(container.Animation),
            Geometry = NormalizeElementGeometry(container.Geometry),
            PrimarySlot = NormalizeSlot(container.PrimarySlot, "content", kind != LayoutContainerKind.HoverSwitch),
            SecondarySlot = NormalizeSlot(container.SecondarySlot, "unused", allowInteractive: true),
            GridBounds = NormalizeContainerBounds(container.GridBounds, index, grid)
        };
        return string.Equals(normalized.InstanceId, "media-interaction", StringComparison.Ordinal)
            ? NormalizeDefaultMediaInteraction(normalized)
            : normalized;
    }

    private static LayoutCollapseContainer NormalizeCollapse(
        LayoutCollapseContainer collapse,
        int index,
        LayoutGridSettings grid)
    {
        if (collapse is null)
        {
            return new LayoutCollapseContainer(
                $"recovered-collapse-{index + 1}",
                false,
                new LayoutGridRect(0, 0, 1, 1),
                new LayoutAttachment(string.Empty, LayoutEdge.Top),
                MinimumTriggerThicknessDip,
                0,
                LayoutAnimationSettings.Default,
                LayoutSlot.Empty("expanded"));
        }

        return collapse with
        {
            InstanceId = string.IsNullOrWhiteSpace(collapse.InstanceId)
                ? $"recovered-collapse-{index + 1}"
                : collapse.InstanceId,
            GridBounds = NormalizeContainerBounds(collapse.GridBounds, index, grid),
            Attachment = collapse.Attachment is null
                ? new LayoutAttachment(string.Empty, LayoutEdge.Top)
                : collapse.Attachment,
            TriggerThicknessDip = Math.Clamp(
                collapse.TriggerThicknessDip,
                MinimumTriggerThicknessDip,
                MaximumTriggerThicknessDip),
            ProximityDip = Math.Clamp(collapse.ProximityDip, 0, MaximumProximityDip),
            Animation = NormalizeAnimation(collapse.Animation),
            ExpandedSlot = NormalizeSlot(collapse.ExpandedSlot, "expanded", allowInteractive: true)
        };
    }

    private static LayoutGridRect NormalizeContainerBounds(
        LayoutGridRect? bounds,
        int index,
        LayoutGridSettings grid)
    {
        if (bounds is null)
        {
            return new LayoutGridRect(
                Math.Min(index, Math.Max(0, grid.Columns - 1)),
                Math.Min(index, Math.Max(0, grid.Rows - 1)),
                1,
                1);
        }

        var normalized = bounds.Normalized;
        return new LayoutGridRect(
            Math.Clamp(normalized.X, 0, Math.Max(0, grid.Columns - 1)),
            Math.Clamp(normalized.Y, 0, Math.Max(0, grid.Rows - 1)),
            Math.Max(1, normalized.Width),
            Math.Max(1, normalized.Height));
    }

    private static LayoutSlot NormalizeSlot(
        LayoutSlot slot,
        string fallbackSlotId,
        bool allowInteractive)
    {
        slot ??= LayoutSlot.Empty(fallbackSlotId);
        var children = new List<LayoutElement>();
        foreach (var child in slot.Children ?? [])
        {
            if (child is null)
            {
                continue;
            }

            if (child is LayoutContainerElement nested)
            {
                // schema 4 禁止槽位嵌套容器：静态嵌套展开为组件，其余禁用并保留展开内容。
                // Schema 4 forbids nested containers in slots: static nesting flattens, others stay disabled with content preserved.
                if (nested.ContainerKind == LayoutContainerKind.Static)
                {
                    children.AddRange(nested.PrimarySlot.Children
                        .Select(item => NormalizeElement(item, allowInteractive))
                        .Where(item => item is not null)
                        .Cast<LayoutElement>());
                }
                else
                {
                    var disabled = nested with
                    {
                        Enabled = false,
                        PrimarySlot = NormalizeSlot(nested.PrimarySlot, "content", allowInteractive: false),
                        SecondarySlot = NormalizeSlot(nested.SecondarySlot, "unused", allowInteractive: false)
                    };
                    foreach (var widget in disabled.PrimarySlot.Children.Concat(disabled.SecondarySlot.Children)
                        .OfType<LayoutWidgetElement>())
                    {
                        children.Add(NormalizeElement(widget with { Enabled = false }, allowInteractive: false)!);
                    }
                }

                continue;
            }

            if (NormalizeElement(child, allowInteractive) is { } element)
            {
                children.Add(element);
            }
        }

        return slot with { SlotId = string.IsNullOrWhiteSpace(slot.SlotId) ? fallbackSlotId : slot.SlotId, Children = children };
    }

    private static LayoutElement? NormalizeElement(
        LayoutElement element,
        bool allowInteractive)
    {
        if (element is LayoutWidgetElement widget)
        {
            var enabled = widget.Enabled;
            if (enabled && (!ComponentCatalog.TryGet(widget.TypeId, out _) ||
                    (!allowInteractive && ComponentCatalog.IsInteractive(widget))))
            {
                enabled = false;
            }

            var skin = ComponentSkinCatalog.Normalize(
                widget.TypeId,
                widget.SkinId,
                widget.SkinVersion,
                widget.SkinSettings);
            return widget with
            {
                Enabled = enabled,
                Geometry = NormalizeElementGeometry(widget.Geometry),
                Settings = NormalizeWidgetSettings(widget.TypeId, widget.Settings),
                SkinId = skin?.SkinId,
                SkinVersion = skin?.Version,
                SkinSettings = skin?.Settings,
                GridBounds = NormalizeWidgetBounds(widget.GridBounds)
            };
        }

        return null;
    }

    private static LayoutGridRect NormalizeWidgetBounds(LayoutGridRect? bounds)
    {
        if (bounds is null)
        {
            return new LayoutGridRect(0, 0, 1, 1);
        }

        var normalized = bounds.Normalized;
        return new LayoutGridRect(
            Math.Max(0, normalized.X),
            Math.Max(0, normalized.Y),
            Math.Max(1, normalized.Width),
            Math.Max(1, normalized.Height));
    }

    private static LayoutGeometry NormalizeElementGeometry(LayoutGeometry geometry)
    {
        geometry ??= LayoutGeometry.Auto;
        var normalized = geometry with
        {
            // 组件/容器外框由网格矩形决定；DIP 尺寸覆盖在迁移后清空，避免与网格成为两个事实来源。
            // The grid rectangle owns the frame size; legacy DIP overrides are cleared so the grid stays the single source of truth.
            WidthDip = null,
            HeightDip = null,
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
                    BandCount = Math.Clamp(
                        spectrum.BandCount,
                        1,
                        SpectrumWidgetSettings.MaximumBandCount),
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

    private static LayoutContainerElement NormalizeDefaultMediaInteraction(
        LayoutContainerElement container)
    {
        // 旧默认档案把标题和歌手作为两个横向控件；合并为稳定宽度的两行信息，避免控制按钮被长标题推出窗口。
        // Older defaults placed title and artist as two horizontal widgets; merge them into a stable two-line block so long titles cannot push controls out.
        var title = container.SecondarySlot.Children
            .OfType<LayoutWidgetElement>()
            .FirstOrDefault(widget =>
                widget.Settings is MediaTextWidgetSettings { TextKind: MediaTextKind.Title });
        var artist = container.SecondarySlot.Children
            .OfType<LayoutWidgetElement>()
            .FirstOrDefault(widget =>
                widget.Settings is MediaTextWidgetSettings { TextKind: MediaTextKind.Artist });
        if (title is null || artist is null)
        {
            return container;
        }

        var combined = title with
        {
            Settings = title.Settings is MediaTextWidgetSettings text
                ? text with { TextKind = MediaTextKind.TitleAndArtist }
                : title.Settings
        };
        return container with
        {
            SecondarySlot = container.SecondarySlot with
            {
                Children = container.SecondarySlot.Children
                    .Where(child => !string.Equals(child.InstanceId, artist.InstanceId, StringComparison.Ordinal))
                    .Select(child => string.Equals(child.InstanceId, title.InstanceId, StringComparison.Ordinal)
                        ? combined
                        : child)
                    .ToArray()
            }
        };
    }

    private static IReadOnlyList<LayoutContainerElement> EnsureUniqueContainerIds(
        IReadOnlyList<LayoutContainerElement> containers)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        return containers.Select((container, index) =>
        {
            var id = string.IsNullOrWhiteSpace(container.InstanceId)
                ? $"container-{index + 1}"
                : container.InstanceId;
            while (!used.Add(id))
            {
                id = $"{id}-{index + 1}";
            }

            return container with { InstanceId = id };
        }).ToArray();
    }

    private static IReadOnlyList<LayoutCollapseContainer> EnsureUniqueCollapseIds(
        IReadOnlyList<LayoutCollapseContainer> collapses)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        return collapses.Select((collapse, index) =>
        {
            var id = string.IsNullOrWhiteSpace(collapse.InstanceId)
                ? $"collapse-{index + 1}"
                : collapse.InstanceId;
            while (!used.Add(id))
            {
                id = $"{id}-{index + 1}";
            }

            return collapse with { InstanceId = id };
        }).ToArray();
    }

    private static int? ClampNullable(int? value, int minimum, int maximum)
    {
        return value.HasValue
            ? Math.Clamp(value.Value, minimum, maximum)
            : null;
    }

    private sealed record LayoutCollapsePlacement(
        string AnchorId,
        LayoutEdge Side,
        LayoutGridRect Rect);

    private sealed class PlacedContainer(Schema3ContainerElement model, (int W, int H) cells)
    {
        internal Schema3ContainerElement Model { get; } = model;
        internal string InstanceId => Model.InstanceId;
        internal int X { get; set; }
        internal int Y { get; set; }
        internal int W { get; set; } = cells.W;
        internal int H { get; set; } = cells.H;
    }
}
