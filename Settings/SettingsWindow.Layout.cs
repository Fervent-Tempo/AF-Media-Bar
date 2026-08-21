using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Controls;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Settings;

/// <summary>
/// 负责当前方向布局的可视化拼贴、主要属性和档案内撤销；方向跟随窗口设置，不提供独立档案切换。
/// Owns visual composition, primary properties, and per-profile undo for the current orientation; direction follows window settings without a separate selector.
/// </summary>
public partial class SettingsWindow
{
    private const string NewWidgetDragFormat = "AFMediaBar.Layout.NewWidget";
    private const string ExistingWidgetDragFormat = "AFMediaBar.Layout.ExistingWidget";
    private const string ExistingContainerDragFormat = "AFMediaBar.Layout.ExistingContainer";

    private readonly LayoutEditHistoryService _layoutEditHistory = new();
    private LayoutProfileKey _layoutEditorProfileKey = LayoutProfileKey.Horizontal;
    private LayoutEditorSelection? _layoutEditorSelection;
    private bool _layoutEditorSyncing;
    private bool _layoutPropertySyncing;
    private Point _layoutDragStart;
    private readonly List<ComponentLayoutSurface> _layoutPreviewSurfaces = [];
    private readonly List<ComponentLayoutSurface> _layoutPaletteSurfaces = [];
    private Popup? _layoutDragPreviewPopup;
    private Border? _layoutPreviewDropOverlay;

    private void InitializeLayoutEditor()
    {
        PopulateLayoutEditorOptions();
        _layoutEditorProfileKey = ResolveCurrentLayoutProfile();
        PopulateComponentPalette();
        RefreshLayoutEditor();
    }

    private void SyncLayoutEditor()
    {
        if (!_isInitialized)
        {
            return;
        }

        var currentKey = ResolveCurrentLayoutProfile();
        if (currentKey != _layoutEditorProfileKey)
        {
            _layoutEditorProfileKey = currentKey;
            _layoutEditorSelection = null;
        }

        PopulateLayoutEditorOptions();
        PopulateComponentPalette();
        RefreshLayoutEditor();
    }

    private void PopulateLayoutEditorOptions()
    {
        _layoutEditorSyncing = true;
        try
        {
            LayoutNewEdgeComboBox.Items.Clear();
            var unavailableEdge = GetUnavailableTaskbarEdge();
            foreach (var edge in Enum.GetValues<LayoutEdge>())
            {
                LayoutNewEdgeComboBox.Items.Add(new ComboBoxItem
                {
                    Content = GetEdgeName(edge),
                    Tag = edge,
                    IsEnabled = unavailableEdge != edge
                });
            }

            LayoutNewEdgeComboBox.SelectedIndex = FindFirstEnabledIndex(LayoutNewEdgeComboBox);
            if (LayoutEditorContextText is not null)
            {
                var window = _coordinator.Current.Window;
                var host = Loc.Get(window.HostMode == WindowHostMode.Taskbar
                    ? "Settings.Layout.DockToTaskbar"
                    : "Settings.Layout.Floating");
                var orientation = Loc.Get(_layoutEditorProfileKey == LayoutProfileKey.Vertical
                    ? "Settings.Common.Vertical"
                    : "Settings.Common.Horizontal");
                LayoutEditorContextText.Text = Loc.Get(
                    "Settings.Layout.EditorCurrentContextFormat",
                    host,
                    orientation);
            }
        }
        finally
        {
            _layoutEditorSyncing = false;
        }
    }

    private void PopulateComponentPalette()
    {
        if (LayoutComponentPalette is null)
        {
            return;
        }

        foreach (var surface in _layoutPaletteSurfaces)
        {
            surface.Dispose();
        }
        _layoutPaletteSurfaces.Clear();
        LayoutComponentPalette.Children.Clear();
        foreach (var entry in EnumeratePaletteEntries())
        {
            var preview = CreatePalettePreview(entry.Token);
            var button = new Button
            {
                Width = 96,
                Height = 82,
                Content = new StackPanel
                {
                    IsHitTestVisible = false,
                    Children =
                    {
                        new Viewbox
                        {
                            Width = 78,
                            Height = 48,
                            Stretch = Stretch.Uniform,
                            Child = preview
                        },
                        new TextBlock
                        {
                            Text = entry.Label,
                            FontSize = 10,
                            TextAlignment = TextAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Margin = new Thickness(2, 3, 2, 0)
                        }
                    }
                },
                Tag = entry.Token,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(4),
                Cursor = Cursors.Hand,
                Style = TryFindResource("SettingsActionButtonStyle") as Style,
                ToolTip = entry.Description
            };
            button.PreviewMouseLeftButtonDown += LayoutDragSource_OnPreviewMouseLeftButtonDown;
            button.PreviewMouseMove += LayoutPaletteButton_OnPreviewMouseMove;
            button.Click += LayoutPaletteButton_OnClick;
            LayoutComponentPalette.Children.Add(button);
        }
    }

    private ComponentLayoutSurface CreatePalettePreview(string paletteToken)
    {
        var parts = paletteToken.Split('|', 2);
        var typeId = parts[0];
        var settings = ComponentCatalog.CreateDefaultSettings(typeId);
        if (parts.Length == 2 &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var option))
        {
            settings = typeId switch
            {
                BuiltInWidgetTypeIds.Command when Enum.IsDefined(typeof(MediaCommandKind), option) =>
                    new CommandWidgetSettings((MediaCommandKind)option, 36),
                BuiltInWidgetTypeIds.MediaText when Enum.IsDefined(typeof(MediaTextKind), option) =>
                    new MediaTextWidgetSettings((MediaTextKind)option, false, 14, 1),
                _ => settings
            };
        }

