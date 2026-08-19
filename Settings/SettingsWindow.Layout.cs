using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AFMediaBar.Controls;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Settings;

/// <summary>
/// 负责四套布局档案的树形编辑、预览和即时提交；不直接处理注册表或系统 API。
/// Owns tree editing, preview, and immediate submission for four layout profiles without touching registry or system APIs.
/// </summary>
public partial class SettingsWindow
{
    private LayoutProfileKey _layoutEditorProfileKey = LayoutProfileKey.TaskbarHorizontal;
    private LayoutEditorNode? _layoutEditorSelection;
    private ComponentLayoutSurface? _layoutEditorPreview;
    private bool _layoutEditorSyncing;
    private bool _layoutPropertySyncing;

    private void InitializeLayoutEditor()
    {
        _layoutEditorPreview = new ComponentLayoutSurface();
        LayoutEditorPreviewHost.Child = _layoutEditorPreview;
        PopulateLayoutEditorOptions();
        _layoutEditorProfileKey = ResolveInitialLayoutProfile();
        LayoutProfileComboBox.SelectedIndex = (int)_layoutEditorProfileKey;
        LayoutSlotComboBox.SelectedIndex = 0;
        LayoutWidgetTypeComboBox.SelectedIndex = 0;
        RefreshLayoutEditor();
    }

    private void SyncLayoutEditor()
    {
        if (!_isInitialized || _layoutEditorPreview is null)
        {
            return;
        }

        PopulateLayoutEditorOptions();
        RefreshLayoutEditor();
    }

