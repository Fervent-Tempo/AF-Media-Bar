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

        LayoutComponentPalette.Children.Clear();
        foreach (var definition in ComponentCatalog.All)
        {
            var button = new Button
            {
                Content = Loc.Get(definition.NameResourceKey),
                Tag = definition.TypeId,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 5, 10, 5),
                Cursor = Cursors.Hand,
                Style = TryFindResource("SettingsActionButtonStyle") as Style,
                ToolTip = Loc.Get(definition.DescriptionResourceKey)
            };
            button.PreviewMouseLeftButtonDown += LayoutDragSource_OnPreviewMouseLeftButtonDown;
            button.PreviewMouseMove += LayoutPaletteButton_OnPreviewMouseMove;
            button.Click += LayoutPaletteButton_OnClick;
            LayoutComponentPalette.Children.Add(button);
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
        LayoutVisualEditorHost.Child = BuildVisualEditor(profile);
        RefreshSlotOptions();
        RefreshSelectionText();
        RefreshLayoutProperties();
        UpdateLayoutEditorButtons();
        LayoutEditorMessageText.Text = string.Empty;
    }

    private FrameworkElement BuildVisualEditor(LayoutProfile profile)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });

        AddEdgeArea(grid, profile, LayoutEdge.Top, 0, 1, Orientation.Horizontal);
        AddEdgeArea(grid, profile, LayoutEdge.Left, 1, 0, Orientation.Vertical);
        AddEdgeArea(grid, profile, LayoutEdge.Right, 1, 2, Orientation.Vertical);
        AddEdgeArea(grid, profile, LayoutEdge.Bottom, 2, 1, Orientation.Horizontal);

        var stripPanel = new StackPanel
        {
            Orientation = profile.LayoutMode == PlayerLayoutMode.Vertical
                ? Orientation.Vertical
                : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var container in profile.InlineContainers)
        {
            stripPanel.Children.Add(BuildInlineContainerCard(profile, container));
        }

        if (profile.InlineContainers.Count == 0)
        {
            stripPanel.Children.Add(CreateEmptyHint("Settings.Layout.EditorEmptyStrip"));
        }

        var strip = new Border
        {
            Padding = new Thickness(7),
            MinWidth = 260,
            MinHeight = 72,
            Background = FindBrush("TaskbarReadabilityBrush", Brushes.DimGray),
            BorderBrush = FindBrush("MenuBorderBrush", Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = stripPanel
        };
        Grid.SetRow(strip, 1);
        Grid.SetColumn(strip, 1);
        grid.Children.Add(strip);
        return grid;
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
        e.Effects = e.Data.GetDataPresent(ExistingContainerDragFormat) &&
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
        if (sender is Button { Tag: string typeId })
        {
            AddWidgetToTarget(typeId, ResolveAddTarget());
        }
    }

    private void LayoutDragSource_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _layoutDragStart = e.GetPosition(this);
    }

    private void LayoutPaletteButton_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button { Tag: string typeId } button || !ShouldBeginDrag(e))
        {
            return;
        }

        DragDrop.DoDragDrop(button, new DataObject(NewWidgetDragFormat, typeId), DragDropEffects.Copy);
    }

    private void LayoutWidgetTile_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button { Tag: LayoutEditorSelection selection } button || !ShouldBeginDrag(e))
        {
            return;
        }

        DragDrop.DoDragDrop(
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

        DragDrop.DoDragDrop(
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
            : e.Data.GetDataPresent(ExistingContainerDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void LayoutVisualEditorHost_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(NewWidgetDragFormat) is string typeId)
        {
            AddWidgetToTarget(typeId, ResolveAddTarget());
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
        e.Handled = true;
    }

    private void ApplyDrop(DragEventArgs e, LayoutDropTarget target)
    {
        if (e.Data.GetData(NewWidgetDragFormat) is string typeId)
        {
            AddWidgetToTarget(typeId, target);
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

    private void AddWidgetToTarget(string typeId, LayoutDropTarget? target)
    {
        var widget = new LayoutWidgetElement(
            $"widget-{Guid.NewGuid():N}",
            true,
            LayoutGeometry.Auto,
            typeId,
            ComponentCatalog.CreateDefaultSettings(typeId));
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
                            [MediaTextKind.Source] = "Settings.Layout.PropertyTextSource"
                        },
                        value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { TextKind = value }));
                }
                AddSliderRow(panel, "Settings.Layout.PropertyFontSize", text.FontSizeDip, 6, 72,
                    value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { FontSizeDip = value }));
                AddCheckRow(panel, "Settings.Layout.PropertyMarquee", text.EnableMarquee,
                    value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { EnableMarquee = value }));
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
        if (container.ContainerKind != LayoutContainerKind.HoverSwitch)
        {
            panel.Children.Add(CreateEmptyHint("Settings.Layout.EditorStaticFollowsProfile"));
            return;
        }

        AddCheckRow(panel, "Settings.Layout.PropertyAnimation", container.Animation.Enabled,
            value => UpdateInlineContainer(
                container,
                container.ProximityDip,
                container.Animation with { Enabled = value }));
        AddSliderRow(panel, "Settings.Layout.PropertyDuration", container.Animation.DurationMilliseconds, 0, 2_000,
            value => UpdateInlineContainer(
                container,
                container.ProximityDip,
                container.Animation with { DurationMilliseconds = value }),
            value => Loc.Get("Settings.Layout.UnitMilliseconds", value));
    }

    private void AddEdgeContainerProperties(StackPanel panel, LayoutEdgeContainer edge)
    {
        AddEnumRow(panel, "Settings.Layout.PropertyEdge", edge.Edge,
            Enum.GetValues<LayoutEdge>().ToDictionary(value => value, GetEdgeResourceKey),
            value => UpdateEdgeContainer(edge, value, edge.OffsetDip, edge.TriggerThicknessDip, edge.ProximityDip, edge.Animation));
        AddSliderRow(panel, "Settings.Layout.PropertyEdgeOffset", edge.OffsetDip, -500, 500,
            value => UpdateEdgeContainer(edge, edge.Edge, value, edge.TriggerThicknessDip, edge.ProximityDip, edge.Animation));
        AddSliderRow(panel, "Settings.Layout.PropertyTriggerThickness", edge.TriggerThicknessDip, 2, 24,
            value => UpdateEdgeContainer(edge, edge.Edge, edge.OffsetDip, value, edge.ProximityDip, edge.Animation));
        AddCheckRow(panel, "Settings.Layout.PropertyAnimation", edge.Animation.Enabled,
            value => UpdateEdgeContainer(
                edge,
                edge.Edge,
                edge.OffsetDip,
                edge.TriggerThicknessDip,
                edge.ProximityDip,
                edge.Animation with { Enabled = value }));
        AddSliderRow(panel, "Settings.Layout.PropertyDuration", edge.Animation.DurationMilliseconds, 0, 2_000,
            value => UpdateEdgeContainer(
                edge,
                edge.Edge,
                edge.OffsetDip,
                edge.TriggerThicknessDip,
                edge.ProximityDip,
                edge.Animation with { DurationMilliseconds = value }),
            value => Loc.Get("Settings.Layout.UnitMilliseconds", value));
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

    private void UpdateGeometry(LayoutWidgetElement widget, Func<LayoutGeometry, LayoutGeometry> update)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        TryApplyProfile(profile => LayoutEditorService.TryUpdateGeometry(
            profile,
            widget.InstanceId,
            update(widget.Geometry ?? LayoutGeometry.Auto),
            out var updated) ? updated : null);
    }

    private void UpdateInlineContainer(
        LayoutContainerElement container,
        int proximityDip,
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
            animation,
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
        var slider = new Slider
        {
            Width = 150,
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = Math.Max(1, (maximum - minimum) / 10),
            Value = Math.Clamp(value, minimum, maximum)
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
        row.Children.Add(slider);
        row.Children.Add(input);
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
            Width = 176,
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
        panel.Children.Add(row);
    }

    private StackPanel CreatePropertyRow(string labelKey)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(new TextBlock
        {
            Width = 170,
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
            if (ResolveWidgetSelection(container.PrimarySlot, container.InstanceId, LayoutSlotKind.Primary, instanceId) is { } primary)
            {
                return primary;
            }
            if (ResolveWidgetSelection(container.SecondarySlot, container.InstanceId, LayoutSlotKind.Secondary, instanceId) is { } secondary)
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
            if (ResolveWidgetSelection(edge.ExpandedSlot, edge.InstanceId, LayoutSlotKind.Expanded, instanceId) is { } widget)
            {
                return widget;
            }
        }

        return null;
    }

    private static LayoutEditorSelection? ResolveWidgetSelection(
        LayoutSlot slot,
        string parentId,
        LayoutSlotKind slotKind,
        string instanceId)
    {
        var widget = slot.Children.OfType<LayoutWidgetElement>()
            .FirstOrDefault(item => item.InstanceId == instanceId);
        return widget is null
            ? null
            : new LayoutEditorSelection(
                instanceId,
                LayoutEditorNodeKind.Widget,
                parentId,
                slotKind,
                widget);
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
                TaskbarEdgeService.TryResolveCurrent() is LayoutEdge.Left or LayoutEdge.Right,
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
        return ComponentCatalog.TryGet(widget.TypeId, out var definition)
            ? Loc.Get(definition.NameResourceKey)
            : widget.TypeId;
    }

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

    private sealed record LayoutEditorSelection(
        string InstanceId,
        LayoutEditorNodeKind Kind,
        string? ParentContainerId,
        LayoutSlotKind SlotKind,
        object Model);
}