        var widget = new LayoutWidgetElement(
            "palette-widget",
            true,
            LayoutGeometry.Auto,
            typeId,
            settings);
        var container = new LayoutContainerElement(
            "palette-container",
            true,
            LayoutGeometry.Auto,
            LayoutContainerKind.Static,
            LayoutFlowOrientation.Automatic,
            LayoutContentAlignment.Center,
            LayoutContentAlignment.Center,
            LayoutTriggerMode.Always,
            0,
            LayoutAnimationSettings.Default,
            new LayoutSlot("palette-primary", [widget]),
            LayoutSlot.Empty("palette-secondary"),
            LayoutSlot.Empty("palette-collapsed"));
        var profile = new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default with { GapDip = 2, WidthDip = null, HeightDip = null },
            [container],
            []);
        var surface = new ComponentLayoutSurface();
        surface.SetMediaSnapshot(CreateLayoutPreviewSnapshot());
        surface.Apply(profile, pointerNear: true);
        surface.IsHitTestVisible = false;
        _layoutPaletteSurfaces.Add(surface);
        return surface;
    }

    private static IEnumerable<PaletteEntry> EnumeratePaletteEntries()
    {
        foreach (var definition in ComponentCatalog.All)
        {
            if (definition.TypeId == BuiltInWidgetTypeIds.Command)
            {
                foreach (var command in Enum.GetValues<MediaCommandKind>())
                {
                    yield return new PaletteEntry(
                        $"{definition.TypeId}|{(int)command}",
                        Loc.Get(GetCommandOptionKey(command)),
                        Loc.Get(definition.DescriptionResourceKey));
                }

                continue;
            }

            if (definition.TypeId == BuiltInWidgetTypeIds.MediaText)
            {
                foreach (var kind in new[]
                {
                    MediaTextKind.Title,
                    MediaTextKind.Artist,
                    MediaTextKind.TitleAndArtist
                })
                {
                    yield return new PaletteEntry(
                        $"{definition.TypeId}|{(int)kind}",
                        GetMediaTextOptionLabel(kind),
                        Loc.Get(definition.DescriptionResourceKey));
                }

                continue;
            }

            yield return new PaletteEntry(
                definition.TypeId,
                Loc.Get(definition.NameResourceKey),
                Loc.Get(definition.DescriptionResourceKey));
        }
    }

    private void RefreshLayoutEditor()
    {
        if (_layoutEditorSyncing || !_isInitialized || LayoutVisualEditorHost is null)
        {
            return;
        }

        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        var selectedId = _layoutEditorSelection?.InstanceId;
        _layoutEditorSelection = string.IsNullOrWhiteSpace(selectedId)
            ? null
            : ResolveSelection(profile, selectedId);
        PopulateLayoutObjectList(profile);
        DisposeLayoutPreviewSurfaces();
        LayoutVisualEditorHost.Child = BuildVisualEditor(profile);
        foreach (var surface in _layoutPreviewSurfaces)
        {
            surface.SetDesignSelection(_layoutEditorSelection?.InstanceId);
        }
        RefreshSlotOptions();
        RefreshSelectionText();
        RefreshLayoutProperties();
        UpdateLayoutEditorButtons();
        LayoutEditorMessageText.Text = string.Empty;
    }

    private void PopulateLayoutObjectList(LayoutProfile profile)
    {
        if (LayoutObjectList is null)
        {
            return;
        }

        _layoutEditorSyncing = true;
        try
        {
            LayoutObjectList.Items.Clear();
            foreach (var container in profile.InlineContainers)
            {
                AddLayoutObjectItem(container, 0, null, LayoutSlotKind.Primary);
                AddLayoutObjectSlotItems(container.PrimarySlot, container.InstanceId, LayoutSlotKind.Primary, 1);
                if (container.ContainerKind == LayoutContainerKind.HoverSwitch)
                {
                    AddLayoutObjectSlotItems(container.SecondarySlot, container.InstanceId, LayoutSlotKind.Secondary, 1);
                }
            }

            foreach (var edge in profile.EdgeContainers)
            {
                AddLayoutObjectItem(edge, 0, null, LayoutSlotKind.Expanded);
                AddLayoutObjectSlotItems(edge.ExpandedSlot, edge.InstanceId, LayoutSlotKind.Expanded, 1);
            }

            var selected = LayoutObjectList.Items
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => item.Tag is LayoutEditorSelection selection &&
                    selection.InstanceId == _layoutEditorSelection?.InstanceId);
            LayoutObjectList.SelectedItem = selected;
        }
        finally
        {
            _layoutEditorSyncing = false;
        }
    }

    private void AddLayoutObjectSlotItems(
        LayoutSlot slot,
        string parentId,
        LayoutSlotKind slotKind,
        int depth)
    {
        foreach (var child in slot.Children)
        {
            var selection = child switch
            {
                LayoutWidgetElement widget => new LayoutEditorSelection(
                    widget.InstanceId,
                    LayoutEditorNodeKind.Widget,
                    parentId,
                    slotKind,
                    widget),
                LayoutContainerElement container => new LayoutEditorSelection(
                    container.InstanceId,
                    LayoutEditorNodeKind.InlineContainer,
                    parentId,
                    slotKind,
                    container),
                _ => null
            };
            if (selection is null)
            {
                continue;
            }

            AddLayoutObjectItem(child, depth, selection);
            if (child is LayoutContainerElement nested)
            {
                AddLayoutObjectSlotItems(nested.PrimarySlot, nested.InstanceId, LayoutSlotKind.Primary, depth + 1);
                if (nested.ContainerKind == LayoutContainerKind.HoverSwitch)
                {
                    AddLayoutObjectSlotItems(nested.SecondarySlot, nested.InstanceId, LayoutSlotKind.Secondary, depth + 1);
                }
            }
        }
    }

    private void AddLayoutObjectItem(
        LayoutElement element,
        int depth,
        string? parentId,
        LayoutSlotKind slotKind)
    {
        var selection = element switch
        {
            LayoutWidgetElement widget => new LayoutEditorSelection(
                widget.InstanceId,
                LayoutEditorNodeKind.Widget,
                parentId,
                slotKind,
                widget),
            LayoutContainerElement container => new LayoutEditorSelection(
                container.InstanceId,
                LayoutEditorNodeKind.InlineContainer,
                parentId,
                slotKind,
                container),
            _ => null
        };
        AddLayoutObjectItem(element, depth, selection);
    }

    private void AddLayoutObjectItem(
        LayoutEdgeContainer edge,
        int depth,
        string? parentId,
        LayoutSlotKind slotKind)
    {
        AddLayoutObjectItem(edge, depth, new LayoutEditorSelection(
            edge.InstanceId,
            LayoutEditorNodeKind.EdgeContainer,
            parentId,
            slotKind,
            edge));
    }

    private void AddLayoutObjectItem(LayoutElement element, int depth, LayoutEditorSelection? selection)
    {
        var label = element switch
        {
            LayoutWidgetElement widget => GetWidgetTitle(widget),
            LayoutContainerElement { ContainerKind: LayoutContainerKind.HoverSwitch } => Loc.Get("Settings.Layout.ContainerHoverSwitch"),
            LayoutContainerElement => Loc.Get("Settings.Layout.ContainerStatic"),
            _ => element.InstanceId
        };
        var item = new ListBoxItem
        {
            Tag = selection,
            Padding = new Thickness(8, 5, 6, 5),
            Content = new TextBlock
            {
                Text = new string(' ', depth * 2) + label,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = element.InstanceId
            }
        };
        LayoutObjectList.Items.Add(item);
    }

    private void AddLayoutObjectItem(LayoutEdgeContainer edge, int depth, LayoutEditorSelection selection)
    {
        var item = new ListBoxItem
        {
            Tag = selection,
            Padding = new Thickness(8, 5, 6, 5),
            Content = new TextBlock
            {
                Text = new string(' ', depth * 2) +
                    $"{Loc.Get("Settings.Layout.ContainerAutoCollapse")} · {GetEdgeName(edge.Edge)}",
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = edge.InstanceId
            }
        };
        LayoutObjectList.Items.Add(item);
    }

    private void LayoutObjectList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_layoutEditorSyncing ||
            LayoutObjectList.SelectedItem is not ListBoxItem { Tag: LayoutEditorSelection selection })
        {
            return;
        }

        SelectLayoutNode(selection);
    }

    private FrameworkElement BuildVisualEditor(LayoutProfile profile)
    {
        // 预览使用与主窗口相同的组件树，再用 Viewbox 适配可用区域，避免固定边缘栏裁掉内容。
        // Reuse the runtime component tree and fit it with a Viewbox so fixed edge bands cannot crop the preview.
        var stripSize = LayoutRuntimeService.CalculateDesiredSize(profile);
        var edgeSizes = profile.EdgeContainers
            .Where(container => container.Enabled)
            .Select(container => (container.Edge, Size: LayoutRuntimeService.MeasureEdgeContainer(profile, container)))
            .ToArray();
        var leftBand = Math.Max(86, edgeSizes.Where(item => item.Edge == LayoutEdge.Left).Select(item => item.Size.WidthDip).DefaultIfEmpty().Max() + 24);
        var rightBand = Math.Max(86, edgeSizes.Where(item => item.Edge == LayoutEdge.Right).Select(item => item.Size.WidthDip).DefaultIfEmpty().Max() + 24);
        var topBand = Math.Max(72, edgeSizes.Where(item => item.Edge == LayoutEdge.Top).Select(item => item.Size.HeightDip).DefaultIfEmpty().Max() + 24);
        var bottomBand = Math.Max(72, edgeSizes.Where(item => item.Edge == LayoutEdge.Bottom).Select(item => item.Size.HeightDip).DefaultIfEmpty().Max() + 24);
        var centerWidth = Math.Max(360, stripSize.WidthDip + 32);
        var centerHeight = Math.Max(220, stripSize.HeightDip + 32);
        var composition = new Grid
        {
            Width = leftBand + centerWidth + rightBand,
            Height = topBand + centerHeight + bottomBand,
            Background = new SolidColorBrush(Color.FromRgb(35, 43, 52)),
            ClipToBounds = true,
            AllowDrop = true
        };
        composition.RowDefinitions.Add(new RowDefinition { Height = new GridLength(topBand) });
        composition.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        composition.RowDefinitions.Add(new RowDefinition { Height = new GridLength(bottomBand) });
        composition.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftBand) });
        composition.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        composition.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightBand) });

        AddPreviewEdgeStrip(composition, profile, LayoutEdge.Top, 0, 1, Orientation.Horizontal);
        AddPreviewEdgeStrip(composition, profile, LayoutEdge.Left, 1, 0, Orientation.Vertical);
        AddPreviewEdgeStrip(composition, profile, LayoutEdge.Right, 1, 2, Orientation.Vertical);
        AddPreviewEdgeStrip(composition, profile, LayoutEdge.Bottom, 2, 1, Orientation.Horizontal);

        var inlineSurface = CreatePreviewSurface(profile);
        inlineSurface.HorizontalAlignment = HorizontalAlignment.Center;
        inlineSurface.VerticalAlignment = VerticalAlignment.Center;
        inlineSurface.Margin = new Thickness(16);
        // 释放高亮只属于设计模式，避免拖动时用户失去当前槽位的空间反馈；运行时窗口不会创建此层。
        // The drop highlight exists only in design mode so users keep spatial feedback while dragging; runtime never creates it.
        var dropOverlay = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(54, 86, 156, 255)),
            BorderBrush = FindBrush("MenuHighlightTextBrush", Brushes.White),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = Loc.Get("Settings.Layout.EditorDropHere"),
                Foreground = FindBrush("MenuHighlightTextBrush", Brushes.White),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        _layoutPreviewDropOverlay = dropOverlay;
        var centerContent = new Grid();
        centerContent.Children.Add(inlineSurface);
        centerContent.Children.Add(dropOverlay);
        var center = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromRgb(35, 43, 52)),
            BorderBrush = FindBrush("MenuBorderBrush", Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = centerContent,
            AllowDrop = true,
            ToolTip = Loc.Get("Settings.Layout.EditorDropHere")
        };
        center.DragOver += LayoutVisualEditorHost_OnDragOver;
        center.Drop += LayoutVisualEditorHost_OnDrop;
        center.DragEnter += LayoutPreviewDropHost_OnDragEnter;
        center.DragLeave += LayoutPreviewDropHost_OnDragLeave;
        Grid.SetRow(center, 1);
        Grid.SetColumn(center, 1);
        composition.Children.Add(center);

        composition.DragOver += LayoutVisualEditorHost_OnDragOver;
        composition.Drop += LayoutVisualEditorHost_OnDrop;

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Color.FromRgb(35, 43, 52)),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Child = composition
            }
        };
    }

    private void AddPreviewEdgeStrip(
        Grid grid,
        LayoutProfile profile,
        LayoutEdge edge,
        int row,
        int column,
        Orientation orientation)
    {
        var panel = new StackPanel
        {
            Orientation = orientation,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var container in profile.EdgeContainers.Where(item => item.Edge == edge && item.Enabled))
        {
            var surface = CreatePreviewSurface(profile, container);
            surface.Margin = new Thickness(3);
            panel.Children.Add(surface);
        }

        var area = new Border
        {
            Margin = new Thickness(3),
            Padding = new Thickness(3),
            Background = Brushes.Transparent,
            BorderBrush = GetUnavailableTaskbarEdge() == edge
                ? FindBrush("MenuSeparatorBrush", Brushes.Gray)
                : Brushes.Transparent,
            BorderThickness = GetUnavailableTaskbarEdge() == edge ? new Thickness(1) : new Thickness(0),
            Child = panel,
            AllowDrop = true,
            Tag = edge,
            ToolTip = GetUnavailableTaskbarEdge() == edge
                ? Loc.Get("Settings.Layout.EditorTaskbarEdgeUnavailable")
                : GetEdgeName(edge)
        };
        area.DragOver += LayoutEdgeArea_OnDragOver;
        area.Drop += LayoutEdgeArea_OnDrop;
        Grid.SetRow(area, row);
        Grid.SetColumn(area, column);
        grid.Children.Add(area);
    }

    private ComponentLayoutSurface CreatePreviewSurface(LayoutProfile profile, LayoutEdgeContainer? edge = null)
    {
        var surface = new ComponentLayoutSurface();
        surface.SetDesignMode(true);
        surface.DesignElementSelected += LayoutPreviewSurface_OnElementSelected;
        surface.DesignElementDragRequested += LayoutPreviewSurface_OnElementDragRequested;
        surface.DesignDropTargetDragOver += LayoutPreviewSurface_OnDropTargetDragOver;
        surface.DesignDropRequested += LayoutPreviewSurface_OnDropRequested;
        surface.SetMediaSnapshot(CreateLayoutPreviewSnapshot());
        if (edge is null)
        {
            surface.Apply(profile, pointerNear: ResolvePreviewPointerNear());
        }
        else
        {
            surface.ApplyEdge(profile, edge);
        }

        _layoutPreviewSurfaces.Add(surface);
        return surface;
    }

    private bool ResolvePreviewPointerNear()
    {
        return _layoutEditorSelection?.SlotKind == LayoutSlotKind.Primary
            ? false
            : true;
    }

    private static MediaSnapshot CreateLayoutPreviewSnapshot() => new(
        true,
        true,
        true,
        true,
        true,
        Loc.Get("Settings.Layout.EditorPreviewTitle"),
        Loc.Get("Settings.Layout.EditorPreviewArtist"),
        "design-preview",
        Loc.Get("Settings.Layout.EditorPreviewSource"),
        null);

    private void LayoutPreviewSurface_OnElementSelected(object? sender, LayoutDesignElementEventArgs e)
    {
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        _layoutEditorSelection = ResolveSelection(profile, e.InstanceId);
        foreach (var surface in _layoutPreviewSurfaces)
        {
            surface.SetDesignSelection(e.InstanceId);
            surface.SetPointerNear(ResolvePreviewPointerNear());
        }
        RefreshSlotOptions();
        RefreshSelectionText();
        RefreshLayoutProperties();
        UpdateLayoutEditorButtons();
    }

    private void LayoutPreviewSurface_OnElementDragRequested(object? sender, LayoutDesignElementEventArgs e)
    {
        if (e.Source is not UIElement source)
        {
            return;
        }

        BeginVisualDrag(
            source,
            new DataObject(
                e.IsContainer ? ExistingContainerDragFormat : ExistingWidgetDragFormat,
                e.InstanceId),
            DragDropEffects.Move);
    }

    private void LayoutPreviewSurface_OnDropTargetDragOver(object? sender, LayoutDesignDropEventArgs e)
    {
        var drag = e.DragEventArgs;
        drag.Effects = drag.Data.GetDataPresent(NewWidgetDragFormat)
            ? DragDropEffects.Copy
            : drag.Data.GetDataPresent(ExistingWidgetDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
        if (drag.Effects == DragDropEffects.None)
        {
            return;
        }

        if (_layoutPreviewDropOverlay is not null)
        {
            _layoutPreviewDropOverlay.Visibility = Visibility.Visible;
        }
        drag.Handled = true;
    }

    private void LayoutPreviewSurface_OnDropRequested(object? sender, LayoutDesignDropEventArgs e)
    {
        if (_layoutPreviewDropOverlay is not null)
        {
            _layoutPreviewDropOverlay.Visibility = Visibility.Collapsed;
        }

        var target = new LayoutDropTarget(e.ContainerId, e.SlotKind);
        if (e.DragEventArgs.Data.GetData(ExistingContainerDragFormat) is string sourceId)
        {
            // 容器只允许在同一层级卡片之间重排；槽位仅接受组件，避免把不可嵌套的容器拖入后静默无效。
            // Containers reorder only among same-level cards; slots accept widgets only so unsupported nesting never appears to succeed silently.
            TryApplyProfile(profile => LayoutEditorService.TryReorderTopLevel(
                profile,
                sourceId,
                e.ContainerId,
                out var updated) ? updated : null);
        }
        else
        {
            ApplyDrop(e.DragEventArgs, target);
        }
        e.DragEventArgs.Handled = true;
    }

    private void DisposeLayoutPreviewSurfaces()
    {
        if (_layoutPreviewDropOverlay is not null)
        {
            _layoutPreviewDropOverlay.Visibility = Visibility.Collapsed;
            _layoutPreviewDropOverlay = null;
        }
        foreach (var surface in _layoutPreviewSurfaces)
        {
            surface.Dispose();
        }
        _layoutPreviewSurfaces.Clear();
    }

    private void DisposeLayoutEditorSurfaces()
    {
        DisposeLayoutPreviewSurfaces();
        foreach (var surface in _layoutPaletteSurfaces)
        {
            surface.Dispose();
        }
        _layoutPaletteSurfaces.Clear();
    }

    private void AddEdgeArea(
        Grid grid,
        LayoutProfile profile,
        LayoutEdge edge,
        int row,
        int column,
        Orientation orientation)
    {
        var panel = new StackPanel
        {
            Orientation = orientation,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var container in profile.EdgeContainers.Where(item => item.Edge == edge))
        {
            panel.Children.Add(BuildEdgeContainerCard(container));
        }

        var area = new Border
        {
            Margin = new Thickness(3),
            Padding = new Thickness(3),
            Background = Brushes.Transparent,
            BorderBrush = GetUnavailableTaskbarEdge() == edge
                ? FindBrush("MenuSeparatorBrush", Brushes.Gray)
                : Brushes.Transparent,
            BorderThickness = GetUnavailableTaskbarEdge() == edge
                ? new Thickness(1)
                : new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Child = panel,
            AllowDrop = true,
            Tag = edge,
            ToolTip = GetUnavailableTaskbarEdge() == edge
                ? Loc.Get("Settings.Layout.EditorTaskbarEdgeUnavailable")
                : GetEdgeName(edge)
        };
        area.DragOver += LayoutEdgeArea_OnDragOver;
        area.Drop += LayoutEdgeArea_OnDrop;
        Grid.SetRow(area, row);
        Grid.SetColumn(area, column);
        grid.Children.Add(area);
    }

    private void LayoutEdgeArea_OnDragOver(object sender, DragEventArgs e)
    {
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        e.Effects = e.Data.GetData(ExistingContainerDragFormat) is string sourceId &&
            LayoutEditorService.Find(profile, sourceId) is LayoutEdgeContainer &&
            sender is Border { Tag: LayoutEdge edge } &&
            GetUnavailableTaskbarEdge() != edge
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void LayoutEdgeArea_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: LayoutEdge edge } ||
            e.Data.GetData(ExistingContainerDragFormat) is not string sourceId)
        {
            return;
        }

        if (!TryApplyProfile(profile =>
        {
            if (LayoutEditorService.Find(profile, sourceId) is not LayoutEdgeContainer source)
            {
                return null;
            }

            return LayoutEditorService.TryUpdateEdgeContainer(
                profile,
                source.InstanceId,
                edge,
                GetUnavailableTaskbarEdge(),
                source.OffsetDip,
                source.TriggerThicknessDip,
                source.ProximityDip,
                source.Animation,
                out var updated,
                out _) ? updated : null;
        }))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorTaskbarEdgeUnavailable");
        }
        e.Handled = true;
    }

    private FrameworkElement BuildInlineContainerCard(
        LayoutProfile profile,
        LayoutContainerElement container)
    {
        var isSelected = _layoutEditorSelection?.InstanceId == container.InstanceId;
        var activeSlot = ResolveVisibleSlot(container);
        var target = new LayoutDropTarget(container.InstanceId, activeSlot);
        var content = BuildSlotContent(container, activeSlot, target);
        var titleKey = container.ContainerKind == LayoutContainerKind.HoverSwitch
            ? "Settings.Layout.ContainerHoverSwitch"
            : "Settings.Layout.ContainerStatic";
        var title = container.ContainerKind == LayoutContainerKind.HoverSwitch
            ? $"{Loc.Get(titleKey)} · {GetSlotName(activeSlot)}"
            : Loc.Get(titleKey);
        return CreateContainerCard(
            title,
            container.InstanceId,
            new LayoutEditorSelection(
                container.InstanceId,
                LayoutEditorNodeKind.InlineContainer,
                null,
                activeSlot,
                container),
            content,
            isSelected,
            profile.LayoutMode == PlayerLayoutMode.Vertical ? 126 : 150);
    }

    private FrameworkElement BuildEdgeContainerCard(LayoutEdgeContainer container)
    {
        var target = new LayoutDropTarget(container.InstanceId, LayoutSlotKind.Expanded);
        var content = BuildSlotContent(container.ExpandedSlot, target);
        return CreateContainerCard(
            Loc.Get("Settings.Layout.EditorExpandedContent"),
            container.InstanceId,
            new LayoutEditorSelection(
                container.InstanceId,
                LayoutEditorNodeKind.EdgeContainer,
                null,
                LayoutSlotKind.Expanded,
                container),
            content,
            _layoutEditorSelection?.InstanceId == container.InstanceId,
            112);
    }

    private Border CreateContainerCard(
        string title,
        string instanceId,
        LayoutEditorSelection selection,
        FrameworkElement content,
        bool selected,
        double minWidth)
    {
        var panel = new StackPanel();
        var header = new TextBlock
        {
            Text = title,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Cursor = Cursors.SizeAll,
            Tag = selection
        };
        header.PreviewMouseLeftButtonDown += LayoutDragSource_OnPreviewMouseLeftButtonDown;
        header.PreviewMouseMove += LayoutContainerCard_OnPreviewMouseMove;
        panel.Children.Add(header);
        panel.Children.Add(content);
        var card = new Border
        {
            MinWidth = minWidth,
            MinHeight = 52,
            Margin = new Thickness(3),
            Padding = new Thickness(6),
            Background = FindBrush("MenuBackgroundBrush", Brushes.Black),
            BorderBrush = selected
                ? FindBrush("MenuHighlightTextBrush", Brushes.White)
                : FindBrush("MenuBorderBrush", Brushes.Gray),
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(5),
            Cursor = Cursors.Hand,
            Tag = selection,
            ToolTip = instanceId,
            Child = panel
        };
        card.MouseLeftButtonUp += LayoutVisualNode_OnMouseLeftButtonUp;
        card.AllowDrop = true;
        card.DragOver += LayoutContainerCard_OnDragOver;
        card.Drop += LayoutContainerCard_OnDrop;
        return card;
    }

    private FrameworkElement BuildSlotContent(
        LayoutContainerElement container,
        LayoutSlotKind slotKind,
        LayoutDropTarget target)
    {
        var slot = slotKind == LayoutSlotKind.Secondary
            ? container.SecondarySlot
            : container.PrimarySlot;
        return BuildSlotContent(slot, target);
    }

    private FrameworkElement BuildSlotContent(LayoutSlot slot, LayoutDropTarget target)
    {
        var panel = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0)
        };
        foreach (var child in slot.Children)
        {
            if (child is LayoutWidgetElement widget)
            {
                panel.Children.Add(CreateWidgetTile(widget, target));
            }
            else if (child is LayoutContainerElement nested)
            {
                foreach (var nestedWidget in nested.PrimarySlot.Children.OfType<LayoutWidgetElement>())
                {
                    panel.Children.Add(CreateWidgetTile(nestedWidget, target));
                }
            }
        }

        if (panel.Children.Count == 0)
        {
            panel.Children.Add(CreateEmptyHint("Settings.Layout.EditorDropHere"));
        }

        var dropHost = new Border
        {
            MinHeight = 30,
            Padding = new Thickness(3),
            Background = Brushes.Transparent,
            BorderBrush = FindBrush("MenuSeparatorBrush", Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            AllowDrop = true,
            Tag = target,
            Child = panel
        };
        dropHost.DragOver += LayoutDropTarget_OnDragOver;
        dropHost.Drop += LayoutDropTarget_OnDrop;
        return dropHost;
    }

    private FrameworkElement CreateWidgetTile(LayoutWidgetElement widget, LayoutDropTarget target)
    {
        var selected = _layoutEditorSelection?.InstanceId == widget.InstanceId;
        var tile = new Button
        {
            Content = GetWidgetTitle(widget),
            Tag = new LayoutEditorSelection(
                widget.InstanceId,
                LayoutEditorNodeKind.Widget,
                target.ContainerId,
                target.SlotKind,
                widget),
            Margin = new Thickness(2),
            Padding = new Thickness(7, 3, 7, 3),
            MinWidth = 44,
            MinHeight = 26,
            Opacity = widget.Enabled ? 1 : 0.45,
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected
                ? FindBrush("MenuHighlightTextBrush", Brushes.White)
                : FindBrush("MenuBorderBrush", Brushes.Gray),
            Cursor = Cursors.Hand
        };
        tile.PreviewMouseLeftButtonDown += LayoutDragSource_OnPreviewMouseLeftButtonDown;
        tile.PreviewMouseMove += LayoutWidgetTile_OnPreviewMouseMove;
        tile.Click += LayoutWidgetTile_OnClick;
        return tile;
    }

    private TextBlock CreateEmptyHint(string resourceKey)
    {
        return new TextBlock
        {
            Text = Loc.Get(resourceKey),
            Margin = new Thickness(6),
            Foreground = FindBrush("MenuSecondaryTextBrush", Brushes.Gray),
            FontSize = 10.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private void LayoutAddStaticContainerButton_OnClick(object sender, RoutedEventArgs e) =>
        AddInlineContainer(LayoutContainerKind.Static);

    private void LayoutAddHoverContainerButton_OnClick(object sender, RoutedEventArgs e) =>
        AddInlineContainer(LayoutContainerKind.HoverSwitch);

    private void AddInlineContainer(LayoutContainerKind kind)
    {
        TryApplyProfile(profile => LayoutEditorService.TryAddInlineContainer(
            profile,
            kind,
            out var updated,
            out _) ? updated : null);
    }

    private void LayoutAddEdgeContainerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (LayoutNewEdgeComboBox.SelectedItem is not ComboBoxItem { Tag: LayoutEdge edge })
        {
            return;
        }

        if (!TryApplyProfile(profile => LayoutEditorService.TryAddEdgeContainer(
                profile,
                edge,
                GetUnavailableTaskbarEdge(),
                out var updated,
                out _) ? updated : null))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorTaskbarEdgeUnavailable");
        }
    }

    private void LayoutPaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string paletteToken })
        {
            AddWidgetToTarget(paletteToken, ResolveAddTarget());
        }
    }

    private void LayoutDragSource_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _layoutDragStart = e.GetPosition(this);
    }

    private void LayoutPaletteButton_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button { Tag: string paletteToken } button || !ShouldBeginDrag(e))
        {
            return;
        }

        BeginVisualDrag(
            button,
            new DataObject(NewWidgetDragFormat, paletteToken),
            DragDropEffects.Copy);
    }

    private void LayoutWidgetTile_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button { Tag: LayoutEditorSelection selection } button || !ShouldBeginDrag(e))
        {
            return;
        }

        BeginVisualDrag(
            button,
            new DataObject(ExistingWidgetDragFormat, selection.InstanceId),
            DragDropEffects.Move);
    }

    private void LayoutContainerCard_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not TextBlock { Tag: LayoutEditorSelection selection } header ||
            selection.Kind == LayoutEditorNodeKind.Widget ||
            !ShouldBeginDrag(e))
        {
            return;
        }

        BeginVisualDrag(
            header,
            new DataObject(ExistingContainerDragFormat, selection.InstanceId),
            DragDropEffects.Move);
    }

    private void LayoutContainerCard_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(ExistingContainerDragFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        if (e.Effects != DragDropEffects.None)
        {
            e.Handled = true;
        }
    }

    private void LayoutContainerCard_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is Border { Tag: LayoutEditorSelection target } &&
            e.Data.GetData(ExistingContainerDragFormat) is string sourceId)
        {
            TryApplyProfile(profile => LayoutEditorService.TryReorderTopLevel(
                profile,
                sourceId,
                target.InstanceId,
                out var updated) ? updated : null);
            e.Handled = true;
        }
    }

    private bool ShouldBeginDrag(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return false;
        }

        var current = e.GetPosition(this);
        return Math.Abs(current.X - _layoutDragStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - _layoutDragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    /// <summary>
    /// 使用源控件的 VisualBrush 作为拖影，让用户看到实际组件外观而不是文本占位框。
    /// Uses the source control as a VisualBrush drag ghost so users see the real component instead of a text placeholder.
    /// </summary>
    private void BeginVisualDrag(UIElement source, DataObject data, DragDropEffects effects)
    {
        if (_layoutDragPreviewPopup is not null)
        {
            return;
        }

        var width = Math.Clamp(source.RenderSize.Width, 32, 180);
        var height = Math.Clamp(source.RenderSize.Height, 24, 96);
        var ghost = new Border
        {
            Width = width,
            Height = height,
            Padding = new Thickness(2),
            Background = new VisualBrush(source)
            {
                Stretch = Stretch.Uniform,
                Opacity = 0.9
            },
            BorderBrush = FindBrush("MenuHighlightTextBrush", Brushes.White),
            BorderThickness = new Thickness(1),
            Opacity = 0.88,
            IsHitTestVisible = false
        };
        var popup = new Popup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            PlacementTarget = this,
            Placement = PlacementMode.Relative,
            Child = ghost
        };
        _layoutDragPreviewPopup = popup;
        popup.IsOpen = true;

        void GiveFeedback(object? sender, GiveFeedbackEventArgs args)
        {
            var point = Mouse.GetPosition(this);
            popup.HorizontalOffset = point.X + 12;
            popup.VerticalOffset = point.Y + 12;
            args.UseDefaultCursors = true;
            args.Handled = true;
        }

        source.GiveFeedback += GiveFeedback;
        try
        {
            DragDrop.DoDragDrop(source, data, effects);
        }
        finally
        {
            source.GiveFeedback -= GiveFeedback;
            popup.IsOpen = false;
            popup.Child = null;
            _layoutDragPreviewPopup = null;
            if (_layoutPreviewDropOverlay is not null)
            {
                _layoutPreviewDropOverlay.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void LayoutDropTarget_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(NewWidgetDragFormat)
            ? DragDropEffects.Copy
            : e.Data.GetDataPresent(ExistingWidgetDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void LayoutDropTarget_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: LayoutDropTarget target })
        {
            return;
        }

        ApplyDrop(e, target);
        e.Handled = true;
    }

    private void LayoutVisualEditorHost_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(NewWidgetDragFormat)
            ? DragDropEffects.Copy
            : e.Data.GetDataPresent(ExistingWidgetDragFormat)
                ? DragDropEffects.Move
            : e.Data.GetDataPresent(ExistingContainerDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
        if (e.Effects != DragDropEffects.None && _layoutPreviewDropOverlay is not null)
        {
            _layoutPreviewDropOverlay.Visibility = Visibility.Visible;
        }
        else if (_layoutPreviewDropOverlay is not null)
        {
            _layoutPreviewDropOverlay.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }

    private void LayoutPreviewDropHost_OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(NewWidgetDragFormat) ||
            e.Data.GetDataPresent(ExistingWidgetDragFormat) ||
            e.Data.GetDataPresent(ExistingContainerDragFormat))
        {
            if (_layoutPreviewDropOverlay is not null)
            {
                _layoutPreviewDropOverlay.Visibility = Visibility.Visible;
            }
        }
    }

    private void LayoutPreviewDropHost_OnDragLeave(object sender, DragEventArgs e)
    {
        if (_layoutPreviewDropOverlay is not null)
        {
            _layoutPreviewDropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void LayoutVisualEditorHost_OnDrop(object sender, DragEventArgs e)
    {
        if (_layoutPreviewDropOverlay is not null)
        {
            _layoutPreviewDropOverlay.Visibility = Visibility.Collapsed;
        }
        if (e.Data.GetData(NewWidgetDragFormat) is string paletteToken)
        {
            AddWidgetToTarget(paletteToken, ResolveAddTarget());
        }
        else if (e.Data.GetData(ExistingContainerDragFormat) is string sourceId &&
            ResolveAddTarget() is { } target)
        {
            TryApplyProfile(profile => LayoutEditorService.TryReorderTopLevel(
                profile,
                sourceId,
                target.ContainerId,
                out var updated) ? updated : null);
        }
        else if (e.Data.GetData(ExistingWidgetDragFormat) is string widgetId &&
            ResolveAddTarget() is { } widgetTarget)
        {
            ApplyDrop(e, widgetTarget);
        }
        e.Handled = true;
    }

    private void ApplyDrop(DragEventArgs e, LayoutDropTarget target)
    {
        if (e.Data.GetData(NewWidgetDragFormat) is string paletteToken)
        {
            AddWidgetToTarget(paletteToken, target);
            return;
        }

        if (e.Data.GetData(ExistingWidgetDragFormat) is string instanceId &&
            !TryApplyProfile(profile => LayoutEditorService.TryRelocateWidget(
                profile,
                instanceId,
                target.ContainerId,
                target.SlotKind,
                out var updated,
                out _) ? updated : null))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorAddFailed");
        }
    }

    private void AddWidgetToTarget(string paletteToken, LayoutDropTarget? target)
    {
        var parts = paletteToken.Split('|', 2);
        var typeId = parts[0];
        var settings = ComponentCatalog.CreateDefaultSettings(typeId);
        if (parts.Length == 2 &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var option))
        {
            settings = typeId switch
            {
                BuiltInWidgetTypeIds.Command when Enum.IsDefined(typeof(MediaCommandKind), option) =>
                    new CommandWidgetSettings((MediaCommandKind)option, 36),
                BuiltInWidgetTypeIds.MediaText when Enum.IsDefined(typeof(MediaTextKind), option) =>
                    new MediaTextWidgetSettings(
                        (MediaTextKind)option,
                        true,
                        option == (int)MediaTextKind.Artist ? 11 : 14,
                        1),
                _ => settings
            };
        }

        var widget = new LayoutWidgetElement(
            $"widget-{Guid.NewGuid():N}",
            true,
            LayoutGeometry.Auto,
            typeId,
            settings);
        if (!TryApplyProfile(profile =>
        {
            var working = profile;
            var destination = target;
            if (destination is null)
            {
                if (!LayoutEditorService.TryAddInlineContainer(
                        profile,
                        LayoutContainerKind.Static,
                        out working,
                        out _))
                {
                    return null;
                }

                destination = new LayoutDropTarget(
                    working.InlineContainers[^1].InstanceId,
                    LayoutSlotKind.Primary);
            }

            return LayoutEditorService.TryAddWidget(
                working,
                destination.ContainerId,
                destination.SlotKind,
                widget,
                out var updated,
                out _) ? updated : null;
        }))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorAddFailed");
        }
    }

    private void LayoutVisualNode_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: LayoutEditorSelection selection })
        {
            SelectLayoutNode(selection);
            e.Handled = true;
        }
    }

    private void LayoutWidgetTile_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LayoutEditorSelection selection })
        {
            SelectLayoutNode(selection);
            e.Handled = true;
        }
    }

    private void SelectLayoutNode(LayoutEditorSelection selection)
    {
        _layoutEditorSelection = selection;
        RefreshLayoutEditor();
    }

    private void LayoutSlotComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_layoutEditorSyncing ||
            _layoutEditorSelection is not { Kind: LayoutEditorNodeKind.InlineContainer } selection ||
            LayoutSlotComboBox.SelectedItem is not ComboBoxItem { Tag: LayoutSlotKind slotKind })
        {
            return;
        }

        _layoutEditorSelection = selection with { SlotKind = slotKind };
        RefreshLayoutEditor();
    }

    private void LayoutRemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_layoutEditorSelection is null)
        {
            return;
        }

        var id = _layoutEditorSelection.InstanceId;
        if (TryApplyProfile(profile => LayoutEditorService.TryRemove(profile, id, out var updated)
                ? updated
                : null))
        {
            _layoutEditorSelection = null;
        }
    }

    private void LayoutMoveUpButton_OnClick(object sender, RoutedEventArgs e) => TryMoveSelected(-1);

    private void LayoutMoveDownButton_OnClick(object sender, RoutedEventArgs e) => TryMoveSelected(1);

    private void TryMoveSelected(int offset)
    {
        if (_layoutEditorSelection is not { } selection)
        {
            return;
        }

        TryApplyProfile(profile => LayoutEditorService.TryMove(
            profile,
            selection.InstanceId,
            offset,
            out var updated) ? updated : null);
    }

    private void LayoutToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_layoutEditorSelection is not { } selection)
        {
            return;
        }

        var enabled = selection.Model switch
        {
            LayoutElement element => element.Enabled,
            LayoutEdgeContainer edge => edge.Enabled,
            _ => true
        };
        TryApplyProfile(profile => LayoutEditorService.TrySetEnabled(
            profile,
            selection.InstanceId,
            !enabled,
            out var updated) ? updated : null);
    }

    private void LayoutUndoButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_layoutEditHistory.TryUndo(_layoutEditorProfileKey, out var profile))
        {
            return;
        }

        var current = _coordinator.Current.Layout;
        // 长度和厚度是窗口级设置；撤销组件拼贴时保留当前比例，避免旧快照让滑块与实际布局不一致。
        // Length and thickness are window-level settings; preserve them while undoing composition so snapshots cannot desynchronize the sliders.
        profile = profile with
        {
            Surface = current.Get(_layoutEditorProfileKey).Surface
        };
        var document = current.WithProfile(profile);
        TryUpdate(() => _coordinator.UpdateLayout(document));
    }

    private void LayoutResetProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var current = _coordinator.Current;
        var profile = current.Layout.Get(_layoutEditorProfileKey);
        var defaults = LayoutMigrationService.CreateFromLegacy(current.Window, current.Metrics)
            .Get(_layoutEditorProfileKey);
        if (profile == defaults)
        {
            return;
        }

        _layoutEditHistory.Record(profile);
        TryUpdate(() => _coordinator.UpdateLayout(current.Layout.WithProfile(defaults)));
        _layoutEditorSelection = null;
    }

    private bool TryApplyProfile(Func<LayoutProfile, LayoutProfile?> edit)
    {
        var current = _coordinator.Current.Layout;
        var profile = current.Get(_layoutEditorProfileKey);
        var updated = edit(profile);
        if (updated is null || updated == profile)
        {
            return false;
        }

        _layoutEditHistory.Record(profile);
        TryUpdate(() => _coordinator.UpdateLayout(current.WithProfile(updated)));
        return true;
    }

    private void RefreshSlotOptions()
    {
        _layoutEditorSyncing = true;
        try
        {
            LayoutSlotComboBox.Items.Clear();
            if (_layoutEditorSelection is not { } selection)
            {
                LayoutSlotComboBox.IsEnabled = false;
                return;
            }

            if (selection.Model is LayoutContainerElement { ContainerKind: LayoutContainerKind.HoverSwitch })
            {
                AddComboOption(LayoutSlotComboBox, LayoutSlotKind.Primary, "Settings.Layout.EditorLeaveContent");
                AddComboOption(LayoutSlotComboBox, LayoutSlotKind.Secondary, "Settings.Layout.EditorNearContent");
                LayoutSlotComboBox.SelectedIndex = selection.SlotKind == LayoutSlotKind.Secondary ? 1 : 0;
                LayoutSlotComboBox.IsEnabled = true;
                return;
            }

            if (selection.Kind == LayoutEditorNodeKind.EdgeContainer)
            {
                AddComboOption(LayoutSlotComboBox, LayoutSlotKind.Expanded, "Settings.Layout.EditorExpandedContent");
                LayoutSlotComboBox.SelectedIndex = 0;
                LayoutSlotComboBox.IsEnabled = false;
                return;
            }

            AddComboOption(LayoutSlotComboBox, selection.SlotKind, GetSlotResourceKey(selection.SlotKind));
            LayoutSlotComboBox.SelectedIndex = 0;
            LayoutSlotComboBox.IsEnabled = false;
        }
        finally
        {
            _layoutEditorSyncing = false;
        }
    }

    private void RefreshSelectionText()
    {
        LayoutEditorSelectionText.Text = _layoutEditorSelection?.Model switch
        {
            LayoutWidgetElement widget => GetWidgetTitle(widget),
            LayoutContainerElement { ContainerKind: LayoutContainerKind.HoverSwitch } =>
                Loc.Get("Settings.Layout.ContainerHoverSwitch"),
            LayoutContainerElement => Loc.Get("Settings.Layout.ContainerStatic"),
            LayoutEdgeContainer edge => $"{Loc.Get("Settings.Layout.ContainerAutoCollapse")} · {GetEdgeName(edge.Edge)}",
            _ => Loc.Get("Settings.Layout.EditorNoSelection")
        };
    }

    private void RefreshLayoutProperties()
    {
        if (LayoutPropertyHost is null)
        {
            return;
        }

        _layoutPropertySyncing = true;
        try
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = Loc.Get("Settings.Layout.EditorPrimaryProperties"),
                FontWeight = FontWeights.SemiBold
            });
            switch (_layoutEditorSelection?.Model)
            {
                case LayoutWidgetElement widget:
                    AddWidgetProperties(panel, widget);
                    AddAdvancedGeometryProperties(panel, widget);
                    break;
                case LayoutContainerElement container:
                    AddInlineContainerProperties(panel, container);
                    break;
                case LayoutEdgeContainer edge:
                    AddEdgeContainerProperties(panel, edge);
                    break;
                default:
                    panel.Children.Add(CreateEmptyHint("Settings.Layout.EditorNoSelection"));
                    break;
            }

            LayoutPropertyHost.Child = panel;
        }
        finally
        {
            _layoutPropertySyncing = false;
        }
    }

    private void AddWidgetProperties(StackPanel panel, LayoutWidgetElement widget)
    {
        var resetButton = new Button
        {
            Content = Loc.Get("Settings.Layout.PropertyResetDefault"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
            Style = TryFindResource("SettingsActionButtonStyle") as Style,
            ToolTip = Loc.Get("Settings.Layout.PropertyResetDefaultHint")
        };
        resetButton.Click += (_, _) => ResetWidgetProperties(widget);
        panel.Children.Add(resetButton);

        switch (widget.Settings)
        {
            case ArtworkWidgetSettings artwork:
                AddCheckRow(panel, "Settings.Layout.PropertyArtworkOpenSource", artwork.OpenSourceOnClick,
                    value => UpdateWidget(widget, current => ((ArtworkWidgetSettings)current) with { OpenSourceOnClick = value }));
                AddSliderRow(panel, "Settings.Layout.PropertyArtworkRadius", artwork.CornerRadiusDip, 0, 32,
                    value => UpdateWidget(widget, current => ((ArtworkWidgetSettings)current) with { CornerRadiusDip = value }));
                AddCheckRow(panel, "Settings.Layout.PropertyArtworkColor", artwork.UseMediaPrimaryColor,
                    value => UpdateWidget(widget, current => ((ArtworkWidgetSettings)current) with { UseMediaPrimaryColor = value }));
                break;
            case MediaTextWidgetSettings text:
                if (widget.TypeId == BuiltInWidgetTypeIds.MediaText)
                {
                    AddEnumRow(panel, "Settings.Layout.PropertyTextKind", text.TextKind,
                        new Dictionary<MediaTextKind, string>
                        {
                            [MediaTextKind.Title] = "Settings.Layout.PropertyTextTitle",
                            [MediaTextKind.Artist] = "Settings.Layout.PropertyTextArtist",
                            [MediaTextKind.Source] = "Settings.Layout.PropertyTextSource",
                            [MediaTextKind.TitleAndArtist] = "Settings.Layout.PropertyTextTitleAndArtist"
                        },
                        value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { TextKind = value }));
                }
                AddSliderRow(panel, "Settings.Layout.PropertyFontSize", text.FontSizeDip, 6, 72,
                    value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { FontSizeDip = value }));
                var advancedText = new StackPanel();
                if (text.TextKind != MediaTextKind.TitleAndArtist)
                {
                    advancedText.Children.Add(CreateEmptyHint("Settings.Layout.PropertyMaxLinesHint"));
                    AddSliderRow(advancedText, "Settings.Layout.PropertyMaxLines", text.MaxLines, 1, 2,
                        value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { MaxLines = value }));
                    AddCheckRow(advancedText, "Settings.Layout.PropertyMarquee", text.EnableMarquee,
                        value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { EnableMarquee = value }));
                }
                if (advancedText.Children.Count > 0)
                {
                    panel.Children.Add(new Expander
                    {
                        Header = Loc.Get("Settings.Layout.EditorAdvancedText"),
                        Margin = new Thickness(0, 8, 0, 0),
                        IsExpanded = false,
                        Content = advancedText
                    });
                }
                break;
            case CommandWidgetSettings command:
                AddEnumRow(panel, "Settings.Layout.PropertyCommand", command.Command,
                    Enum.GetValues<MediaCommandKind>().ToDictionary(value => value, GetCommandOptionKey),
                    value => UpdateWidget(widget, current => ((CommandWidgetSettings)current) with { Command = value }));
                AddSliderRow(panel, "Settings.Layout.PropertyButtonSize", command.ButtonSizeDip, 20, 96,
                    value => UpdateWidget(widget, current => ((CommandWidgetSettings)current) with { ButtonSizeDip = value }));
                break;
            case MetricsWidgetSettings metrics:
                AddEnumRow(panel, "Settings.Layout.PropertyMetric", metrics.Metric,
                    Enum.GetValues<MetricKind>().ToDictionary(value => value, GetMetricOptionKey),
                    value => UpdateWidget(widget, current => ((MetricsWidgetSettings)current) with
                    {
                        Metric = value,
                        CycleMetrics = [value]
                    }));
                AddSliderRow(panel, "Settings.Layout.PropertyRefresh", metrics.RefreshIntervalMilliseconds, 250, 30_000,
                    value => UpdateWidget(widget, current => ((MetricsWidgetSettings)current) with { RefreshIntervalMilliseconds = value }),
                    value => Loc.Get("Settings.Layout.UnitMilliseconds", value));
                AddCheckRow(panel, "Settings.Layout.PropertyOpenTaskManager", metrics.OpenTaskManagerOnClick,
                    value => UpdateWidget(widget, current => ((MetricsWidgetSettings)current) with { OpenTaskManagerOnClick = value }));
                break;
            case SpectrumWidgetSettings spectrum:
                AddSliderRow(panel, "Settings.Layout.PropertyBandCount", spectrum.BandCount, 1, AudioMonitorService.BandCount,
                    value => UpdateWidget(widget, current => ((SpectrumWidgetSettings)current) with { BandCount = value }));
                AddSliderRow(panel, "Settings.Layout.PropertyRefreshRate", spectrum.RefreshRateHz, 5, 30,
                    value => UpdateWidget(widget, current => ((SpectrumWidgetSettings)current) with { RefreshRateHz = value }),
                    value => Loc.Get("Settings.Layout.UnitHertz", value));
                AddSliderRow(panel, "Settings.Layout.PropertySensitivity", spectrum.SensitivityPercent, 1, 400,
                    value => UpdateWidget(widget, current => ((SpectrumWidgetSettings)current) with { SensitivityPercent = value }),
                    value => Loc.Get("Settings.Layout.UnitPercent", value));
                break;
            case SeparatorWidgetSettings separator:
                AddSliderRow(panel, "Settings.Layout.PropertyThickness", separator.ThicknessDip, 1, 8,
                    value => UpdateWidget(widget, current => ((SeparatorWidgetSettings)current) with { ThicknessDip = value }));
                AddSliderRow(panel, "Settings.Layout.PropertyLength", separator.LengthDip, 4, 256,
                    value => UpdateWidget(widget, current => ((SeparatorWidgetSettings)current) with { LengthDip = value }));
                break;
        }
    }

    private void AddAdvancedGeometryProperties(StackPanel panel, LayoutWidgetElement widget)
    {
        var geometry = widget.Geometry ?? LayoutGeometry.Auto;
        var content = new StackPanel();
        AddNullableNumericRow(content, "Settings.Layout.PropertyWidth", geometry.WidthDip, 1, 2_000,
            value => UpdateGeometry(widget, current => current with { WidthDip = value }));
        AddNullableNumericRow(content, "Settings.Layout.PropertyHeight", geometry.HeightDip, 1, 2_000,
            value => UpdateGeometry(widget, current => current with { HeightDip = value }));
        panel.Children.Add(new Expander
        {
            Header = Loc.Get("Settings.Layout.EditorAdvancedSize"),
            Margin = new Thickness(0, 8, 0, 0),
            IsExpanded = false,
            Content = content
        });
    }

    private void AddInlineContainerProperties(StackPanel panel, LayoutContainerElement container)
    {
        var resetButton = new Button
        {
            Content = Loc.Get("Settings.Layout.PropertyResetContainerDefault"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
            Style = TryFindResource("SettingsActionButtonStyle") as Style,
            ToolTip = Loc.Get("Settings.Layout.PropertyResetContainerDefaultHint")
        };
        resetButton.Click += (_, _) => ResetInlineContainerProperties(container);
        panel.Children.Add(resetButton);

        AddEnumRow(
            panel,
            "Settings.Layout.PropertyAlignment",
            container.ContentAlignment,
            new Dictionary<LayoutContentAlignment, string>
            {
                [LayoutContentAlignment.Center] = "Settings.Layout.PropertyAlignmentCenter",
                [LayoutContentAlignment.Start] = "Settings.Layout.PropertyAlignmentStart",
                [LayoutContentAlignment.End] = "Settings.Layout.PropertyAlignmentEnd",
                [LayoutContentAlignment.Stretch] = "Settings.Layout.PropertyAlignmentStretch"
            },
            value => UpdateInlineContainer(
                container,
                container.ProximityDip,
                value,
                container.SecondaryContentAlignment,
                container.Animation));

        if (container.ContainerKind != LayoutContainerKind.HoverSwitch)
        {
            panel.Children.Add(CreateEmptyHint("Settings.Layout.EditorStaticFollowsProfile"));
            AddAdvancedContainerGeometryProperties(panel, container);
            return;
        }

        AddEnumRow(
            panel,
            "Settings.Layout.PropertyNearAlignment",
            container.SecondaryContentAlignment,
            new Dictionary<LayoutContentAlignment, string>
            {
                [LayoutContentAlignment.Center] = "Settings.Layout.PropertyAlignmentCenter",
                [LayoutContentAlignment.Start] = "Settings.Layout.PropertyAlignmentStart",
                [LayoutContentAlignment.End] = "Settings.Layout.PropertyAlignmentEnd",
                [LayoutContentAlignment.Stretch] = "Settings.Layout.PropertyAlignmentStretch"
            },
            value => UpdateInlineContainer(
                container,
                container.ProximityDip,
                container.ContentAlignment,
                value,
                container.Animation));

        var advanced = new StackPanel();
        AddCheckRow(advanced, "Settings.Layout.PropertyAnimation", container.Animation.Enabled,
            value => UpdateInlineContainer(
                container,
                container.ProximityDip,
                container.ContentAlignment,
                container.SecondaryContentAlignment,
                container.Animation with { Enabled = value }));
        AddSliderRow(advanced, "Settings.Layout.PropertyDuration", container.Animation.DurationMilliseconds, 0, 2_000,
            value => UpdateInlineContainer(
                container,
                container.ProximityDip,
                container.ContentAlignment,
                container.SecondaryContentAlignment,
                container.Animation with { DurationMilliseconds = value }),
            value => Loc.Get("Settings.Layout.UnitMilliseconds", value));
        AddSliderRow(advanced, "Settings.Layout.PropertyDelay", container.Animation.DelayMilliseconds, 0, 2_000,
            value => UpdateInlineContainer(
                container,
                container.ProximityDip,
                container.ContentAlignment,
                container.SecondaryContentAlignment,
                container.Animation with { DelayMilliseconds = value }),
            value => Loc.Get("Settings.Layout.UnitMilliseconds", value));
        AddSliderRow(advanced, "Settings.Layout.PropertyProximity", container.ProximityDip, 0, 256,
            value => UpdateInlineContainer(
                container,
                value,
                container.ContentAlignment,
                container.SecondaryContentAlignment,
                container.Animation),
            value => Loc.Get("Settings.Layout.UnitDip", value));
        AddEnumRow(
            advanced,
            "Settings.Layout.PropertyEasing",
            container.Animation.Easing,
            new Dictionary<LayoutEasingKind, string>
            {
                [LayoutEasingKind.Linear] = "Settings.Layout.PropertyEasingLinear",
                [LayoutEasingKind.EaseOut] = "Settings.Layout.PropertyEasingEaseOut",
                [LayoutEasingKind.EaseInOut] = "Settings.Layout.PropertyEasingEaseInOut"
            },
            value => UpdateInlineContainer(
                container,
                container.ProximityDip,
                container.ContentAlignment,
                container.SecondaryContentAlignment,
                container.Animation with { Easing = value }));
        panel.Children.Add(new Expander
        {
            Header = Loc.Get("Settings.Layout.EditorAdvancedBehavior"),
            Margin = new Thickness(0, 8, 0, 0),
            IsExpanded = false,
            Content = advanced
        });
        AddAdvancedContainerGeometryProperties(panel, container);
    }

    private void AddEdgeContainerProperties(StackPanel panel, LayoutEdgeContainer edge)
    {
        var resetButton = new Button
        {
            Content = Loc.Get("Settings.Layout.PropertyResetContainerDefault"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
            Style = TryFindResource("SettingsActionButtonStyle") as Style,
            ToolTip = Loc.Get("Settings.Layout.PropertyResetContainerDefaultHint")
        };
        resetButton.Click += (_, _) => ResetEdgeContainerProperties(edge);
        panel.Children.Add(resetButton);

        AddEnumRow(panel, "Settings.Layout.PropertyEdge", edge.Edge,
            Enum.GetValues<LayoutEdge>().ToDictionary(value => value, GetEdgeResourceKey),
            value => UpdateEdgeContainer(edge, value, edge.OffsetDip, edge.TriggerThicknessDip, edge.ProximityDip, edge.Animation));
        AddSliderRow(panel, "Settings.Layout.PropertyEdgeOffset", edge.OffsetDip, -500, 500,
            value => UpdateEdgeContainer(edge, edge.Edge, value, edge.TriggerThicknessDip, edge.ProximityDip, edge.Animation));
        AddSliderRow(panel, "Settings.Layout.PropertyTriggerThickness", edge.TriggerThicknessDip, 2, 24,
            value => UpdateEdgeContainer(edge, edge.Edge, edge.OffsetDip, value, edge.ProximityDip, edge.Animation));
        var advanced = new StackPanel();
        AddCheckRow(advanced, "Settings.Layout.PropertyAnimation", edge.Animation.Enabled,
            value => UpdateEdgeContainer(
                edge,
                edge.Edge,
                edge.OffsetDip,
                edge.TriggerThicknessDip,
                edge.ProximityDip,
                edge.Animation with { Enabled = value }));
        AddSliderRow(advanced, "Settings.Layout.PropertyDuration", edge.Animation.DurationMilliseconds, 0, 2_000,
            value => UpdateEdgeContainer(
                edge,
                edge.Edge,
                edge.OffsetDip,
                edge.TriggerThicknessDip,
                edge.ProximityDip,
                edge.Animation with { DurationMilliseconds = value }),
            value => Loc.Get("Settings.Layout.UnitMilliseconds", value));
        AddSliderRow(advanced, "Settings.Layout.PropertyDelay", edge.Animation.DelayMilliseconds, 0, 2_000,
            value => UpdateEdgeContainer(
                edge,
                edge.Edge,
                edge.OffsetDip,
                edge.TriggerThicknessDip,
                edge.ProximityDip,
                edge.Animation with { DelayMilliseconds = value }),
            value => Loc.Get("Settings.Layout.UnitMilliseconds", value));
        AddEnumRow(
            advanced,
            "Settings.Layout.PropertyEasing",
            edge.Animation.Easing,
            new Dictionary<LayoutEasingKind, string>
            {
                [LayoutEasingKind.Linear] = "Settings.Layout.PropertyEasingLinear",
                [LayoutEasingKind.EaseOut] = "Settings.Layout.PropertyEasingEaseOut",
                [LayoutEasingKind.EaseInOut] = "Settings.Layout.PropertyEasingEaseInOut"
            },
            value => UpdateEdgeContainer(
                edge,
                edge.Edge,
                edge.OffsetDip,
                edge.TriggerThicknessDip,
                edge.ProximityDip,
                edge.Animation with { Easing = value }));
        panel.Children.Add(new Expander
        {
            Header = Loc.Get("Settings.Layout.EditorAdvancedBehavior"),
            Margin = new Thickness(0, 8, 0, 0),
            IsExpanded = false,
            Content = advanced
        });
    }

    private void AddAdvancedContainerGeometryProperties(StackPanel panel, LayoutElement element)
    {
        // 容器尺寸属于高级覆盖项；默认保持自动测量，避免普通用户被无效固定值干扰。
        // Container dimensions remain advanced overrides; automatic measurement keeps the common path predictable.
        var geometry = element.Geometry ?? LayoutGeometry.Auto;
        var content = new StackPanel();
        AddNullableNumericRow(content, "Settings.Layout.PropertyWidth", geometry.WidthDip, 1, 2_000,
            value => UpdateGeometry(element, current => current with { WidthDip = value }));
        AddNullableNumericRow(content, "Settings.Layout.PropertyHeight", geometry.HeightDip, 1, 2_000,
            value => UpdateGeometry(element, current => current with { HeightDip = value }));
        panel.Children.Add(new Expander
        {
            Header = Loc.Get("Settings.Layout.EditorAdvancedSize"),
            Margin = new Thickness(0, 8, 0, 0),
            IsExpanded = false,
            Content = content
        });
    }

    private void UpdateWidget(LayoutWidgetElement widget, Func<WidgetSettings, WidgetSettings> update)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        TryApplyProfile(profile =>
        {
            var current = LayoutEditorService.Find(profile, widget.InstanceId) as LayoutWidgetElement;
            return current is not null && LayoutEditorService.TryUpdateWidgetSettings(
                profile,
                widget.InstanceId,
                update(current.Settings),
                out var updated) ? updated : null;
        });
    }

    private void UpdateGeometry(LayoutElement element, Func<LayoutGeometry, LayoutGeometry> update)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        TryApplyProfile(profile => LayoutEditorService.TryUpdateGeometry(
            profile,
            element.InstanceId,
            update(element.Geometry ?? LayoutGeometry.Auto),
            out var updated) ? updated : null);
    }

    private void UpdateInlineContainer(
        LayoutContainerElement container,
        int proximityDip,
        LayoutContentAlignment contentAlignment,
        LayoutContentAlignment secondaryContentAlignment,
        LayoutAnimationSettings animation)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        TryApplyProfile(profile => LayoutEditorService.TryUpdateInlineContainer(
            profile,
            container.InstanceId,
            proximityDip,
            contentAlignment,
            secondaryContentAlignment,
            animation,
            out var updated) ? updated : null);
    }

    private void ResetInlineContainerProperties(LayoutContainerElement container)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        TryApplyProfile(profile => LayoutEditorService.TryResetInlineContainer(
            profile,
            container.InstanceId,
            out var updated) ? updated : null);
    }

    private void ResetEdgeContainerProperties(LayoutEdgeContainer container)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        TryApplyProfile(profile => LayoutEditorService.TryResetEdgeContainer(
            profile,
            container.InstanceId,
            out var updated) ? updated : null);
    }

    private void ResetWidgetProperties(LayoutWidgetElement widget)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        TryApplyProfile(profile => LayoutEditorService.TryResetWidgetProperties(
            profile,
            widget.InstanceId,
            out var updated) ? updated : null);
    }

    private void UpdateEdgeContainer(
        LayoutEdgeContainer container,
        LayoutEdge edge,
        int offsetDip,
        int triggerThicknessDip,
        int proximityDip,
        LayoutAnimationSettings animation)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        if (!TryApplyProfile(profile => LayoutEditorService.TryUpdateEdgeContainer(
                profile,
                container.InstanceId,
                edge,
                GetUnavailableTaskbarEdge(),
                offsetDip,
                triggerThicknessDip,
                proximityDip,
                animation,
                out var updated,
                out _) ? updated : null))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorTaskbarEdgeUnavailable");
        }
    }

    private void AddNullableNumericRow(
        Panel panel,
        string labelKey,
        int? value,
        int minimum,
        int maximum,
        Action<int?> update)
    {
        var row = CreatePropertyRow(labelKey);
        var input = new TextBox
        {
            Width = 86,
            Text = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ToolTip = Loc.Get("Settings.Layout.PropertyAuto")
        };
        void Commit()
        {
            var text = input.Text.Trim();
            if (text.Length == 0)
            {
                update(null);
                return;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                parsed = Math.Clamp(parsed, minimum, maximum);
                input.Text = parsed.ToString(CultureInfo.InvariantCulture);
                update(parsed);
            }
            else
            {
                input.Text = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        input.LostFocus += (_, _) => Commit();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        row.Children.Add(input);
        Grid.SetColumn(input, 1);
        panel.Children.Add(row);
    }

    private void AddSliderRow(
        Panel panel,
        string labelKey,
        int value,
        int minimum,
        int maximum,
        Action<int> update,
        Func<int, string>? format = null)
    {
        var row = CreatePropertyRow(labelKey);
        var controlGroup = new Grid();
        controlGroup.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        controlGroup.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = Math.Max(1, (maximum - minimum) / 10),
            Value = Math.Clamp(value, minimum, maximum),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var input = new TextBox
        {
            Width = 82,
            Margin = new Thickness(6, 0, 0, 0),
            Text = (format ?? (current => current.ToString(CultureInfo.InvariantCulture)))(value)
        };
        slider.ValueChanged += (_, _) =>
        {
            input.Text = (format ?? (current => current.ToString(CultureInfo.InvariantCulture)))(
                Math.Clamp((int)Math.Round(slider.Value), minimum, maximum));
        };
        void CommitSlider() => update(Math.Clamp((int)Math.Round(slider.Value), minimum, maximum));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => CommitSlider()));
        slider.KeyUp += (_, _) => CommitSlider();
        void CommitInput()
        {
            if (!TryParseNumericInput(input.Text, out var parsed))
            {
                input.Text = (format ?? (current => current.ToString(CultureInfo.InvariantCulture)))(value);
                return;
            }

            parsed = Math.Clamp(parsed, minimum, maximum);
            slider.Value = parsed;
            input.Text = (format ?? (current => current.ToString(CultureInfo.InvariantCulture)))(parsed);
            update(parsed);
        }

        input.LostFocus += (_, _) => CommitInput();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitInput();
                e.Handled = true;
            }
        };
        controlGroup.Children.Add(slider);
        controlGroup.Children.Add(input);
        Grid.SetColumn(input, 1);
        row.Children.Add(controlGroup);
        Grid.SetColumn(controlGroup, 1);
        panel.Children.Add(row);
    }

    private void AddCheckRow(Panel panel, string labelKey, bool value, Action<bool> update)
    {
        var check = new CheckBox
        {
            Content = Loc.Get(labelKey),
            IsChecked = value,
            Margin = new Thickness(0, 3, 0, 3),
            Style = TryFindResource("SettingsCheckBoxStyle") as Style
        };
        check.Checked += (_, _) => update(true);
        check.Unchecked += (_, _) => update(false);
        panel.Children.Add(check);
    }

    private void AddEnumRow<TEnum>(
        Panel panel,
        string labelKey,
        TEnum value,
        IReadOnlyDictionary<TEnum, string> labels,
        Action<TEnum> update)
        where TEnum : struct, Enum
    {
        var row = CreatePropertyRow(labelKey);
        var combo = new ComboBox
        {
            MinWidth = 160,
            Style = TryFindResource("SettingsComboBoxStyle") as Style
        };
        var selectedIndex = 0;
        foreach (var pair in labels)
        {
            var item = new ComboBoxItem
            {
                Content = Loc.Get(pair.Value),
                Tag = pair.Key,
                IsEnabled = typeof(TEnum) != typeof(LayoutEdge) ||
                    GetUnavailableTaskbarEdge() is not { } unavailable ||
                    !Equals(pair.Key, unavailable)
            };
            if (EqualityComparer<TEnum>.Default.Equals(pair.Key, value))
            {
                selectedIndex = combo.Items.Count;
            }
            combo.Items.Add(item);
        }

        combo.SelectedIndex = selectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (!_layoutPropertySyncing && combo.SelectedItem is ComboBoxItem { Tag: TEnum selected })
            {
                update(selected);
            }
        };
        row.Children.Add(combo);
        Grid.SetColumn(combo, 1);
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(row);
    }

    private Grid CreatePropertyRow(string labelKey)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 3, 0, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = Loc.Get(labelKey),
            VerticalAlignment = VerticalAlignment.Center,
            Style = TryFindResource("SettingsRowDescriptionStyle") as Style
        });
        return row;
    }

    private static bool TryParseNumericInput(string text, out int value)
    {
        var token = text.Trim();
        var separator = token.IndexOf(' ');
        if (separator >= 0)
        {
            token = token[..separator];
        }
        token = token.TrimEnd('%');
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private LayoutDropTarget? ResolveAddTarget()
    {
        if (_layoutEditorSelection is { } selection)
        {
            if (selection.Kind == LayoutEditorNodeKind.EdgeContainer)
            {
                return new LayoutDropTarget(selection.InstanceId, LayoutSlotKind.Expanded);
            }
            if (selection.Kind == LayoutEditorNodeKind.InlineContainer)
            {
                return new LayoutDropTarget(selection.InstanceId, selection.SlotKind);
            }
            if (selection.ParentContainerId is not null)
            {
                return new LayoutDropTarget(selection.ParentContainerId, selection.SlotKind);
            }
        }

        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        return profile.InlineContainers.FirstOrDefault() is { } first
            ? new LayoutDropTarget(first.InstanceId, LayoutSlotKind.Primary)
            : null;
    }

    private LayoutSlotKind ResolveVisibleSlot(LayoutContainerElement container)
    {
        if (container.ContainerKind != LayoutContainerKind.HoverSwitch)
        {
            return LayoutSlotKind.Primary;
        }

        if (_layoutEditorSelection is { } selection &&
            (selection.InstanceId == container.InstanceId || selection.ParentContainerId == container.InstanceId))
        {
            return selection.SlotKind == LayoutSlotKind.Secondary
                ? LayoutSlotKind.Secondary
                : LayoutSlotKind.Primary;
        }

        return LayoutSlotKind.Primary;
    }

    private LayoutEditorSelection? ResolveSelection(LayoutProfile profile, string instanceId)
    {
        foreach (var container in profile.InlineContainers)
        {
            if (container.InstanceId == instanceId)
            {
                return new LayoutEditorSelection(
                    instanceId,
                    LayoutEditorNodeKind.InlineContainer,
                    null,
                    _layoutEditorSelection?.SlotKind == LayoutSlotKind.Secondary
                        ? LayoutSlotKind.Secondary
                        : LayoutSlotKind.Primary,
                    container);
            }
            if (ResolveSlotSelection(container.PrimarySlot, container.InstanceId, LayoutSlotKind.Primary, instanceId) is { } primary)
            {
                return primary;
            }
            if (ResolveSlotSelection(container.SecondarySlot, container.InstanceId, LayoutSlotKind.Secondary, instanceId) is { } secondary)
            {
                return secondary;
            }
        }

        foreach (var edge in profile.EdgeContainers)
        {
            if (edge.InstanceId == instanceId)
            {
                return new LayoutEditorSelection(
                    instanceId,
                    LayoutEditorNodeKind.EdgeContainer,
                    null,
                    LayoutSlotKind.Expanded,
                    edge);
            }
            if (ResolveSlotSelection(edge.ExpandedSlot, edge.InstanceId, LayoutSlotKind.Expanded, instanceId) is { } widget)
            {
                return widget;
            }
        }

        return null;
    }

    private static LayoutEditorSelection? ResolveSlotSelection(
        LayoutSlot slot,
        string parentId,
        LayoutSlotKind slotKind,
        string instanceId)
    {
        foreach (var child in slot.Children)
        {
            if (child.InstanceId == instanceId)
            {
                return child switch
                {
                    LayoutWidgetElement widget => new LayoutEditorSelection(
                        instanceId,
                        LayoutEditorNodeKind.Widget,
                        parentId,
                        slotKind,
                        widget),
                    LayoutContainerElement container => new LayoutEditorSelection(
                        instanceId,
                        LayoutEditorNodeKind.InlineContainer,
                        parentId,
                        slotKind,
                        container),
                    _ => null
                };
            }

            if (child is LayoutContainerElement nested)
            {
                if (ResolveSlotSelection(nested.PrimarySlot, nested.InstanceId, LayoutSlotKind.Primary, instanceId) is { } primary)
                {
                    return primary;
                }
                if (ResolveSlotSelection(nested.SecondarySlot, nested.InstanceId, LayoutSlotKind.Secondary, instanceId) is { } secondary)
                {
                    return secondary;
                }
            }
        }

        return null;
    }

    private void UpdateLayoutEditorButtons()
    {
        var hasSelection = _layoutEditorSelection is not null;
        LayoutMoveUpButton.IsEnabled = hasSelection;
        LayoutMoveDownButton.IsEnabled = hasSelection;
        LayoutToggleButton.IsEnabled = hasSelection;
        LayoutRemoveButton.IsEnabled = hasSelection;
        LayoutUndoButton.IsEnabled = _layoutEditHistory.CanUndo(_layoutEditorProfileKey);
    }

    private LayoutEdge? GetUnavailableTaskbarEdge()
    {
        return _coordinator.Current.Window.HostMode == WindowHostMode.Taskbar
            ? TaskbarEdgeService.TryResolveCurrent()
            : null;
    }

    private LayoutProfileKey ResolveCurrentLayoutProfile()
    {
        var settings = _coordinator.Current.Window;
        var vertical = settings.LayoutMode switch
        {
            PlayerLayoutMode.Vertical => true,
            PlayerLayoutMode.Horizontal => false,
            _ when settings.HostMode == WindowHostMode.Taskbar =>
                TaskbarEdgeService.TryResolveCurrentVerticalLayout() ??
                (TaskbarEdgeService.TryResolveCurrent() is LayoutEdge.Left or LayoutEdge.Right),
            _ => false
        };
        return LayoutRuntimeService.ResolveProfileKey(vertical);
    }

    private static void AddComboOption<T>(ComboBox combo, T value, string resourceKey)
    {
        combo.Items.Add(new ComboBoxItem
        {
            Content = Loc.Get(resourceKey),
            Tag = value
        });
    }

    private static int FindFirstEnabledIndex(ComboBox combo)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is ComboBoxItem { IsEnabled: true })
            {
                return index;
            }
        }
        return -1;
    }

    private Brush FindBrush(string resourceKey, Brush fallback) =>
        TryFindResource(resourceKey) as Brush ?? fallback;

    private static string GetWidgetTitle(LayoutWidgetElement widget)
    {
        return widget.Settings switch
        {
            CommandWidgetSettings command => GetCommandOptionLabel(command.Command),
            MediaTextWidgetSettings text when widget.TypeId == BuiltInWidgetTypeIds.MediaText =>
                GetMediaTextOptionLabel(text.TextKind),
            MetricsWidgetSettings metrics => GetMetricOptionLabel(metrics.Metric),
            _ when ComponentCatalog.TryGet(widget.TypeId, out var definition) =>
                Loc.Get(definition.NameResourceKey),
            _ => widget.TypeId
        };
    }

    private static string GetCommandOptionLabel(MediaCommandKind command) =>
        Loc.Get(GetCommandOptionKey(command));

    private static string GetMediaTextOptionLabel(MediaTextKind kind) => kind switch
    {
        MediaTextKind.Title => Loc.Get("Settings.Layout.PropertyTextTitle"),
        MediaTextKind.Artist => Loc.Get("Settings.Layout.PropertyTextArtist"),
        MediaTextKind.Source => Loc.Get("Settings.Layout.PropertyTextSource"),
        MediaTextKind.TitleAndArtist => Loc.Get("Settings.Layout.PropertyTextTitleAndArtist"),
        _ => Loc.Get("Settings.LayoutWidget.MediaTextTitle")
    };

    private static string GetMetricOptionLabel(MetricKind metric) =>
        Loc.Get(GetMetricOptionKey(metric));

    private static string GetSlotName(LayoutSlotKind slotKind) => Loc.Get(GetSlotResourceKey(slotKind));

    private static string GetSlotResourceKey(LayoutSlotKind slotKind) => slotKind switch
    {
        LayoutSlotKind.Secondary => "Settings.Layout.EditorNearContent",
        LayoutSlotKind.Expanded => "Settings.Layout.EditorExpandedContent",
        _ => "Settings.Layout.EditorLeaveContent"
    };

    private static string GetEdgeName(LayoutEdge edge) => Loc.Get(GetEdgeResourceKey(edge));

    private static string GetEdgeResourceKey(LayoutEdge edge) => edge switch
    {
        LayoutEdge.Top => "Settings.Layout.EdgeTop",
        LayoutEdge.Right => "Settings.Layout.EdgeRight",
        LayoutEdge.Bottom => "Settings.Layout.EdgeBottom",
        LayoutEdge.Left => "Settings.Layout.EdgeLeft",
        _ => "Settings.Layout.EdgeTop"
    };

    private static string GetCommandOptionKey(MediaCommandKind command) => command switch
    {
        MediaCommandKind.Previous => "Main.Control.Previous",
        MediaCommandKind.PlayPause => "Main.Control.Play",
        MediaCommandKind.Next => "Main.Control.Next",
        MediaCommandKind.SelectSource => "Main.Menu.ShowSource",
        MediaCommandKind.AdjustVolume => "Main.Volume.Current",
        MediaCommandKind.SelectOutputDevice => "Main.Device.Output",
        _ => "Settings.Layout.PropertyCommand"
    };

    private static string GetMetricOptionKey(MetricKind metric) => metric switch
    {
        MetricKind.SystemMemory => "Settings.Layout.PropertyMetricMemory",
        MetricKind.SystemCpu => "Settings.Layout.PropertyMetricCpu",
        MetricKind.SystemGpu => "Settings.Layout.PropertyMetricGpu",
        MetricKind.ProcessMemory => "Settings.Layout.PropertyMetricApp",
        _ => "Settings.Layout.PropertyMetric"
    };

    private enum LayoutEditorNodeKind
    {
        InlineContainer,
        EdgeContainer,
        Widget
    }

    private sealed record LayoutDropTarget(string ContainerId, LayoutSlotKind SlotKind);

    private sealed record PaletteEntry(string Token, string Label, string Description);

    private sealed record LayoutEditorSelection(
        string InstanceId,
        LayoutEditorNodeKind Kind,
        string? ParentContainerId,
        LayoutSlotKind SlotKind,
        object Model);
}