    private void PopulateLayoutEditorOptions()
    {
        _layoutEditorSyncing = true;
        try
        {
            var selectedProfileIndex = Math.Clamp((int)_layoutEditorProfileKey, 0, 3);
            LayoutProfileComboBox.Items.Clear();
            AddProfileOption(LayoutProfileKey.TaskbarHorizontal, "Settings.Layout.ProfileTaskbarHorizontal");
            AddProfileOption(LayoutProfileKey.TaskbarVertical, "Settings.Layout.ProfileTaskbarVertical");
            AddProfileOption(LayoutProfileKey.FloatingHorizontal, "Settings.Layout.ProfileFloatingHorizontal");
            AddProfileOption(LayoutProfileKey.FloatingVertical, "Settings.Layout.ProfileFloatingVertical");
            LayoutProfileComboBox.SelectedIndex = selectedProfileIndex;

            LayoutSlotComboBox.Items.Clear();
            AddSlotOption(LayoutSlotKind.Primary, "Settings.Layout.EditorPrimarySlot");
            AddSlotOption(LayoutSlotKind.Secondary, "Settings.Layout.EditorSecondarySlot");
            AddSlotOption(LayoutSlotKind.Collapsed, "Settings.Layout.EditorCollapsedSlot");

            var selectedTypeId = (LayoutWidgetTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            LayoutWidgetTypeComboBox.Items.Clear();
            foreach (var definition in ComponentCatalog.All)
            {
                LayoutWidgetTypeComboBox.Items.Add(new ComboBoxItem
                {
                    Content = Loc.Get(definition.NameResourceKey),
                    Tag = definition.TypeId
                });
            }

            var selectedTypeIndex = ComponentCatalog.All
                .Select((definition, index) => (definition.TypeId, index))
                .FirstOrDefault(item => item.TypeId == selectedTypeId)
                .index;
            LayoutWidgetTypeComboBox.SelectedIndex = selectedTypeId is null
                ? 0
                : Math.Max(0, selectedTypeIndex);
        }
        finally
        {
            _layoutEditorSyncing = false;
        }
    }

    private void RefreshLayoutEditor()
    {
        if (_layoutEditorSyncing || !_isInitialized)
        {
            return;
        }

        var profile = _coordinator.Current.Layout.Get(_layoutEditorProfileKey);
        var selectedId = _layoutEditorSelection?.InstanceId;
        _layoutEditorSelection = null;
        LayoutTree.Items.Clear();
        var rootItem = BuildTreeItem(
            profile.Root,
            parentContainerId: null,
            slotKind: LayoutSlotKind.Primary,
            isSlot: false);
        LayoutTree.Items.Add(rootItem);
        if (!string.IsNullOrWhiteSpace(selectedId) && FindTreeItem(rootItem, selectedId) is { } selectedItem)
        {
            selectedItem.IsSelected = true;
        }
        else
        {
            LayoutEditorSelectionText.Text = Loc.Get("Settings.Layout.EditorNoSelection");
        }
        LayoutEditorMessageText.Text = string.Empty;
        _layoutEditorPreview?.Apply(profile, pointerNear: false);
        RefreshLayoutProperties();
        UpdateLayoutEditorButtons();
    }

    private static TreeViewItem? FindTreeItem(TreeViewItem item, string instanceId)
    {
        if (item.Tag is LayoutEditorNode node &&
            string.Equals(node.InstanceId, instanceId, StringComparison.Ordinal))
        {
            return item;
        }

        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            if (FindTreeItem(child, instanceId) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private TreeViewItem BuildTreeItem(
        LayoutElement element,
        string? parentContainerId,
        LayoutSlotKind slotKind,
        bool isSlot)
    {
        var node = new LayoutEditorNode(
            element.InstanceId,
            element is LayoutContainerElement,
            isSlot,
            parentContainerId,
            slotKind,
            element);
        var item = new TreeViewItem
        {
            Header = GetElementTitle(element),
            Tag = node,
            IsExpanded = true
        };

        if (element is LayoutContainerElement container)
        {
            AddSlotTreeItem(item, container, LayoutSlotKind.Primary, container.PrimarySlot);
            AddSlotTreeItem(item, container, LayoutSlotKind.Secondary, container.SecondarySlot);
            AddSlotTreeItem(item, container, LayoutSlotKind.Collapsed, container.CollapsedSlot);
        }

        return item;
    }

    private void AddSlotTreeItem(
        TreeViewItem parent,
        LayoutContainerElement container,
        LayoutSlotKind slotKind,
        LayoutSlot slot)
    {
        var slotNode = new LayoutEditorNode(
            $"{container.InstanceId}:{slotKind}",
            true,
            true,
            container.InstanceId,
            slotKind,
            null);
        var slotItem = new TreeViewItem
        {
            Header = GetSlotTitle(slotKind),
            Tag = slotNode,
            IsExpanded = true
        };
        foreach (var child in slot.Children)
        {
            slotItem.Items.Add(BuildTreeItem(
                child,
                container.InstanceId,
                slotKind,
                isSlot: false));
        }

        parent.Items.Add(slotItem);
    }

    private void LayoutProfileComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_layoutEditorSyncing ||
            LayoutProfileComboBox.SelectedItem is not ComboBoxItem { Tag: LayoutProfileKey key })
        {
            return;
        }

        _layoutEditorProfileKey = key;
        RefreshLayoutEditor();
    }

    private void LayoutTree_OnSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        _layoutEditorSelection = (e.NewValue as TreeViewItem)?.Tag as LayoutEditorNode;
        if (_layoutEditorSelection?.Element is { } element)
        {
            LayoutEditorSelectionText.Text = GetElementTitle(element);
        }
        else
        {
            LayoutEditorSelectionText.Text = Loc.Get("Settings.Layout.EditorNoSelection");
        }

        RefreshLayoutProperties();
        UpdateLayoutEditorButtons();
    }

    private void LayoutAddWidgetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (LayoutWidgetTypeComboBox.SelectedItem is not ComboBoxItem { Tag: string typeId })
        {
            return;
        }

