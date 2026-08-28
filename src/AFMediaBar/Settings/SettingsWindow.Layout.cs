using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Controls;
using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.LayoutEditor.Wpf.Input;
using AFMediaBar.LayoutEditor.Wpf.Controls;
using AFMediaBar.LayoutEditor.Wpf.Preview;
using AFMediaBar.LayoutEditor.Wpf.ViewModels;
using AFMediaBar.LayoutEditor.Wpf.Views;
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
    private const int LayoutEditorPaddingCells = 6;
    private const string NewWidgetDragFormat = LayoutEditorDragFormats.NewWidget;
    private const string NewContainerDragFormat = LayoutEditorDragFormats.NewContainer;
    private const string ExistingWidgetDragFormat = LayoutEditorDragFormats.ExistingWidget;
    private const string ExistingContainerDragFormat = LayoutEditorDragFormats.ExistingContainer;

    private readonly LayoutEditorCommandProcessor _layoutEditorCommands =
        new(new CoreLayoutConstraintAdapter());
    private LayoutProfileKey _layoutEditorProfileKey = LayoutProfileKey.Horizontal;
    private LayoutEditorSelection? _layoutEditorSelection;
    private LayoutEditorSession? _layoutEditorSession;
    private LayoutEditorViewModel? _layoutEditorViewModel;
    private LayoutEditorControl? _layoutEditorHostControl;
    private bool _layoutEditorSyncing;
    private bool _layoutPropertySyncing;
    private bool _hasSkinPreview;
    private string? _skinPreviewInstanceId;
    private ComponentSkinAssignment? _skinPreviewAssignment;
    private LayoutProfileKey _skinPreviewProfileKey;
    private Point _layoutDragStart;
    private readonly List<LayoutEditorPreviewSurface> _layoutPreviewSurfaces = [];
    private readonly List<ComponentLayoutSurface> _layoutPaletteSurfaces = [];
    private Popup? _layoutDragPreviewPopup;
    private Border? _layoutPreviewDropOverlay;
    private Adorner? _layoutPreviewDropAdorner;
    private FrameworkElement? _layoutPreviewDropAdornerTarget;
    private ContextMenu? _layoutPreviewDeleteMenu;
    // 细网格放置状态机：调色板点击容器工具后 armed，画布上单击创建 1 x 1、拖动创建矩形，释放提交。
    // Fine-grid placement state machine: arming a container tool lets the canvas commit a 1x1 click or a dragged rectangle.
    private readonly LayoutEditorInteractionState _layoutInteraction = new();
    private LayoutEditorPointerController _layoutPointerController = null!;
    private WidgetSettings? _layoutWidgetSettings;
    private LayoutPlacementTool? _layoutPlacementTool
    {
        get => _layoutInteraction.PlacementTool;
        set => _layoutInteraction.PlacementTool = value;
    }

    private bool _layoutDrawing
    {
        get => _layoutInteraction.IsDrawing;
        set => _layoutInteraction.IsDrawing = value;
    }

    private bool _layoutDragMoved
    {
        get => _layoutInteraction.DragMoved;
        set => _layoutInteraction.DragMoved = value;
    }

    private Point _layoutDrawStartDip
    {
        get => _layoutInteraction.DrawStart;
        set => _layoutInteraction.DrawStart = value;
    }

    private LayoutGridRect? _layoutDrawCandidate
    {
        get => _layoutInteraction.DrawCandidate;
        set => _layoutInteraction.DrawCandidate = value;
    }
    private readonly LayoutEditorOverlay _layoutOverlay = new();
    private Canvas? _layoutEditorCanvas;
    private FrameworkElement? _layoutEditorViewport;
    private readonly TransformGroup _layoutCanvasTransform = new();
    private bool _layoutPanning
    {
        get => _layoutInteraction.IsPanning;
        set => _layoutInteraction.IsPanning = value;
    }

    private Point _layoutPanStart
    {
        get => _layoutInteraction.PanStart;
        set => _layoutInteraction.PanStart = value;
    }

    private Point _layoutPanOrigin
    {
        get => _layoutInteraction.PanOrigin;
        set => _layoutInteraction.PanOrigin = value;
    }
    private readonly LayoutViewportState _layoutViewportState = new();
    private readonly LayoutEditorInputRouter _layoutInputRouter = new();
    private double _layoutEditorCompositionWidth;
    private double _layoutEditorCompositionHeight;
    private bool _layoutEditorResizeInProgress;
    private readonly Dictionary<(string InstanceId, LayoutEdge Edge), int> _layoutResizeAppliedCells = [];

    private void InitializeLayoutEditor()
    {
        _layoutPointerController = new(_layoutInteraction, _layoutViewportState);
        LayoutVisualEditorHost.AllowDrop = true;
        LayoutVisualEditorHost.DragEnter += LayoutPreviewDropHost_OnDragEnter;
        LayoutVisualEditorHost.DragOver += LayoutVisualEditorHost_OnDragOver;
        LayoutVisualEditorHost.DragLeave += LayoutPreviewDropHost_OnDragLeave;
        LayoutVisualEditorHost.Drop += LayoutVisualEditorHost_OnDrop;
        _layoutInputRouter.MouseLeftButtonDown += LayoutEditorCanvas_OnMouseLeftButtonDown;
        _layoutInputRouter.MouseMove += LayoutEditorCanvas_OnMouseMove;
        _layoutInputRouter.MouseLeftButtonUp += LayoutEditorCanvas_OnMouseLeftButtonUp;
        _layoutInputRouter.MouseLeave += LayoutEditorCanvas_OnMouseLeave;
        _layoutInputRouter.PreviewKeyDown += LayoutEditorCanvas_OnPreviewKeyDown;
        LayoutPaletteView.PreviewFactory = item => CreatePalettePreview(item.Token);
        PopulateLayoutEditorOptions();
        _layoutEditorProfileKey = ResolveCurrentLayoutProfile();
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
            ClearSkinPreview();
            _layoutEditorProfileKey = currentKey;
            _layoutEditorSelection = null;
            _layoutEditorViewModel?.SelectProfile(currentKey);
        }

        PopulateLayoutEditorOptions();
        RefreshLayoutEditor();
    }

    private void PopulateLayoutEditorOptions()
    {
        _layoutEditorSyncing = true;
        try
        {
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
        if (LayoutComponentCategories is null)
        {
            return;
        }

        foreach (var surface in _layoutPaletteSurfaces)
        {
            surface.Dispose();
        }
        _layoutPaletteSurfaces.Clear();
        var selectedCategory = LayoutComponentCategories.SelectedItem is TabItem { Tag: ComponentCategory category }
            ? category
            : ComponentCategory.Media;
        LayoutComponentCategories.Items.Clear();
        foreach (var group in EnumeratePaletteEntries().GroupBy(entry => entry.Category))
        {
            var panel = new WrapPanel();
            foreach (var entry in group)
            {
                var preview = CreatePalettePreview(entry.Token);
                var button = new Button
                {
                    Width = 90,
                    Height = 68,
                    Content = new StackPanel
                    {
                        IsHitTestVisible = false,
                        Children =
                        {
                            new Viewbox
                            {
                                Width = 72,
                                Height = 36,
                                Stretch = Stretch.Uniform,
                                Child = preview
                            },
                            new TextBlock
                            {
                                Text = entry.Label,
                                FontSize = 10,
                                TextAlignment = TextAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                Margin = new Thickness(2, 2, 2, 0)
                            }
                        }
                    },
                    Tag = entry.Token,
                    Margin = new Thickness(0, 0, 5, 5),
                    Padding = new Thickness(3),
                    Cursor = Cursors.Hand,
                    Style = TryFindResource("SettingsActionButtonStyle") as Style,
                    ToolTip = entry.Description
                };
                button.PreviewMouseLeftButtonDown += LayoutDragSource_OnPreviewMouseLeftButtonDown;
                button.Click += LayoutPaletteButton_OnClick;
                panel.Children.Add(button);
            }

            LayoutComponentCategories.Items.Add(new TabItem
            {
                Header = Loc.Get(GetComponentCategoryResourceKey(group.Key)),
                Tag = group.Key,
                Content = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = panel
                }
            });
        }

        LayoutComponentCategories.SelectedItem = LayoutComponentCategories.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => item.Tag is ComponentCategory category && category == selectedCategory)
            ?? LayoutComponentCategories.Items.OfType<TabItem>().FirstOrDefault();
    }

    private ComponentLayoutSurface CreatePalettePreview(string paletteToken)
    {
        if (TryParseContainerToken(paletteToken, out var containerKind))
        {
            if (containerKind == LayoutContainerKind.AutoCollapse)
            {
                return CreateCollapsePalettePreview();
            }

            return CreateContainerPalettePreview(containerKind);
        }

        var parts = paletteToken.Split('|', 2);
        var typeId = parts[0];
        var settings = ComponentCatalog.CreateDefaultSettings(typeId);
        if (parts.Length == 2 &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var option))
        {
            settings = typeId switch
            {
                BuiltInWidgetTypeIds.Command when Enum.IsDefined(typeof(MediaCommandKind), option) =>
                    new CommandWidgetSettings(
                        (MediaCommandKind)option,
                        CommandWidgetSettings.DefaultButtonSizeDip),
                BuiltInWidgetTypeIds.MediaText when Enum.IsDefined(typeof(MediaTextKind), option) =>
                    new MediaTextWidgetSettings((MediaTextKind)option, false, 14, 1),
                _ => settings
            };
        }

        // 调色板预览用足够大的网格容器容纳组件的自然尺寸，避免网格矩形太小裁掉内容。
        // The palette preview uses a large grid container so the widget's intrinsic size is not clipped by a tiny rectangle.
        var widget = new LayoutWidgetElement(
            "palette-widget",
            true,
            LayoutGeometry.Auto,
            typeId,
            settings,
            null,
            null,
            null,
            new LayoutGridRect(2, 1, 28, 8));
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
            new LayoutGridRect(0, 0, 32, 10));
        var profile = new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default with { GapDip = 2, WidthDip = null, HeightDip = null },
            LayoutGridSettings.Default,
            [container],
            []);
        var surface = new ComponentLayoutSurface();
        surface.SetDesignMode(true);
        surface.SetDesignPlacementArmed(_layoutPlacementTool is not null);
        surface.SetUseMenuThemeForContent(true);
        surface.SetMediaSnapshot(CreateLayoutPreviewSnapshot());
        surface.Apply(profile, pointerNear: true);
        surface.IsHitTestVisible = false;
        _layoutPaletteSurfaces.Add(surface);
        return surface;
    }

    /// <summary>
    /// 容器预览：设计模式空容器渲染出可辨识的容器轮廓，供分类选择直接点击绘制。
    /// Container preview: an empty container in design mode renders a recognizable outline for direct placement.
    /// </summary>
    private ComponentLayoutSurface CreateContainerPalettePreview(LayoutContainerKind kind)
    {
        var hover = kind == LayoutContainerKind.HoverSwitch;
        var container = new LayoutContainerElement(
            "palette-container",
            true,
            LayoutGeometry.Auto,
            kind,
            LayoutFlowOrientation.Automatic,
            LayoutContentAlignment.Center,
            LayoutContentAlignment.Center,
            hover ? LayoutTriggerMode.PointerNear : LayoutTriggerMode.Always,
            0,
            hover ? LayoutAnimationSettings.Default : new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
            new LayoutSlot(hover ? "leave" : "content", []),
            new LayoutSlot(hover ? "near" : "unused", []),
            new LayoutGridRect(1, 1, 12, 5));
        var profile = new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default with { GapDip = 2, WidthDip = null, HeightDip = null },
            LayoutGridSettings.Default,
            [container],
            []);
        var surface = new ComponentLayoutSurface();
        surface.SetDesignMode(true);
        surface.Apply(profile, pointerNear: hover);
        surface.IsHitTestVisible = false;
        _layoutPaletteSurfaces.Add(surface);
        return surface;
    }

    private ComponentLayoutSurface CreateCollapsePalettePreview()
    {
        var anchor = new LayoutContainerElement(
            "palette-anchor",
            true,
            LayoutGeometry.Auto,
            LayoutContainerKind.Static,
            LayoutFlowOrientation.Automatic,
            LayoutContentAlignment.Center,
            LayoutContentAlignment.Center,
            LayoutTriggerMode.Always,
            0,
            new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
            LayoutSlot.Empty("content"),
            LayoutSlot.Empty("unused"),
            new LayoutGridRect(2, 4, 14, 4));
        var collapse = new LayoutCollapseContainer(
            "palette-collapse",
            true,
            new LayoutGridRect(2, 1, 14, 3),
            new LayoutAttachment(anchor.InstanceId, LayoutEdge.Top),
            6,
            72,
            LayoutAnimationSettings.Default,
            LayoutSlot.Empty("expanded"));
        var profile = new LayoutProfile(
            LayoutProfileKey.Horizontal,
            PlayerLayoutMode.Horizontal,
            LayoutSurfaceSettings.Default with { GapDip = 2, WidthDip = null, HeightDip = null },
            LayoutGridSettings.Default,
            [anchor],
            [collapse]);
        var surface = new ComponentLayoutSurface();
        surface.SetDesignMode(true);
        surface.SetMediaSnapshot(CreateLayoutPreviewSnapshot());
        surface.ApplyEdge(profile, collapse);
        surface.IsHitTestVisible = false;
        _layoutPaletteSurfaces.Add(surface);
        return surface;
    }

    private static IEnumerable<PaletteEntry> EnumeratePaletteEntries()
    {
        // 容器作为组件放入分类选择，提供预览图片。
        // Containers join the component palette with a live preview.
        yield return new PaletteEntry(
            "container:static",
            Loc.Get("Settings.Layout.EditorContainerStaticShort"),
            Loc.Get("Settings.Layout.EditorAddStaticContainer"),
            ComponentCategory.Layout);
        yield return new PaletteEntry(
            "container:hover",
            Loc.Get("Settings.Layout.EditorContainerHoverShort"),
            Loc.Get("Settings.Layout.EditorAddHoverContainer"),
            ComponentCategory.Layout);
        yield return new PaletteEntry(
            "container:edge",
            Loc.Get("Settings.Layout.EditorContainerEdgeShort"),
            Loc.Get("Settings.Layout.EditorAddEdgeContainer"),
            ComponentCategory.Layout);

        foreach (var definition in ComponentCatalog.All)
        {
            if (definition.TypeId == BuiltInWidgetTypeIds.Command)
            {
                foreach (var command in Enum.GetValues<MediaCommandKind>())
                {
                    yield return new PaletteEntry(
                        $"{definition.TypeId}|{(int)command}",
                        Loc.Get(GetCommandOptionKey(command)),
                        Loc.Get(definition.DescriptionResourceKey),
                        command is MediaCommandKind.AdjustVolume or MediaCommandKind.SelectOutputDevice
                            ? ComponentCategory.Audio
                            : definition.Category);
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
                        Loc.Get(definition.DescriptionResourceKey),
                        definition.Category);
                }

                continue;
            }

            yield return new PaletteEntry(
                definition.TypeId,
                Loc.Get(definition.NameResourceKey),
                Loc.Get(definition.DescriptionResourceKey),
                definition.Category);
        }
    }

    private void RefreshLayoutEditor()
    {
        if (_layoutEditorSyncing || !_isInitialized || LayoutVisualEditorHost is null)
        {
            return;
        }

        var preserveViewport = _layoutEditorCanvas is not null;
        var preservedTranslate = _layoutViewportState.Translate;
        var preservedScale = _layoutViewportState.Scale;
        var document = _coordinator.Current.Layout;
        if (_layoutEditorViewModel is null ||
            _layoutEditorViewModel.Session.Document != document)
        {
            _layoutEditorViewModel?.Dispose();
            _layoutEditorViewModel = CreateLayoutEditorViewModel(document);
        }
        else if (_layoutEditorViewModel.ProfileKey != _layoutEditorProfileKey)
        {
            _layoutEditorViewModel.SelectProfile(_layoutEditorProfileKey);
        }
        _layoutEditorSession = _layoutEditorViewModel.Session;
        LayoutEditorPage.DataContext = _layoutEditorViewModel;
        var profile = ApplySkinPreview(
            document.Get(_layoutEditorProfileKey));
        var selectedId = _layoutEditorSelection?.InstanceId;
        _layoutEditorSelection = string.IsNullOrWhiteSpace(selectedId)
            ? null
            : ResolveSelection(profile, selectedId);
        _layoutEditorViewModel.SelectNode(_layoutEditorSelection?.InstanceId);
        DisposeLayoutPreviewSurfaces();
        // 画布重建后旧的幽灵/画布引用随之失效。
        _layoutOverlay.Reset();
        _layoutEditorCanvas = null;
        _layoutViewportState.ResetCentered();
        _layoutEditorHostControl ??= new LayoutEditorControl();
        _layoutEditorHostControl.DataContext = _layoutEditorViewModel;
        _layoutEditorHostControl.Session = _layoutEditorSession;
        _layoutEditorHostControl.PreviewFactory = previewProfile =>
            BuildVisualEditor(ApplySkinPreview(previewProfile));
        LayoutVisualEditorHost.Child = _layoutEditorHostControl;
        if (preserveViewport)
        {
            UpdateLayoutCanvasTranslate(preservedTranslate, preservedScale);
            _layoutViewportState.MarkCentered();
        }
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

    private void PopulateLayoutObjectTree(LayoutProfile profile)
    {
        if (LayoutObjectTree is null)
        {
            return;
        }

        _layoutEditorSyncing = true;
        try
        {
            LayoutObjectTree.Items.Clear();
            foreach (var container in profile.Containers)
            {
                LayoutObjectTree.Items.Add(BuildInlineTreeItem(container, null, LayoutSlotKind.Primary));
            }

            foreach (var collapse in profile.CollapseContainers)
            {
                LayoutObjectTree.Items.Add(BuildCollapseTreeItem(collapse));
            }

            if (!string.IsNullOrWhiteSpace(_layoutEditorSelection?.InstanceId) &&
                FindTreeItem(LayoutObjectTree.Items, _layoutEditorSelection.InstanceId) is { } selected)
            {
                selected.IsSelected = true;
                selected.BringIntoView();
            }
        }
        finally
        {
            _layoutEditorSyncing = false;
        }
    }

    private TreeViewItem BuildInlineTreeItem(
        LayoutContainerElement container,
        string? parentId,
        LayoutSlotKind parentSlot)
    {
        var selection = new LayoutEditorSelection(
            container.InstanceId,
            LayoutEditorNodeKind.InlineContainer,
            parentId,
            parentSlot,
            container);
        var label = Loc.Get(container.ContainerKind == LayoutContainerKind.HoverSwitch
            ? "Settings.Layout.ContainerHoverSwitch"
            : "Settings.Layout.ContainerStatic");
        var item = CreateLayoutTreeItem("\uE8B7", label, container.InstanceId, selection, container.Enabled);
        item.Items.Add(BuildSlotTreeItem(
            container.PrimarySlot,
            container.InstanceId,
            LayoutSlotKind.Primary,
            container.ContainerKind == LayoutContainerKind.HoverSwitch
                ? "Settings.Layout.EditorLeaveContent"
                : "Settings.Layout.EditorContent"));
        if (container.ContainerKind == LayoutContainerKind.HoverSwitch)
        {
            item.Items.Add(BuildSlotTreeItem(
                container.SecondarySlot,
                container.InstanceId,
                LayoutSlotKind.Secondary,
                "Settings.Layout.EditorNearContent"));
        }
        return item;
    }

    private TreeViewItem BuildCollapseTreeItem(LayoutCollapseContainer collapse)
    {
        var selection = new LayoutEditorSelection(
            collapse.InstanceId,
            LayoutEditorNodeKind.EdgeContainer,
            null,
            LayoutSlotKind.Expanded,
            collapse);
        var item = CreateLayoutTreeItem(
            "\uE7F1",
            $"{Loc.Get("Settings.Layout.ContainerAutoCollapse")} · {GetEdgeName(collapse.Attachment.AttachmentSide)}",
            collapse.InstanceId,
            selection,
            collapse.Enabled);
        item.Items.Add(BuildSlotTreeItem(
            collapse.ExpandedSlot,
            collapse.InstanceId,
            LayoutSlotKind.Expanded,
            "Settings.Layout.EditorExpandedContent"));
        return item;
    }

    private TreeViewItem BuildSlotTreeItem(
        LayoutSlot slot,
        string parentId,
        LayoutSlotKind slotKind,
        string resourceKey)
    {
        var item = new TreeViewItem
        {
            Header = CreateTreeHeader("\uE8A0", Loc.Get(resourceKey), slot.SlotId, enabled: true, isSlot: true),
            IsExpanded = true
        };
        foreach (var child in slot.Children)
        {
            if (child is LayoutWidgetElement widget)
            {
                var selection = new LayoutEditorSelection(
                    widget.InstanceId,
                    LayoutEditorNodeKind.Widget,
                    parentId,
                    slotKind,
                    widget);
                item.Items.Add(CreateLayoutTreeItem(
                    "\uE7C3",
                    GetWidgetTitle(widget),
                    widget.InstanceId,
                    selection,
                    widget.Enabled));
            }
            else if (child is LayoutContainerElement nested)
            {
                item.Items.Add(BuildInlineTreeItem(nested, parentId, slotKind));
            }
        }

        if (item.Items.Count == 0)
        {
            item.Items.Add(new TreeViewItem
            {
                Header = CreateTreeHeader(
                    "\uE73A",
                    Loc.Get("Settings.Layout.EditorEmptySlot"),
                    slot.SlotId,
                    enabled: false,
                    isSlot: true),
                IsHitTestVisible = false
            });
        }
        return item;
    }

    private TreeViewItem CreateLayoutTreeItem(
        string icon,
        string label,
        string instanceId,
        LayoutEditorSelection selection,
        bool enabled)
    {
        return new TreeViewItem
        {
            Header = CreateTreeHeader(icon, label, instanceId, enabled, isSlot: false),
            Tag = selection,
            IsExpanded = true,
            ToolTip = instanceId,
            ContextMenu = BuildLayoutContextMenu(selection, label)
        };
    }

    /// <summary>
    /// 右键小菜单：显示组件/容器名称，并提供删除动作；组件树与画布预览共用。
    /// Right-click mini menu showing the element name plus a delete action; shared by the tree and the live preview.
    /// </summary>
    private ContextMenu BuildLayoutContextMenu(LayoutEditorSelection selection, string label)
    {
        var menu = new ContextMenu();
        var title = new MenuItem
        {
            Header = label,
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        };
        menu.Items.Add(title);
        var delete = new MenuItem
        {
            Header = Loc.Get("Settings.Layout.EditorRemove")
        };
        delete.Click += (_, _) => DeleteLayoutSelection(selection);
        menu.Items.Add(delete);
        return menu;
    }

    private void DeleteLayoutSelection(LayoutEditorSelection selection)
    {
        if (TryApplyProfile(profile =>
            LayoutGridConstraintService.TryRemove(profile, selection.InstanceId).Updated))
        {
            if (_layoutEditorSelection?.InstanceId == selection.InstanceId)
            {
                _layoutEditorSelection = null;
            }

            RefreshLayoutEditor();
        }
    }

    private FrameworkElement CreateTreeHeader(
        string icon,
        string label,
        string toolTip,
        bool enabled,
        bool isSlot)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Opacity = enabled ? 1 : 0.5,
            ToolTip = toolTip
        };
        var iconText = new TextBlock
        {
            Width = 20,
            Text = icon,
            FontFamily = TryFindResource("AppIconFontFamily") as FontFamily,
            VerticalAlignment = VerticalAlignment.Center
        };
        SetDynamicResource(
            iconText,
            TextBlock.ForegroundProperty,
            isSlot ? "MenuSecondaryTextBrush" : "MenuPrimaryTextBrush");
        panel.Children.Add(iconText);
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = isSlot ? 11 : 12,
            FontWeight = isSlot ? FontWeights.Normal : FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        SetDynamicResource(
            labelText,
            TextBlock.ForegroundProperty,
            isSlot ? "MenuSecondaryTextBrush" : "MenuPrimaryTextBrush");
        panel.Children.Add(labelText);
        return panel;
    }

    private static TreeViewItem? FindTreeItem(ItemCollection items, string instanceId)
    {
        foreach (var item in items.OfType<TreeViewItem>())
        {
            if (item.Tag is LayoutEditorSelection selection && selection.InstanceId == instanceId)
            {
                return item;
            }
            if (FindTreeItem(item.Items, instanceId) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }

    private void LayoutObjectTree_OnSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (_layoutEditorSyncing ||
            e.NewValue is not TreeViewItem { Tag: LayoutEditorSelection selection })
        {
            return;
        }

        SelectLayoutNode(selection);
    }

    private void LayoutTreeView_OnItemSelected(
        object? sender,
        LayoutTreeItemViewModel item)
    {
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        if (ResolveSelection(profile, item.InstanceId) is not { } selection)
        {
            return;
        }

        if (_hasSkinPreview &&
            !string.Equals(_skinPreviewInstanceId, selection.InstanceId, StringComparison.Ordinal))
        {
            ClearSkinPreview();
        }

        _layoutEditorSelection = selection;
        foreach (var surface in _layoutPreviewSurfaces)
        {
            surface.SetDesignSelection(selection.InstanceId);
        }
        RefreshSelectionText();
        RefreshLayoutProperties();
        UpdateLayoutEditorButtons();
    }

    private void LayoutPaletteView_OnItemInvoked(
        object? sender,
        ComponentPaletteItemViewModel item)
    {
        var paletteToken = item.Token;
        if (TryParseContainerToken(paletteToken, out var kind))
        {
            if (kind == LayoutContainerKind.AutoCollapse)
            {
                AddCollapseContainerFromPalette();
                return;
            }

            ArmContainerPlacementTool(kind);
            return;
        }

        ArmWidgetPlacementTool(paletteToken);
    }

    private void LayoutPaletteView_OnItemDragRequested(
        object? sender,
        ComponentPaletteDragEventArgs e)
    {
        var paletteToken = e.Item.Token;
        if (TryParseContainerToken(paletteToken, out var kind))
        {
            BeginVisualDrag(
                e.Source,
                new DataObject(
                    NewContainerDragFormat,
                    kind == LayoutContainerKind.HoverSwitch ? "hover" : "static"),
                DragDropEffects.Copy);
            return;
        }

        BeginVisualDrag(
            e.Source,
            new DataObject(NewWidgetDragFormat, paletteToken),
            DragDropEffects.Copy);
    }

    private void LayoutTreeToggleButton_OnToggled(object sender, RoutedEventArgs e)
    {
        if (LayoutObjectTreePanel is null || LayoutTreeToggleButton is null)
        {
            return;
        }

        LayoutObjectTreePanel.Visibility = LayoutTreeToggleButton.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private FrameworkElement BuildVisualEditor(LayoutProfile profile)
    {
        // 预览使用与主窗口相同的组件树，再用 Viewbox 适配可用区域；容器按全局 GridBounds 绝对定位。
        // Reuse the runtime component tree and fit it with a Viewbox; containers are placed by global GridBounds.
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var bodyGrid = LayoutRuntimeService.CalculateBodyGridBounds(profile)
            ?? LayoutGridRect.Unit(0, 0);
        var composition = new Grid
        {
            Width = Math.Max(360, (grid.Columns + LayoutEditorPaddingCells * 2) * cell + 64),
            Height = Math.Max(220, (grid.Rows + LayoutEditorPaddingCells * 2) * cell + 64),
            ClipToBounds = true,
            AllowDrop = false
        };
        _layoutEditorCompositionWidth = composition.Width;
        _layoutEditorCompositionHeight = composition.Height;
        SetDynamicResource(composition, Panel.BackgroundProperty, "PlayerPreviewBackgroundBrush");

        // 画布覆盖整个逻辑网格（含容器外空白），因此任何格都可被放置容器。
        // The canvas covers the whole logical grid including blank cells so a container can extend outward.
        var canvasHost = new LayoutEditorCanvas();
        var canvas = canvasHost.GridSurface;
        canvasHost.Configure(
            (grid.Columns + LayoutEditorPaddingCells * 2) * cell,
            (grid.Rows + LayoutEditorPaddingCells * 2) * cell,
            BuildGridBackground(grid, cell),
            _layoutCanvasTransform);
        _layoutInputRouter.Attach(canvasHost);
        canvas.RenderTransform = _layoutCanvasTransform;
        _layoutEditorCanvas = canvas;

        // Viewport 提供滚轮缩放与左键平移；初始把主体联合边界居中，四周留足空余格子。
        // The viewport offers wheel zoom and left-drag pan; the body union is centered on load to leave spare cells around it.
        var viewport = canvasHost;
        viewport.PreviewMouseWheel += (_, e) =>
        {
            LayoutEditorViewport_OnMouseWheel(this, e);
            e.Handled = true;
        };
        viewport.SizeChanged += (_, _) => CenterLayoutCanvasOnBody(cell);
        _layoutEditorViewport = viewport;
        CenterLayoutCanvasOnBody(cell);

        var inlineSurface = CreatePreviewSurface(profile);
        Canvas.SetLeft(inlineSurface, (bodyGrid.X + LayoutEditorPaddingCells) * cell);
        Canvas.SetTop(inlineSurface, (bodyGrid.Y + LayoutEditorPaddingCells) * cell);
        canvas.Children.Add(inlineSurface);

        foreach (var collapse in profile.CollapseContainers.Where(item => item.Enabled))
        {
            if (!LayoutGridConstraintService.ResolveAttachment(collapse, profile).Valid)
            {
                continue;
            }

            var surface = CreatePreviewSurface(profile, collapse);
            Canvas.SetLeft(surface, (collapse.GridBounds.X + LayoutEditorPaddingCells) * cell);
            Canvas.SetTop(surface, (collapse.GridBounds.Y + LayoutEditorPaddingCells) * cell);
            surface.Width = collapse.GridBounds.Width * cell;
            surface.Height = collapse.GridBounds.Height * cell;
            canvas.Children.Add(surface);
        }

        // 释放高亮只属于设计模式，避免拖动时用户失去当前槽位的空间反馈；运行时窗口不会创建此层。
        // The drop highlight exists only in design mode so users keep spatial feedback while dragging; runtime never creates it.
        var dropOverlayText = new TextBlock
        {
            Text = Loc.Get("Settings.Layout.EditorDropHere"),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        SetDynamicResource(dropOverlayText, TextBlock.ForegroundProperty, "MenuPrimaryTextBrush");
        var dropOverlay = new Border
        {
            Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            IsHitTestVisible = false,
            Child = dropOverlayText
        };
        SetDynamicResource(dropOverlay, Border.BackgroundProperty, "LayoutEditorDropBrush");
        SetDynamicResource(dropOverlay, Border.BorderBrushProperty, "LayoutEditorAccentBrush");
        _layoutPreviewDropOverlay = dropOverlay;
        var centerContent = new Grid();
        centerContent.Children.Add(viewport);
        centerContent.Children.Add(dropOverlay);
        var center = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = centerContent,
            AllowDrop = false,
            ToolTip = Loc.Get("Settings.Layout.EditorDropHere")
        };
        SetDynamicResource(center, Border.BackgroundProperty, "PlayerPreviewBackgroundBrush");
        SetDynamicResource(center, Border.BorderBrushProperty, "MenuBorderBrush");
        composition.Children.Add(center);

        var previewHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(8),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = composition
            }
        };
        SetDynamicResource(previewHost, Border.BackgroundProperty, "MenuHoverBrush");
        return previewHost;
    }

    private LayoutEditorPreviewSurface CreatePreviewSurface(LayoutProfile profile, LayoutCollapseContainer? collapse = null)
    {
        var surface = new LayoutEditorPreviewSurface();
        surface.SetDesignMode(true);
        surface.SetDesignPlacementArmed(_layoutPlacementTool is not null);
        surface.DesignElementSelected += LayoutPreviewSurface_OnElementSelected;
        surface.DesignPreviewStateChanged += LayoutPreviewSurface_OnPreviewStateChanged;
        surface.DesignResizeRequested += LayoutPreviewSurface_OnResizeRequested;
        surface.DesignResizeCompleted += LayoutPreviewSurface_OnResizeCompleted;
        surface.DesignDeleteRequested += LayoutPreviewSurface_OnDeleteRequested;
        surface.SetMediaSnapshot(CreateLayoutPreviewSnapshot());
        if (collapse is null)
        {
            surface.Apply(profile, pointerNear: ResolvePreviewPointerNear());
        }
        else
        {
            surface.ApplyEdge(profile, collapse);
        }

        _layoutPreviewSurfaces.Add(surface);
        return surface;
    }

    private void LayoutPreviewSurface_OnPreviewStateChanged(
        object? sender,
        LayoutDesignPreviewStateEventArgs e)
    {
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        if (ResolveSelection(profile, e.ContainerId) is not { } selection)
        {
            return;
        }

        _layoutEditorSelection = selection with
        {
            SlotKind = e.PointerNear ? LayoutSlotKind.Secondary : LayoutSlotKind.Primary
        };
        foreach (var surface in _layoutPreviewSurfaces)
        {
            surface.SetDesignSelection(e.ContainerId);
        }
        _layoutEditorViewModel?.SelectNode(e.ContainerId);
        RefreshSlotOptions();
        RefreshSelectionText();
        RefreshLayoutProperties();
        UpdateLayoutEditorButtons();
    }

    /// <summary>
    /// 四边 Thumb 的累计 DIP 增量换算为整数格后交给约束服务；拖动到非法候选时保持原位。
    /// Converts cumulative DIP deltas from an edge Thumb into integer cells and submits to the constraint service; illegal candidates keep the original bounds.
    /// </summary>
    private void LayoutPreviewSurface_OnResizeRequested(
        object? sender,
        LayoutDesignResizeEventArgs e)
    {
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        _layoutEditorResizeInProgress = true;
        var key = (e.InstanceId, e.Edge);
        var targetCells = (int)Math.Round(e.DeltaDip / cell, MidpointRounding.AwayFromZero);
        var previousCells = _layoutResizeAppliedCells.TryGetValue(key, out var applied)
            ? applied
            : 0;
        var deltaCells = targetCells - previousCells;
        if (deltaCells == 0)
        {
            return;
        }

        var session = _layoutEditorSession;
        if (session is null || !ReferenceEquals(session.Document, _coordinator.Current.Layout))
        {
            session = new LayoutEditorSession(_coordinator.Current.Layout, _layoutEditorProfileKey);
            _layoutEditorSession = session;
        }
        else
        {
            session.SelectProfile(_layoutEditorProfileKey);
        }

        if (_layoutEditorCommands.TryResize(session, e.InstanceId, e.Edge, deltaCells))
        {
            _layoutResizeAppliedCells[key] = targetCells;
            var updatedProfile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
            foreach (var surface in _layoutPreviewSurfaces)
            {
                surface.RefreshDesignGeometry(updatedProfile);
            }
        }
    }

    private void LayoutPreviewSurface_OnResizeCompleted(object? sender, EventArgs e)
    {
        _layoutResizeAppliedCells.Clear();
        _layoutEditorResizeInProgress = false;
        RefreshLayoutEditor();
    }

    /// <summary>
    /// 实时预览右键：在命中位置弹出名称+删除小菜单。
    /// Right-click on the live preview pops the name-plus-delete menu at the hit position.
    /// </summary>
    private void LayoutPreviewSurface_OnDeleteRequested(
        object? sender,
        LayoutDesignDeleteEventArgs e)
    {
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        if (ResolveSelection(profile, e.InstanceId) is not { } selection)
        {
            return;
        }

        var label = ResolveLayoutSelectionLabel(selection.Model);
        var menu = BuildLayoutContextMenu(selection, label);
        menu.PlacementTarget = e.Source;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        _layoutPreviewDeleteMenu = menu;
    }

    private bool ResolvePreviewPointerNear()
    {
        return _layoutEditorSelection?.SlotKind == LayoutSlotKind.Secondary;
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
        null,
        null,
        0);

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

    private void ShowLayoutDropTarget(FrameworkElement target)
    {
        if (ReferenceEquals(_layoutPreviewDropAdornerTarget, target) &&
            _layoutPreviewDropAdorner is not null)
        {
            return;
        }

        HideLayoutDropTarget();
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer)
        {
            if (_layoutPreviewDropOverlay is not null)
            {
                _layoutPreviewDropOverlay.Visibility = Visibility.Visible;
            }
            return;
        }

        var adorner = new LayoutDropTargetAdorner(target)
        {
            IsHitTestVisible = false
        };
        layer.Add(adorner);
        _layoutPreviewDropAdorner = adorner;
        _layoutPreviewDropAdornerTarget = target;
    }

    private void HideLayoutDropTarget()
    {
        if (_layoutPreviewDropAdorner is not null)
        {
            AdornerLayer.GetAdornerLayer(_layoutPreviewDropAdorner.AdornedElement)
                ?.Remove(_layoutPreviewDropAdorner);
        }
        _layoutPreviewDropAdorner = null;
        _layoutPreviewDropAdornerTarget = null;
    }

    private void DisposeLayoutPreviewSurfaces()
    {
        HideLayoutDropTarget();
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
        _layoutPreviewDeleteMenu = null;
        DisposeLayoutPreviewSurfaces();
        _layoutEditorViewModel?.Dispose();
        _layoutEditorViewModel = null;
        _layoutEditorSession = null;
        foreach (var surface in _layoutPaletteSurfaces)
        {
            surface.Dispose();
        }
        _layoutPaletteSurfaces.Clear();
    }

    // ---------- 细网格放置（GRID-08） ----------

    /// <summary>
    /// 选择容器工具后进入 PaletteArmed；随后在画布上单击创建 1 x 1、拖动创建矩形，释放时提交。
    /// Arming a container tool enters PaletteArmed; a canvas click then creates 1x1 or a drag creates a rectangle, committed on release.
    /// </summary>
    private void ArmContainerPlacementTool(LayoutContainerKind kind)
    {
        _layoutPlacementTool = LayoutPlacementTool.Container(kind);
        _layoutInteraction.EndDrawing();
        SetLayoutPlacementArmed(true);
        _layoutEditorCanvas?.Focus();
        LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorPlaceContainerHint");
    }

    private void ArmWidgetPlacementTool(string paletteToken)
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
                    new CommandWidgetSettings(
                        (MediaCommandKind)option,
                        CommandWidgetSettings.DefaultButtonSizeDip),
                BuiltInWidgetTypeIds.MediaText when Enum.IsDefined(typeof(MediaTextKind), option) =>
                    new MediaTextWidgetSettings(
                        (MediaTextKind)option,
                        true,
                        option == (int)MediaTextKind.Artist ? 11 : 14,
                        1),
                _ => settings
            };
        }

        _layoutPlacementTool = LayoutPlacementTool.Widget(typeId, string.Empty, LayoutSlotKind.Primary);
        _layoutWidgetSettings = settings;
        _layoutInteraction.EndDrawing();
        SetLayoutPlacementArmed(true);
        _layoutEditorCanvas?.Focus();
        LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorPlaceWidgetHint");
    }

    private void LayoutEditorCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not LayoutEditorCanvas canvasHost)
        {
            return;
        }
        if (_layoutEditorViewport is null)
        {
            return;
        }

        _layoutPointerController.HandleMouseLeftButtonDown(
            canvasHost,
            _layoutEditorViewport,
            _layoutPlacementTool,
            UpdateLayoutDrawGhost);
        e.Handled = true;
    }

    private void LayoutEditorCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not LayoutEditorCanvas canvasHost)
        {
            return;
        }

        if (_layoutEditorViewport is null)
        {
            return;
        }

        _layoutPointerController.HandleMouseMove(
            canvasHost,
            _layoutEditorViewport,
            _layoutPlacementTool,
            UpdateLayoutCanvasTranslate,
            UpdateLayoutDrawGhost,
            UpdateLayoutHoverCell);
        e.Handled = true;
    }

    private void LayoutEditorCanvas_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not LayoutEditorCanvas canvasHost)
        {
            return;
        }

        if (_layoutEditorViewport is null)
        {
            return;
        }

        _layoutPointerController.HandleMouseLeftButtonUp(
            canvasHost,
            _layoutEditorViewport,
            _layoutPlacementTool,
            ClearLayoutSelection,
            CommitLayoutPlacement,
            HideLayoutDrawGhost,
            ClearLayoutPlacementTool);

        e.Handled = true;
    }

    private void CommitLayoutPlacement(Canvas canvas, Point point, LayoutPlacementTool tool)
    {
        if (tool.IsContainer)
        {
            CommitContainerPlacement(canvas, point);
        }
        else if (tool.WidgetTypeId is { } typeId)
        {
            CommitWidgetPlacement(canvas, point, typeId);
        }
    }

    private void CommitContainerPlacement(Canvas canvas, Point point)
    {
        var candidate = _layoutDrawCandidate;
        var (startX, startY) = LayoutCanvasToCell(canvas, _layoutDrawStartDip);
        var (currentX, currentY) = LayoutCanvasToCell(canvas, point);
        candidate ??= LayoutGridRect.FromDrag(startX, startY, currentX, currentY);
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        var result = LayoutPlacementService.TryCreateContainer(profile, _layoutPlacementTool!, candidate);
        HideLayoutDrawGhost(canvas);
        if (!result.Success || result.Updated is null)
        {
            LayoutEditorMessageText.Text = DescribeLayoutFailure(result.Failure);
            return;
        }

        TryApplyProfile(current =>
        {
            var currentResult = LayoutPlacementService.TryCreateContainer(current, _layoutPlacementTool!, candidate);
            return currentResult.Success ? currentResult.Updated : null;
        });
    }

    private void CommitWidgetPlacement(Canvas canvas, Point point, string typeId)
    {
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        var (cellX, cellY) = LayoutCanvasToCell(canvas, point);
        var (startX, startY) = LayoutCanvasToCell(canvas, _layoutDrawStartDip);
        var candidate = _layoutDrawCandidate;
        var owner = ResolveWidgetPlacementOwner(profile, startX, startY);
        HideLayoutDrawGhost(canvas);
        if (owner is null)
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorPlaceWidgetNeedsContainer");
            return;
        }

        if (profile.Containers.FirstOrDefault(item => item.InstanceId == owner.Value.ContainerId) is { } inline &&
            inline.ContainerKind == LayoutContainerKind.HoverSwitch)
        {
            owner = owner.Value with { SlotKind = ResolveVisibleSlot(inline) };
        }

        var rect = candidate ??
            LayoutGridRect.FromDrag(startX, startY, cellX, cellY);
        var local = new LayoutGridRect(
            rect.X - owner.Value.Bounds.X,
            rect.Y - owner.Value.Bounds.Y,
            rect.Width,
            rect.Height);
        var widget = new LayoutWidgetElement(
            $"widget-{Guid.NewGuid():N}",
            true,
            LayoutGeometry.Auto,
            typeId,
            _layoutWidgetSettings ?? ComponentCatalog.CreateDefaultSettings(typeId),
            null,
            null,
            null,
            local);
        if (!TryApplyProfile(current =>
                LayoutGridConstraintService.TryAddWidget(
                    current,
                    owner.Value.ContainerId,
                    owner.Value.SlotKind,
                    widget).Updated))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorAddFailed");
        }
    }

    private (string ContainerId, LayoutGridRect Bounds, LayoutSlotKind SlotKind)?
        ResolveWidgetPlacementOwner(LayoutProfile profile, int cellX, int cellY)
    {
        foreach (var container in profile.Containers.Where(item =>
                     item.Enabled && item.GridBounds is not null))
        {
            var bounds = container.GridBounds!;
            if (cellX >= bounds.X && cellX < bounds.Right &&
                cellY >= bounds.Y && cellY < bounds.Bottom)
            {
                return (container.InstanceId, bounds, ResolveVisibleSlot(container));
            }
        }

        foreach (var collapse in profile.CollapseContainers.Where(item => item.Enabled))
        {
            var bounds = collapse.GridBounds;
            if (cellX >= bounds.X && cellX < bounds.Right &&
                cellY >= bounds.Y && cellY < bounds.Bottom)
            {
                return (collapse.InstanceId, bounds, LayoutSlotKind.Expanded);
            }
        }

        return null;
    }

    private void LayoutEditorCanvas_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is LayoutEditorCanvas canvasHost)
        {
            _layoutPointerController.HandleMouseLeave(
                canvasHost,
                HideLayoutDrawGhost,
                HideLayoutHoverCell);
        }
    }

    private void LayoutEditorCanvas_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            sender is LayoutEditorCanvas canvasHost &&
            _layoutPointerController.HandlePreviewKeyDown(
                canvasHost,
                _layoutPlacementTool,
                ClearLayoutPlacementTool,
                HideLayoutDrawGhost,
                () => LayoutEditorMessageText.Text = string.Empty))
        {
            e.Handled = true;
        }
    }

    private void ClearLayoutPlacementTool()
    {
        _layoutInteraction.ClearPlacement();
        _layoutWidgetSettings = null;
        SetLayoutPlacementArmed(false);
    }

    private void SetLayoutPlacementArmed(bool armed)
    {
        foreach (var surface in _layoutPreviewSurfaces)
        {
            surface.SetDesignPlacementArmed(armed);
        }
    }

    private void ClearLayoutSelection()
    {
        if (_layoutEditorSelection is null)
        {
            return;
        }

        _layoutEditorSelection = null;
        RefreshLayoutEditor();
    }

    /// <summary>
    /// 滚轮缩放预览画布，限制在 0.4x ~ 3.0x；缩放围绕 viewport 中心。
    /// Wheel zooms the preview canvas clamped between 0.4x and 3.0x around the viewport center.
    /// </summary>
    private void LayoutEditorViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_layoutEditorViewport is not { } viewport)
        {
            return;
        }

        var viewportCenter = new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2);
        _layoutViewportState.ZoomAround(viewportCenter, e.Delta);
        UpdateLayoutCanvasTranslate(_layoutViewportState.Translate, _layoutViewportState.Scale);
        e.Handled = true;
    }

    private void UpdateLayoutCanvasTranslate(Point translate, double scale)
    {
        _layoutViewportState.Set(translate, scale);
        _layoutCanvasTransform.Children.Clear();
        _layoutCanvasTransform.Children.Add(new TranslateTransform(translate.X, translate.Y));
        _layoutCanvasTransform.Children.Add(new ScaleTransform(scale, scale));
    }

    /// <summary>
    /// 把主体联合边界居中于 viewport；四周显示网格空余格子供拓展。
    /// Centers the body union within the viewport, leaving spare grid cells around it for expansion.
    /// </summary>
    private void CenterLayoutCanvasOnBody(int cell)
    {
        if (_layoutEditorViewport is not { } viewport ||
            viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0 ||
            _layoutViewportState.IsCentered)
        {
            return;
        }

        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        var bodyGrid = LayoutRuntimeService.CalculateBodyGridBounds(profile)
            ?? LayoutGridRect.Unit(0, 0);
        var bodyCenter = new Point(
            (bodyGrid.X + bodyGrid.Right) / 2.0 * cell + LayoutEditorPaddingCells * cell,
            (bodyGrid.Y + bodyGrid.Bottom) / 2.0 * cell + LayoutEditorPaddingCells * cell);
        var viewportCenter = new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2);
        var fitScale = Math.Min(
            viewport.ActualWidth / Math.Max(_layoutEditorCompositionWidth, 1),
            viewport.ActualHeight / Math.Max(_layoutEditorCompositionHeight, 1));
        fitScale = Math.Clamp(fitScale, 0.01, 1);
        UpdateLayoutCanvasTranslate(
            new Point(
                viewportCenter.X / fitScale - bodyCenter.X,
                viewportCenter.Y / fitScale - bodyCenter.Y),
            _layoutViewportState.Scale);
        _layoutViewportState.MarkCentered();
    }

    private (int X, int Y) LayoutCanvasToCell(Canvas canvas, Point point)
    {
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        return LayoutPointerMapper.ToCell(point, grid.CellSizeDip, LayoutEditorPaddingCells);
    }

    private void UpdateLayoutDrawGhost(Canvas canvas, Point point, bool dragging)
    {
        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        if (_layoutPlacementTool is not { } tool)
        {
            return;
        }

        var grid = LayoutGridSettings.Normalize(profile.Grid);
        var cell = Math.Max(grid.CellSizeDip, 1);
        var (startX, startY) = LayoutCanvasToCell(canvas, _layoutDrawStartDip);
        var (currentX, currentY) = LayoutCanvasToCell(canvas, point);
        var preview = LayoutPlacementPreviewService.Calculate(
            profile,
            tool,
            dragging ? startX : currentX,
            dragging ? startY : currentY,
            currentX,
            currentY,
            _layoutWidgetSettings,
            ResolveVisibleSlot);
        var rect = preview.Bounds;
        _layoutDrawCandidate = preview.IsValid ? rect : null;

        // 画布原点即网格原点，幽灵直接按网格坐标定位。
        _layoutOverlay.ShowGhost(canvas, rect, cell, LayoutEditorPaddingCells, preview.IsValid);
    }

    private void UpdateLayoutHoverCell(Canvas canvas, Point point)
    {
        if (_layoutPlacementTool is not null || _layoutPanning || _layoutDrawing)
        {
            HideLayoutHoverCell();
            return;
        }

        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        var cell = Math.Max(LayoutGridSettings.Normalize(profile.Grid).CellSizeDip, 1);
        var (x, y) = LayoutCanvasToCell(canvas, point);
        if (x < 0 || y < 0)
        {
            HideLayoutHoverCell();
            return;
        }

        _layoutOverlay.ShowHoverCell(canvas, x, y, cell, LayoutEditorPaddingCells);
    }

    private void HideLayoutHoverCell()
    {
        _layoutOverlay.HideHoverCell();
    }

    private void HideLayoutDrawGhost(Canvas? canvas)
    {
        _layoutOverlay.HideGhost();
        _layoutDrawCandidate = null;
    }

    private static string DescribeLayoutFailure(LayoutGridFailure failure) => failure switch
    {
        LayoutGridFailure.OutOfGrid => Loc.Get("Settings.Layout.EditorPlaceOutOfGrid"),
        LayoutGridFailure.Overlap => Loc.Get("Settings.Layout.EditorPlaceOverlap"),
        LayoutGridFailure.DisconnectedContainerGraph => Loc.Get("Settings.Layout.EditorPlaceDisconnected"),
        LayoutGridFailure.LastNonCollapseContainer => Loc.Get("Settings.Layout.EditorPlaceLastContainer"),
        _ => Loc.Get("Settings.Layout.EditorPlaceRejected")
    };

    /// <summary>
    /// 细网格背景：用 Tile 的 DrawingBrush 画单格边框，避免为每个格创建 Border。
    /// Fine-grid background drawn with a tiled DrawingBrush so no per-cell Borders are created.
    /// </summary>
    private static DrawingBrush BuildGridBackground(LayoutGridSettings grid, int cell)
    {
        _ = grid;
        var line = new Pen(new SolidColorBrush(Color.FromArgb(72, 150, 150, 150)), 1);
        var faint = new Pen(new SolidColorBrush(Color.FromArgb(44, 150, 150, 150)), 1);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, line, new RectangleGeometry(new Rect(0.5, 0.5, cell - 1, cell - 1))));
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, faint, new GeometryGroup
        {
            Children =
            {
                new LineGeometry(new Point(0, 0), new Point(cell, 0)),
                new LineGeometry(new Point(0, 0), new Point(0, cell))
            }
        }));
        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell, cell),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
    }

    private DragDropEffects ResolveCenterDropEffect(IDataObject data)
    {
        if (data.GetData(NewContainerDragFormat) is string token)
        {
            return token is "static" or "hover" ? DragDropEffects.Copy : DragDropEffects.None;
        }

        if (data.GetData(ExistingContainerDragFormat) is string sourceId)
        {
            var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
            return profile.Containers.Any(container => container.InstanceId == sourceId)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        }

        return DragDropEffects.None;
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
        panel.Children.Add(header);
        panel.Children.Add(content);
        var card = new Border
        {
            MinWidth = minWidth,
            MinHeight = 52,
            Margin = new Thickness(3),
            Padding = new Thickness(6),
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(5),
            Cursor = Cursors.Hand,
            Tag = selection,
            ToolTip = instanceId,
            Child = panel
        };
        SetDynamicResource(card, Border.BackgroundProperty, "MenuBackgroundBrush");
        SetDynamicResource(
            card,
            Border.BorderBrushProperty,
            selected ? "LayoutEditorAccentBrush" : "MenuBorderBrush");
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
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            AllowDrop = true,
            Tag = target,
            Child = panel
        };
        SetDynamicResource(dropHost, Border.BorderBrushProperty, "MenuSeparatorBrush");
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
            Cursor = Cursors.Hand,
            Style = TryFindResource("SettingsActionButtonStyle") as Style
        };
        SetDynamicResource(
            tile,
            Button.BorderBrushProperty,
            selected ? "LayoutEditorAccentBrush" : "MenuBorderBrush");
        tile.PreviewMouseLeftButtonDown += LayoutDragSource_OnPreviewMouseLeftButtonDown;
        tile.Click += LayoutWidgetTile_OnClick;
        return tile;
    }

    private TextBlock CreateEmptyHint(string resourceKey)
    {
        var hint = new TextBlock
        {
            Text = Loc.Get(resourceKey),
            Margin = new Thickness(6),
            FontSize = 10.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        SetDynamicResource(hint, TextBlock.ForegroundProperty, "MenuSecondaryTextBrush");
        return hint;
    }

    private void AddInlineContainer(LayoutContainerKind kind)
    {
        TryApplyProfile(profile => LayoutPlacementService.TryAddContainer(profile, kind).Updated);
    }

    private void LayoutPaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string paletteToken })
        {
            return;
        }

        if (TryParseContainerToken(paletteToken, out var kind))
        {
            if (kind == LayoutContainerKind.AutoCollapse)
            {
                AddCollapseContainerFromPalette();
                return;
            }

            // 容器条目进入画布绘制模式，由用户单击/拖动确定网格位置。
            ArmContainerPlacementTool(kind);
            return;
        }

        // 组件条目进入选中工具模式：用户随后在目标容器内点击/拖动放置。
        ArmWidgetPlacementTool(paletteToken);
    }

    private void LayoutEditorOutsidePreview_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_layoutPlacementTool is null)
        {
            return;
        }

        ClearLayoutPlacementTool();
        HideLayoutDrawGhost(_layoutEditorCanvas);
        LayoutEditorMessageText.Text = string.Empty;
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

        // 拖动调色板条目直接放置默认大小：容器用容器格式，组件用组件格式。点击（单击）则由 OnClick 进入画布绘制模式。
        // Dragging a palette entry places it at default size; a plain click arms the canvas placement tool instead.
        if (TryParseContainerToken(paletteToken, out var kind))
        {
            BeginVisualDrag(
                button,
                new DataObject(
                    NewContainerDragFormat,
                    kind == LayoutContainerKind.HoverSwitch ? "hover" : "static"),
                DragDropEffects.Copy);
            return;
        }

        BeginVisualDrag(
            button,
            new DataObject(NewWidgetDragFormat, paletteToken),
            DragDropEffects.Copy);
    }

    private static bool TryParseContainerToken(string token, out LayoutContainerKind kind)
    {
        if (token.StartsWith("container:", StringComparison.Ordinal))
        {
            kind = token.EndsWith("edge", StringComparison.Ordinal)
                ? LayoutContainerKind.AutoCollapse
                : token.EndsWith("hover", StringComparison.Ordinal)
                    ? LayoutContainerKind.HoverSwitch
                    : LayoutContainerKind.Static;
            return true;
        }

        kind = default;
        return false;
    }

    private void AddCollapseContainerFromPalette()
    {
        if (!TryApplyProfile(profile => LayoutPlacementService.TryAddCollapse(
                profile,
                LayoutEdge.Top,
                GetUnavailableTaskbarEdge()).Updated))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorAddFailed");
            return;
        }

        LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorAddEdgeContainer");
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
            TryApplyProfile(profile => LayoutOrderingService.TryReorderTopLevel(
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
            BorderThickness = new Thickness(1),
            Opacity = 0.88,
            IsHitTestVisible = false
        };
        SetDynamicResource(ghost, Border.BorderBrushProperty, "LayoutEditorAccentBrush");
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
            HideLayoutDropTarget();
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
        HideLayoutDropTarget();
        var containerEffect = ResolveCenterDropEffect(e.Data);
        e.Effects = containerEffect != DragDropEffects.None
            ? containerEffect
            : e.Data.GetDataPresent(NewWidgetDragFormat)
                ? DragDropEffects.Copy
                : e.Data.GetDataPresent(ExistingWidgetDragFormat)
                    ? DragDropEffects.Move
                    : DragDropEffects.None;
        if (containerEffect != DragDropEffects.None && _layoutPreviewDropOverlay is not null)
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
        if (ResolveCenterDropEffect(e.Data) != DragDropEffects.None)
        {
            if (_layoutPreviewDropOverlay is not null)
            {
                _layoutPreviewDropOverlay.Visibility = Visibility.Visible;
            }
        }
    }

    private void LayoutPreviewDropHost_OnDragLeave(object sender, DragEventArgs e)
    {
        HideLayoutDropTarget();
        if (_layoutPreviewDropOverlay is not null)
        {
            _layoutPreviewDropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void LayoutVisualEditorHost_OnDrop(object sender, DragEventArgs e)
    {
        HideLayoutDropTarget();
        if (_layoutPreviewDropOverlay is not null)
        {
            _layoutPreviewDropOverlay.Visibility = Visibility.Collapsed;
        }
        if (e.Data.GetData(NewWidgetDragFormat) is string paletteToken)
        {
            AddWidgetToTarget(paletteToken, ResolveAddTarget());
        }
        else if (e.Data.GetData(NewContainerDragFormat) is string containerToken)
        {
            if (containerToken == "static")
            {
                AddInlineContainer(LayoutContainerKind.Static);
            }
            else if (containerToken == "hover")
            {
                AddInlineContainer(LayoutContainerKind.HoverSwitch);
            }
        }
        else if (e.Data.GetData(ExistingContainerDragFormat) is string sourceId &&
            ResolveAddTarget() is { } target)
        {
            TryApplyProfile(profile => LayoutOrderingService.TryReorderTopLevel(
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
            !TryApplyProfile(profile => LayoutOrderingService.TryRelocateWidget(
                profile,
                instanceId,
                target.ContainerId,
                target.SlotKind,
                out var updated) ? updated : null))
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
                    new CommandWidgetSettings(
                        (MediaCommandKind)option,
                        CommandWidgetSettings.DefaultButtonSizeDip),
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
                var created = LayoutPlacementService.TryAddContainer(
                    profile,
                    LayoutContainerKind.Static);
                if (!created.Success || created.Updated is not { } createdProfile)
                {
                    return null;
                }

                working = createdProfile;

                destination = new LayoutDropTarget(
                    working.Containers[^1].InstanceId,
                    LayoutSlotKind.Primary);
            }

            return LayoutGridConstraintService.TryAddWidget(
                working,
                destination.ContainerId,
                destination.SlotKind,
                widget).Updated;
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
        if (_hasSkinPreview &&
            !string.Equals(_skinPreviewInstanceId, selection.InstanceId, StringComparison.Ordinal))
        {
            ClearSkinPreview();
        }
        _layoutEditorSelection = selection;
        _layoutEditorViewModel?.SelectNode(selection.InstanceId);
        RefreshLayoutEditor();
    }

    private void LayoutSlotComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 属性面板不再提供“离开时/展开内容”槽位切换按钮；该交互由画布上的悬停标签替代。
    }

    private void RefreshSlotOptions()
    {
        // 槽位切换按钮已从属性面板移除；此方法保留为空，避免改动 RefreshLayoutEditor 的调用点。
    }

    private void LayoutRemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_layoutEditorSelection is null)
        {
            return;
        }

        var id = _layoutEditorSelection.InstanceId;
        if (TryApplyProfile(profile => LayoutGridConstraintService.TryRemove(profile, id).Updated))
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

        TryApplyProfile(profile => LayoutOrderingService.TryMoveSibling(
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
            LayoutCollapseContainer edge => edge.Enabled,
            _ => true
        };
        TryApplyProfile(profile => LayoutGridConstraintService.TrySetEnabled(
            profile,
            selection.InstanceId,
            !enabled).Updated);
    }

    private void LayoutUndoButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_layoutEditorViewModel is null || !_layoutEditorViewModel.UndoCommand.CanExecute(null))
        {
            return;
        }

        _layoutEditorViewModel.UndoCommand.Execute(null);
        var current = _coordinator.Current;
        var profile = _layoutEditorViewModel.Session.Document.Get(_layoutEditorProfileKey);
        // 长度和厚度是窗口级设置；撤销组件拼贴时保留当前比例，避免旧快照让滑块与实际布局不一致。
        // Length and thickness are window-level settings; preserve them while undoing composition so snapshots cannot desynchronize the sliders.
        profile = profile with
        {
            Surface = current.Layout.Get(_layoutEditorProfileKey).Surface
        };
        TryUpdate(() => _coordinator.UpdateLayout(current.Layout.WithProfile(profile)));
    }

    private void LayoutResetProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var current = _coordinator.Current;
        var profile = current.Layout.Get(_layoutEditorProfileKey);
        var defaults = LayoutDefaultTemplates.LoadDocument()
            .Get(_layoutEditorProfileKey);
        if (profile == defaults)
        {
            return;
        }

        if (TryApplyProfile(_ => defaults))
        {
            _layoutEditorSelection = null;
            _layoutEditorViewModel?.SelectNode(null);
        }
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

        if (_layoutEditorViewModel is null ||
            _layoutEditorViewModel.ProfileKey != _layoutEditorProfileKey ||
            _layoutEditorViewModel.Session.Document != current)
        {
            _layoutEditorViewModel?.Dispose();
            _layoutEditorViewModel = CreateLayoutEditorViewModel(current);
            _layoutEditorSession = _layoutEditorViewModel.Session;
        }

        if (!_layoutEditorViewModel.TryApply(document => document.WithProfile(updated)))
        {
            return false;
        }

        return true;
    }

    private LayoutEditorViewModel CreateLayoutEditorViewModel(LayoutDocument document)
    {
        var viewModel = new LayoutEditorViewModel(
            document,
            localize: Loc.Get,
            profileKey: _layoutEditorProfileKey);
        viewModel.DocumentChanged += LayoutEditorViewModel_OnDocumentChanged;
        return viewModel;
    }

    private void LayoutEditorViewModel_OnDocumentChanged(
        object? sender,
        LayoutDocumentChangedEventArgs e)
    {
        if (_coordinator.Current.Layout != e.Current)
        {
            TryUpdate(() => _coordinator.UpdateLayout(e.Current));
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
            LayoutCollapseContainer edge => $"{Loc.Get("Settings.Layout.ContainerAutoCollapse")} · {GetEdgeName(edge.Attachment.AttachmentSide)}",
            _ => Loc.Get("Settings.Layout.EditorNoSelection")
        };
    }

    private static string ResolveLayoutSelectionLabel(object model) => model switch
    {
        LayoutWidgetElement widget => GetWidgetTitle(widget),
        LayoutContainerElement { ContainerKind: LayoutContainerKind.HoverSwitch } =>
            Loc.Get("Settings.Layout.ContainerHoverSwitch"),
        LayoutContainerElement => Loc.Get("Settings.Layout.ContainerStatic"),
        LayoutCollapseContainer edge => $"{Loc.Get("Settings.Layout.ContainerAutoCollapse")} · {GetEdgeName(edge.Attachment.AttachmentSide)}",
        _ => Loc.Get("Settings.Layout.EditorNoSelection")
    };

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
                case LayoutCollapseContainer edge:
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
        AddSkinRow(panel, widget);

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

    private void AddEdgeContainerProperties(StackPanel panel, LayoutCollapseContainer edge)
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

        AddEnumRow(panel, "Settings.Layout.PropertyEdge", edge.Attachment.AttachmentSide,
            Enum.GetValues<LayoutEdge>().ToDictionary(value => value, GetEdgeResourceKey),
            value => UpdateEdgeContainer(edge, value, edge.TriggerThicknessDip, edge.ProximityDip, edge.Animation));
        AddSliderRow(panel, "Settings.Layout.PropertyTriggerThickness", edge.TriggerThicknessDip, 2, 24,
            value => UpdateEdgeContainer(edge, edge.Attachment.AttachmentSide, value, edge.ProximityDip, edge.Animation));
        AddSliderRow(panel, "Settings.Layout.PropertyProximity", edge.ProximityDip, 0, 256,
            value => UpdateEdgeContainer(edge, edge.Attachment.AttachmentSide, edge.TriggerThicknessDip, value, edge.Animation));
        var advanced = new StackPanel();
        AddCheckRow(advanced, "Settings.Layout.PropertyAnimation", edge.Animation.Enabled,
            value => UpdateEdgeContainer(
                edge,
                edge.Attachment.AttachmentSide,
                edge.TriggerThicknessDip,
                edge.ProximityDip,
                edge.Animation with { Enabled = value }));
        AddSliderRow(advanced, "Settings.Layout.PropertyDuration", edge.Animation.DurationMilliseconds, 0, 2_000,
            value => UpdateEdgeContainer(
                edge,
                edge.Attachment.AttachmentSide,
                edge.TriggerThicknessDip,
                edge.ProximityDip,
                edge.Animation with { DurationMilliseconds = value }),
            value => Loc.Get("Settings.Layout.UnitMilliseconds", value));
        AddSliderRow(advanced, "Settings.Layout.PropertyDelay", edge.Animation.DelayMilliseconds, 0, 2_000,
            value => UpdateEdgeContainer(
                edge,
                edge.Attachment.AttachmentSide,
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
                edge.Attachment.AttachmentSide,
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
            var current = LayoutElementQueryService.Find(profile, widget.InstanceId) as LayoutWidgetElement;
            return current is not null && LayoutPropertyEditService.TryUpdateWidgetSettings(
                profile,
                widget.InstanceId,
                update(current.Settings),
                out var updated) ? updated : null;
        });
    }

    private void AddSkinRow(StackPanel panel, LayoutWidgetElement widget)
    {
        var definitions = ComponentSkinCatalog.ForComponent(widget.TypeId)
            .Where(definition => definition.SkinId == ComponentSkinCatalog.DefaultSkinId ||
                widget.Settings is CommandWidgetSettings { Command: MediaCommandKind.PlayPause })
            .ToArray();
        if (definitions.Length == 0)
        {
            return;
        }

        var row = CreatePropertyRow("Settings.Layout.PropertySkin");
        var content = new StackPanel();
        var combo = new ComboBox
        {
            MinWidth = 160,
            ToolTip = Loc.Get("Settings.Layout.PropertySkinHint")
        };
        var current = ComponentSkinCatalog.Normalize(
            widget.TypeId,
            widget.SkinId,
            widget.SkinVersion,
            widget.SkinSettings);
        var selectedIndex = 0;
        foreach (var definition in definitions)
        {
            var item = new ComboBoxItem
            {
                Content = Loc.Get(definition.DisplayNameResourceKey),
                Tag = definition,
                ToolTip = definition.SkinId == ComponentSkinCatalog.DefaultSkinId
                    ? Loc.Get("Settings.Layout.PropertySkinHint")
                    : Loc.Get("Settings.Skin.ExamplePlayPauseName")
            };
            if (string.Equals(current?.SkinId ?? ComponentSkinCatalog.DefaultSkinId,
                definition.SkinId,
                StringComparison.Ordinal))
            {
                selectedIndex = combo.Items.Count;
            }
            combo.Items.Add(item);
        }

        combo.SelectedIndex = selectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (_layoutPropertySyncing ||
                combo.SelectedItem is not ComboBoxItem { Tag: ComponentSkinDefinition selected })
            {
                return;
            }

            var assignment = selected.SkinId == ComponentSkinCatalog.DefaultSkinId
                ? null
                : new ComponentSkinAssignment(selected.SkinId, selected.Version);
            PreviewWidgetSkin(widget, assignment);
        };
        content.Children.Add(combo);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var previewPending = _hasSkinPreview &&
            _skinPreviewProfileKey == _layoutEditorProfileKey &&
            string.Equals(_skinPreviewInstanceId, widget.InstanceId, StringComparison.Ordinal);
        var saveButton = new Button
        {
            Content = Loc.Get("Settings.Layout.PropertySkinSave"),
            Style = TryFindResource("SettingsActionButtonStyle") as Style,
            IsEnabled = previewPending
        };
        saveButton.Click += (_, _) => SaveSkinPreview();
        var cancelButton = new Button
        {
            Content = Loc.Get("Settings.Layout.PropertySkinCancel"),
            Style = TryFindResource("SettingsActionButtonStyle") as Style,
            IsEnabled = previewPending
        };
        cancelButton.Click += (_, _) => CancelSkinPreview();
        actions.Children.Add(saveButton);
        actions.Children.Add(cancelButton);
        content.Children.Add(actions);
        content.Children.Add(new TextBlock
        {
            Text = Loc.Get("Settings.Layout.PropertySkinHint"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
            Style = TryFindResource("SettingsRowDescriptionStyle") as Style
        });
        row.Children.Add(content);
        Grid.SetColumn(content, 1);
        panel.Children.Add(row);
    }

    private void PreviewWidgetSkin(LayoutWidgetElement widget, ComponentSkinAssignment? assignment)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        var persisted = LayoutElementQueryService.Find(
            _coordinator.Current.Layout.Get(_layoutEditorProfileKey),
            widget.InstanceId) as LayoutWidgetElement;
        var current = persisted is null ? null : ComponentSkinService.Normalize(persisted);
        if (current == assignment)
        {
            ClearSkinPreview();
        }
        else
        {
            _hasSkinPreview = true;
            _skinPreviewProfileKey = _layoutEditorProfileKey;
            _skinPreviewInstanceId = widget.InstanceId;
            _skinPreviewAssignment = assignment;
        }
        RefreshLayoutEditor();
    }

    private LayoutProfile ApplySkinPreview(LayoutProfile profile)
    {
        if (!_hasSkinPreview ||
            _skinPreviewProfileKey != _layoutEditorProfileKey ||
            string.IsNullOrWhiteSpace(_skinPreviewInstanceId))
        {
            return profile;
        }

        if (ComponentSkinEditService.TryUpdateWidgetSkin(
            profile,
            _skinPreviewInstanceId,
            _skinPreviewAssignment,
            out var preview))
        {
            return preview;
        }

        ClearSkinPreview();
        return profile;
    }

    private void SaveSkinPreview()
    {
        if (!_hasSkinPreview ||
            _skinPreviewProfileKey != _layoutEditorProfileKey ||
            string.IsNullOrWhiteSpace(_skinPreviewInstanceId))
        {
            return;
        }

        var instanceId = _skinPreviewInstanceId;
        var assignment = _skinPreviewAssignment;
        ClearSkinPreview();
        if (!TryApplyProfile(profile => ComponentSkinEditService.TryUpdateWidgetSkin(
            profile,
            instanceId,
            assignment,
            out var updated) ? updated : null))
        {
            RefreshLayoutEditor();
        }
    }

    private void CancelSkinPreview()
    {
        ClearSkinPreview();
        RefreshLayoutEditor();
    }

    private void ClearSkinPreview()
    {
        _hasSkinPreview = false;
        _skinPreviewInstanceId = null;
        _skinPreviewAssignment = null;
    }

    private void UpdateGeometry(LayoutElement element, Func<LayoutGeometry, LayoutGeometry> update)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        TryApplyProfile(profile => LayoutPropertyEditService.TryUpdateGeometry(
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

        TryApplyProfile(profile => LayoutPropertyEditService.TryUpdateContainer(
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

        TryApplyProfile(profile => LayoutPropertyEditService.TryResetContainer(
            profile,
            container.InstanceId,
            out var updated) ? updated : null);
    }

    private void ResetEdgeContainerProperties(LayoutCollapseContainer container)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        TryApplyProfile(profile => LayoutPropertyEditService.TryResetCollapse(
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

        TryApplyProfile(profile => LayoutPropertyEditService.TryResetWidgetProperties(
            profile,
            widget.InstanceId,
            out var updated) ? updated : null);
    }

    private void UpdateEdgeContainer(
        LayoutCollapseContainer container,
        LayoutEdge edge,
        int triggerThicknessDip,
        int proximityDip,
        LayoutAnimationSettings animation)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        if (!TryApplyProfile(profile => LayoutPropertyEditService.TryUpdateCollapse(
                profile,
                container.InstanceId,
                edge,
                GetUnavailableTaskbarEdge(),
                triggerThicknessDip,
                proximityDip,
                animation,
                out var updated) ? updated : null))
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
            MinWidth = 160
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
        return profile.Containers.FirstOrDefault() is { } first
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
        foreach (var container in profile.Containers)
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

        foreach (var edge in profile.CollapseContainers)
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
        LayoutRemoveButton.IsEnabled = hasSelection;
        LayoutUndoButton.IsEnabled = _layoutEditorViewModel?.CanUndo == true;
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

    private static void SetDynamicResource(
        FrameworkElement element,
        DependencyProperty property,
        string resourceKey) =>
        element.SetResourceReference(property, resourceKey);

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

    private static string GetComponentCategoryResourceKey(ComponentCategory category) => category switch
    {
        ComponentCategory.Media => "Settings.Layout.CategoryMedia",
        ComponentCategory.Controls => "Settings.Layout.CategoryControls",
        ComponentCategory.Audio => "Settings.Layout.CategoryAudio",
        ComponentCategory.System => "Settings.Layout.CategorySystem",
        _ => "Settings.Layout.CategoryLayout"
    };

    private sealed class LayoutDropTargetAdorner(UIElement adornedElement) : Adorner(adornedElement)
    {
        private static readonly Brush Fill = new SolidColorBrush(Color.FromArgb(46, 86, 156, 255));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(86, 156, 255));
        private static readonly Pen Outline = new(Accent, 2);

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var width = AdornedElement.RenderSize.Width;
            var height = AdornedElement.RenderSize.Height;
            var bounds = new Rect(1, 1, Math.Max(0, width - 2), Math.Max(0, height - 2));
            drawingContext.DrawRoundedRectangle(Fill, Outline, bounds, 4, 4);
            if (width >= height)
            {
                drawingContext.DrawRoundedRectangle(
                    Accent,
                    null,
                    new Rect(Math.Max(1, width - 5), 5, 3, Math.Max(0, height - 10)),
                    1.5,
                    1.5);
            }
            else
            {
                drawingContext.DrawRoundedRectangle(
                    Accent,
                    null,
                    new Rect(5, Math.Max(1, height - 5), Math.Max(0, width - 10), 3),
                    1.5,
                    1.5);
            }
        }
    }

    private enum LayoutEditorNodeKind
    {
        InlineContainer,
        EdgeContainer,
        Widget
    }

    private sealed record LayoutDropTarget(string ContainerId, LayoutSlotKind SlotKind);

    private sealed record PaletteEntry(
        string Token,
        string Label,
        string Description,
        ComponentCategory Category);

    private sealed record LayoutEditorSelection(
        string InstanceId,
        LayoutEditorNodeKind Kind,
        string? ParentContainerId,
        LayoutSlotKind SlotKind,
        object Model);
}