        var target = ResolveAddTarget();
        var widget = new LayoutWidgetElement(
            $"widget-{Guid.NewGuid():N}",
            true,
            LayoutGeometry.Auto,
            typeId,
            ComponentCatalog.CreateDefaultSettings(typeId));
        if (!TryApplyProfile(profile => LayoutEditorService.TryAdd(
                profile,
                target.ContainerId,
                target.SlotKind,
                widget,
                out var updated) ? updated : null))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorAddFailed");
        }
    }

    private void LayoutAddContainerButton_OnClick(object sender, RoutedEventArgs e)
    {
        var target = ResolveAddTarget();
        var container = new LayoutContainerElement(
            $"container-{Guid.NewGuid():N}",
            true,
            LayoutGeometry.Auto,
            LayoutContainerKind.Static,
            LayoutFlowOrientation.Automatic,
            LayoutTriggerMode.Always,
            0,
            new LayoutAnimationSettings(false, 0, 0, LayoutEasingKind.Linear),
            LayoutSlot.Empty("content"),
            LayoutSlot.Empty("secondary"),
            LayoutSlot.Empty("collapsed"));
        if (!TryApplyProfile(profile => LayoutEditorService.TryAdd(
                profile,
                target.ContainerId,
                target.SlotKind,
                container,
                out var updated) ? updated : null))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorAddFailed");
        }
    }

    private void LayoutRemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_layoutEditorSelection is not { IsSlot: false, Element: { } element } ||
            string.Equals(element.InstanceId, "root", StringComparison.Ordinal))
        {
            return;
        }

        if (!TryApplyProfile(profile => LayoutEditorService.TryRemove(
                profile,
                element.InstanceId,
                out var updated) ? updated : null))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorRemoveFailed");
        }
    }

    private void LayoutMoveUpButton_OnClick(object sender, RoutedEventArgs e)
    {
        TryMoveSelected(-1);
    }

    private void LayoutMoveDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        TryMoveSelected(1);
    }

    private void LayoutToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_layoutEditorSelection is not { IsSlot: false, Element: { } element })
        {
            return;
        }

        TryApplyProfile(profile => LayoutEditorService.TrySetEnabled(
            profile,
            element.InstanceId,
            !element.Enabled,
            out var updated) ? updated : null);
    }

    private void TryMoveSelected(int offset)
    {
        if (_layoutEditorSelection is not { IsSlot: false, Element: { } element })
        {
            return;
        }

        TryApplyProfile(profile => LayoutEditorService.TryMove(
            profile,
            element.InstanceId,
            offset,
            out var updated) ? updated : null);
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

        TryUpdate(() => _coordinator.UpdateLayout(current.WithProfile(updated)));
        return true;
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
                Text = Loc.Get("Settings.Layout.EditorProperties"),
                FontWeight = FontWeights.SemiBold
            });

            var selected = _layoutEditorSelection?.Element;
            if (selected is null)
            {
                panel.Children.Add(new TextBlock
                {
                    Margin = new Thickness(0, 4, 0, 0),
                    Text = Loc.Get("Settings.Layout.EditorNoSelection"),
                    Style = TryFindResource("SettingsRowDescriptionStyle") as Style
                });
                LayoutPropertyHost.Child = panel;
                return;
            }

            AddGeometryProperties(panel, selected);
            switch (selected)
            {
                case LayoutWidgetElement widget:
                    AddWidgetProperties(panel, widget);
                    break;
                case LayoutContainerElement container:
                    AddContainerProperties(panel, container);
                    break;
            }

            LayoutPropertyHost.Child = new ScrollViewer
            {
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };
        }
        finally
        {
            _layoutPropertySyncing = false;
        }
    }

    private void AddGeometryProperties(StackPanel panel, LayoutElement element)
    {
        panel.Children.Add(CreateSectionHeader("Settings.Layout.PropertyGeometry"));
        var geometry = element.Geometry ?? LayoutGeometry.Auto;
        AddNullableNumericRow(panel, "Settings.Layout.PropertyWidth", geometry.WidthDip, 1, 2_000,
            value => UpdateGeometry(element, current => current with { WidthDip = value }));
        AddNullableNumericRow(panel, "Settings.Layout.PropertyHeight", geometry.HeightDip, 1, 2_000,
            value => UpdateGeometry(element, current => current with { HeightDip = value }));
        AddNullableNumericRow(panel, "Settings.Layout.PropertyMinWidth", geometry.MinWidthDip, 0, 2_000,
            value => UpdateGeometry(element, current => current with { MinWidthDip = value }));
        AddNullableNumericRow(panel, "Settings.Layout.PropertyMaxWidth", geometry.MaxWidthDip, 1, 2_000,
            value => UpdateGeometry(element, current => current with { MaxWidthDip = value }));
        AddNullableNumericRow(panel, "Settings.Layout.PropertyMinHeight", geometry.MinHeightDip, 0, 2_000,
            value => UpdateGeometry(element, current => current with { MinHeightDip = value }));
        AddNullableNumericRow(panel, "Settings.Layout.PropertyMaxHeight", geometry.MaxHeightDip, 1, 2_000,
            value => UpdateGeometry(element, current => current with { MaxHeightDip = value }));
        var margin = geometry.Margin ?? LayoutThickness.Zero;
        AddIntegerRow(panel, "Settings.Layout.PropertyMarginLeft", margin.Left, -256, 256,
            value => UpdateGeometry(element, current => current with { Margin = (current.Margin ?? LayoutThickness.Zero) with { Left = value } }));
        AddIntegerRow(panel, "Settings.Layout.PropertyMarginTop", margin.Top, -256, 256,
            value => UpdateGeometry(element, current => current with { Margin = (current.Margin ?? LayoutThickness.Zero) with { Top = value } }));
        AddIntegerRow(panel, "Settings.Layout.PropertyMarginRight", margin.Right, -256, 256,
            value => UpdateGeometry(element, current => current with { Margin = (current.Margin ?? LayoutThickness.Zero) with { Right = value } }));
        AddIntegerRow(panel, "Settings.Layout.PropertyMarginBottom", margin.Bottom, -256, 256,
            value => UpdateGeometry(element, current => current with { Margin = (current.Margin ?? LayoutThickness.Zero) with { Bottom = value } }));
    }

    private void AddWidgetProperties(StackPanel panel, LayoutWidgetElement widget)
    {
        switch (widget.Settings)
        {
            case ArtworkWidgetSettings artwork:
                AddSliderRow(panel, "Settings.Layout.PropertyArtworkRadius", artwork.CornerRadiusDip, 0, 32,
                    value => UpdateWidget(widget, current => ((ArtworkWidgetSettings)current) with { CornerRadiusDip = value }));
                AddCheckRow(panel, "Settings.Layout.PropertyArtworkColor", artwork.UseMediaPrimaryColor,
                    value => UpdateWidget(widget, current => ((ArtworkWidgetSettings)current) with { UseMediaPrimaryColor = value }));
                break;
            case MediaTextWidgetSettings text:
                AddEnumRow(panel, "Settings.Layout.PropertyTextKind", text.TextKind,
                    new Dictionary<MediaTextKind, string>
                    {
                        [MediaTextKind.Title] = "Settings.Layout.PropertyTextTitle",
                        [MediaTextKind.Artist] = "Settings.Layout.PropertyTextArtist",
                        [MediaTextKind.Source] = "Settings.Layout.PropertyTextSource"
                    },
                    value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { TextKind = value }));
                AddCheckRow(panel, "Settings.Layout.PropertyMarquee", text.EnableMarquee,
                    value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { EnableMarquee = value }));
                AddSliderRow(panel, "Settings.Layout.PropertyFontSize", text.FontSizeDip, 6, 72,
                    value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { FontSizeDip = value }));
                AddSliderRow(panel, "Settings.Layout.PropertyMaxLines", text.MaxLines, 1, 8,
                    value => UpdateWidget(widget, current => ((MediaTextWidgetSettings)current) with { MaxLines = value }));
                break;
            case CommandWidgetSettings command:
                AddEnumRow(panel, "Settings.Layout.PropertyCommand", command.Command,
                    Enum.GetValues<MediaCommandKind>().ToDictionary(value => value, value => GetCommandOptionKey(value)),
                    value => UpdateWidget(widget, current => ((CommandWidgetSettings)current) with { Command = value }));
                AddSliderRow(panel, "Settings.Layout.PropertyButtonSize", command.ButtonSizeDip, 20, 96,
                    value => UpdateWidget(widget, current => ((CommandWidgetSettings)current) with { ButtonSizeDip = value }));
                break;
            case MetricsWidgetSettings metrics:
                AddEnumRow(panel, "Settings.Layout.PropertyMetric", metrics.Metric,
                    Enum.GetValues<MetricKind>().ToDictionary(value => value, value => GetMetricOptionKey(value)),
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
                AddSliderRow(panel, "Settings.Layout.PropertyBandCount", spectrum.BandCount, 1, 32,
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

    private void AddContainerProperties(StackPanel panel, LayoutContainerElement container)
    {
        AddEnumRow(panel, "Settings.Layout.PropertyContainerKind", container.ContainerKind,
            new Dictionary<LayoutContainerKind, string>
            {
                [LayoutContainerKind.Static] = "Settings.Layout.OptionStatic",
                [LayoutContainerKind.HoverSwitch] = "Settings.Layout.OptionHoverSwitch",
                [LayoutContainerKind.AutoCollapse] = "Settings.Layout.OptionAutoCollapse"
            },
            value => UpdateContainer(container, current => current with { ContainerKind = value }));
        AddEnumRow(panel, "Settings.Layout.PropertyOrientation", container.Orientation,
            new Dictionary<LayoutFlowOrientation, string>
            {
                [LayoutFlowOrientation.Automatic] = "Settings.Layout.OptionAutomatic",
                [LayoutFlowOrientation.Horizontal] = "Settings.Layout.OptionHorizontal",
                [LayoutFlowOrientation.Vertical] = "Settings.Layout.OptionVertical"
            },
            value => UpdateContainer(container, current => current with { Orientation = value }));
        AddEnumRow(panel, "Settings.Layout.PropertyTrigger", container.Trigger,
            new Dictionary<LayoutTriggerMode, string>
            {
                [LayoutTriggerMode.Always] = "Settings.Layout.OptionAlways",
                [LayoutTriggerMode.PointerNear] = "Settings.Layout.OptionPointerNear",
                [LayoutTriggerMode.EdgeNear] = "Settings.Layout.OptionEdgeNear"
            },
            value => UpdateContainer(container, current => current with { Trigger = value }));
        AddSliderRow(panel, "Settings.Layout.PropertyProximity", container.ProximityDip, 0, 256,
            value => UpdateContainer(container, current => current with { ProximityDip = value }));
        AddCheckRow(panel, "Settings.Layout.PropertyAnimation", container.Animation.Enabled,
            value => UpdateContainer(container, current => current with { Animation = (current.Animation ?? LayoutAnimationSettings.Default) with { Enabled = value } }));
        AddSliderRow(panel, "Settings.Layout.PropertyDuration", container.Animation.DurationMilliseconds, 0, 2_000,
            value => UpdateContainer(container, current => current with { Animation = (current.Animation ?? LayoutAnimationSettings.Default) with { DurationMilliseconds = value } }),
            value => Loc.Get("Settings.Layout.UnitMilliseconds", value));
        AddEnumRow(panel, "Settings.Layout.PropertyEasing", container.Animation.Easing,
            new Dictionary<LayoutEasingKind, string>
            {
                [LayoutEasingKind.Linear] = "Settings.Layout.OptionLinear",
                [LayoutEasingKind.EaseOut] = "Settings.Layout.OptionEaseOut",
                [LayoutEasingKind.EaseInOut] = "Settings.Layout.OptionEaseInOut"
            },
            value => UpdateContainer(container, current => current with { Animation = (current.Animation ?? LayoutAnimationSettings.Default) with { Easing = value } }));
    }

    private void UpdateGeometry(LayoutElement element, Func<LayoutGeometry, LayoutGeometry> update)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        if (!TryApplyProfile(profile =>
        {
            var current = LayoutEditorService.Find(profile.Root, element.InstanceId);
            return current is null
                ? null
                : LayoutEditorService.TryUpdateGeometry(
                    profile,
                    element.InstanceId,
                    update(current.Geometry ?? LayoutGeometry.Auto),
                    out var updated)
                    ? updated
                    : null;
        }))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorUpdateFailed");
        }
    }

    private void UpdateWidget(LayoutWidgetElement widget, Func<WidgetSettings, WidgetSettings> update)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        if (!TryApplyProfile(profile =>
        {
            var current = LayoutEditorService.Find(profile.Root, widget.InstanceId) as LayoutWidgetElement;
            return current is null
                ? null
                : LayoutEditorService.TryUpdateWidgetSettings(
                    profile,
                    widget.InstanceId,
                    update(current.Settings),
                    out var updated)
                    ? updated
                    : null;
        }))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorUpdateFailed");
        }
    }

    private void UpdateContainer(LayoutContainerElement container, Func<LayoutContainerElement, LayoutContainerElement> update)
    {
        if (_layoutPropertySyncing)
        {
            return;
        }

        if (!TryApplyProfile(profile =>
        {
            var current = LayoutEditorService.Find(profile.Root, container.InstanceId) as LayoutContainerElement;
            if (current is null)
            {
                return null;
            }

            var next = update(current);
            return LayoutEditorService.TryUpdateContainerSettings(
                profile,
                container.InstanceId,
                next.ContainerKind,
                next.Orientation,
                next.Trigger,
                next.ProximityDip,
                next.Animation,
                out var updated)
                ? updated
                : null;
        }))
        {
            LayoutEditorMessageText.Text = Loc.Get("Settings.Layout.EditorUpdateFailed");
        }
    }

    private TextBlock CreateSectionHeader(string resourceKey)
    {
        return new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 4),
            Text = Loc.Get(resourceKey),
            Style = TryFindResource("SettingsRowDescriptionStyle") as Style
        };
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
            Width = 74,
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

    private void AddIntegerRow(
        Panel panel,
        string labelKey,
        int value,
        int minimum,
        int maximum,
        Action<int> update)
    {
        AddSliderRow(panel, labelKey, value, minimum, maximum, update, value => value.ToString(CultureInfo.InvariantCulture));
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
            Width = 130,
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = Math.Max(1, (maximum - minimum) / 10),
            IsSnapToTickEnabled = false,
            Value = Math.Clamp(value, minimum, maximum)
        };
        var input = new TextBox
        {
            Width = 74,
            Margin = new Thickness(6, 0, 0, 0),
            Text = (format ?? (current => current.ToString(CultureInfo.InvariantCulture)))(value)
        };
        var syncing = false;
        slider.ValueChanged += (_, _) =>
        {
            if (syncing || _layoutPropertySyncing)
            {
                return;
            }

            var next = Math.Clamp((int)Math.Round(slider.Value), minimum, maximum);
            input.Text = (format ?? (current => current.ToString(CultureInfo.InvariantCulture)))(next);
        };
        void CommitSlider()
        {
            var next = Math.Clamp((int)Math.Round(slider.Value), minimum, maximum);
            update(next);
        }

        slider.AddHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) => CommitSlider()));
        slider.PreviewMouseLeftButtonUp += (_, _) => CommitSlider();
        slider.KeyUp += (_, _) => CommitSlider();
        void Commit()
        {
            if (!TryParseNumericInput(input.Text, out var parsed))
            {
                input.Text = (format ?? (current => current.ToString(CultureInfo.InvariantCulture)))(value);
                return;
            }

            parsed = Math.Clamp(parsed, minimum, maximum);
            syncing = true;
            slider.Value = parsed;
            syncing = false;
            input.Text = (format ?? (current => current.ToString(CultureInfo.InvariantCulture)))(parsed);
            update(parsed);
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
            Width = 168,
            Style = TryFindResource("SettingsComboBoxStyle") as Style
        };
        foreach (var pair in labels)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = Loc.Get(pair.Value),
                Tag = pair.Key
            });
        }

        combo.SelectedIndex = Math.Max(0, labels.Keys.ToList().IndexOf(value));
        combo.SelectionChanged += (_, _) =>
        {
            if (_layoutPropertySyncing || combo.SelectedItem is not ComboBoxItem { Tag: TEnum selected })
            {
                return;
            }

            update(selected);
        };
        row.Children.Add(combo);
        panel.Children.Add(row);
    }

    private StackPanel CreatePropertyRow(string labelKey)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(new TextBlock
        {
            Width = 150,
            Text = Loc.Get(labelKey),
            VerticalAlignment = VerticalAlignment.Center,
            Style = TryFindResource("SettingsRowDescriptionStyle") as Style
        });
        return row;
    }

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
        MetricKind.SystemMemory => "Settings.Components.MetricMem",
        MetricKind.SystemCpu => "Settings.Components.MetricCpu",
        MetricKind.SystemGpu => "Settings.Components.MetricGpu",
        MetricKind.ProcessMemory => "Settings.Components.MetricApp",
        _ => "Settings.Layout.PropertyMetric"
    };

    private (string ContainerId, LayoutSlotKind SlotKind) ResolveAddTarget()
    {
        if (_layoutEditorSelection is { } selection)
        {
            if (selection.IsSlot)
            {
                return (selection.ParentContainerId ?? "root", selection.SlotKind);
            }

            if (selection.IsContainer)
            {
                return (selection.Element?.InstanceId ?? "root", LayoutSlotKind.Primary);
            }

            return (
                selection.ParentContainerId ?? "root",
                selection.SlotKind);
        }

        var selectedSlot = LayoutSlotComboBox.SelectedItem as ComboBoxItem;
        return (
            "root",
            selectedSlot?.Tag is LayoutSlotKind slotKind
                ? slotKind
                : LayoutSlotKind.Primary);
    }

    private void UpdateLayoutEditorButtons()
    {
        var hasElement = _layoutEditorSelection is { IsSlot: false, Element: not null };
        var isRoot = string.Equals(
            _layoutEditorSelection?.Element?.InstanceId,
            "root",
            StringComparison.Ordinal);
        LayoutMoveUpButton.IsEnabled = hasElement && !isRoot;
        LayoutMoveDownButton.IsEnabled = hasElement && !isRoot;
        LayoutToggleButton.IsEnabled = hasElement && !isRoot;
        LayoutRemoveButton.IsEnabled = hasElement && !isRoot;
    }

    private LayoutProfileKey ResolveInitialLayoutProfile()
    {
        var settings = _coordinator.Current.Window;
        var vertical = settings.LayoutMode == PlayerLayoutMode.Vertical;
        return LayoutRuntimeService.ResolveProfileKey(settings.HostMode, vertical);
    }

    private void AddProfileOption(LayoutProfileKey key, string resourceKey)
    {
        LayoutProfileComboBox.Items.Add(new ComboBoxItem
        {
            Content = Loc.Get(resourceKey),
            Tag = key
        });
    }

    private void AddSlotOption(LayoutSlotKind slotKind, string resourceKey)
    {
        LayoutSlotComboBox.Items.Add(new ComboBoxItem
        {
            Content = Loc.Get(resourceKey),
            Tag = slotKind
        });
    }

    private static string GetElementTitle(LayoutElement element)
    {
        if (element is LayoutContainerElement container)
        {
            var key = container.ContainerKind switch
            {
                LayoutContainerKind.Static => "Settings.Layout.ContainerStatic",
                LayoutContainerKind.HoverSwitch => "Settings.Layout.ContainerHoverSwitch",
                LayoutContainerKind.AutoCollapse => "Settings.Layout.ContainerAutoCollapse",
                _ => "Settings.Layout.ContainerStatic"
            };
            return $"{Loc.Get(key)} ({container.InstanceId})";
        }

        if (element is LayoutWidgetElement widget &&
            ComponentCatalog.TryGet(widget.TypeId, out var definition))
        {
            return $"{Loc.Get(definition.NameResourceKey)} ({widget.InstanceId})";
        }

        return element.InstanceId;
    }

    private static string GetSlotTitle(LayoutSlotKind slotKind)
    {
        var key = slotKind switch
        {
            LayoutSlotKind.Primary => "Settings.Layout.EditorPrimarySlot",
            LayoutSlotKind.Secondary => "Settings.Layout.EditorSecondarySlot",
            LayoutSlotKind.Collapsed => "Settings.Layout.EditorCollapsedSlot",
            _ => "Settings.Layout.EditorPrimarySlot"
        };
        return Loc.Get(key);
    }

    private sealed record LayoutEditorNode(
        string InstanceId,
        bool IsContainer,
        bool IsSlot,
        string? ParentContainerId,
        LayoutSlotKind SlotKind,
        LayoutElement? Element);
}
