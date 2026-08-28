using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;

namespace AFMediaBar.LayoutEditor.Wpf.ViewModels;

/// <summary>
/// Editor state coordinator for the WPF editor. It owns the session, selection,
/// tree and palette projections; canvas input remains in the existing WPF
/// controls and submits mutations through <see cref="LayoutEditorSession"/>.
/// </summary>
public sealed partial class LayoutEditorViewModel : ObservableObject, IDisposable
{
    private readonly IComponentRegistry _registry;
    private readonly Func<string, string> _localize;
    private LayoutDocument _lastDocument;
    private bool _selectionChanging;
    private bool _disposed;

    public LayoutEditorViewModel(
        LayoutDocument document,
        IComponentRegistry? registry = null,
        Func<string, string>? localize = null,
        LayoutProfileKey profileKey = LayoutProfileKey.Horizontal)
    {
        _registry = registry ?? new BuiltInComponentRegistry();
        _localize = localize ?? (key => key);
        Inspector = new LayoutInspectorViewModel(_localize);
        Session = new LayoutEditorSession(document, profileKey);
        _lastDocument = document;
        ProfileKey = profileKey;
        Session.StateChanged += Session_OnStateChanged;
        RebuildPalette();
        RebuildTree();
    }

    public LayoutEditorSession Session { get; }
    public ObservableCollection<LayoutTreeItemViewModel> Roots { get; } = [];
    public ObservableCollection<ComponentPaletteItemViewModel> Palette { get; } = [];
    public ObservableCollection<ComponentPaletteGroupViewModel> PaletteGroups { get; } = [];
    public LayoutInspectorViewModel Inspector { get; }
    public LayoutEditorErrorViewModel Error { get; } = new();
    public event EventHandler<LayoutDocumentChangedEventArgs>? DocumentChanged;

    [ObservableProperty]
    private LayoutProfileKey profileKey;

    [ObservableProperty]
    private string? selectedInstanceId;

    public bool CanUndo => Session.CanUndo;
    public bool CanRedo => Session.CanRedo;
    public LayoutProfile CurrentProfile => Session.Document.Get(ProfileKey);

    public void SelectProfile(LayoutProfileKey key)
    {
        if (ProfileKey == key) return;
        ProfileKey = key;
        Session.SelectProfile(key);
        SelectedInstanceId = null;
    }

    public void SelectNode(string? instanceId)
    {
        SelectedInstanceId = string.IsNullOrWhiteSpace(instanceId) ? null : instanceId;
        // Selection is editor-only state. Do not rebuild the bound tree while
        // WPF is generating TreeViewItem containers for the selection event.
        _selectionChanging = true;
        try
        {
            Session.Select(SelectedInstanceId);
        }
        finally
        {
            _selectionChanging = false;
        }
        RefreshInspector();
        MarkSelected(Roots);
    }

    public bool TryApply(Func<LayoutDocument, LayoutDocument?> mutation) => Session.TryApply(mutation);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (Session.Undo()) RefreshProjections();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (Session.Redo()) RefreshProjections();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Session.StateChanged -= Session_OnStateChanged;
    }

    private void Session_OnStateChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_selectionChanging)
        {
            Error.Set(Session.LastError, Session.LastError is null ? null : "Settings.Layout.EditorAddFailed");
            return;
        }
        if (_lastDocument != Session.Document)
        {
            var previous = _lastDocument;
            _lastDocument = Session.Document;
            DocumentChanged?.Invoke(this, new LayoutDocumentChangedEventArgs(previous, Session.Document));
        }
        RefreshProjections();
    }

    private void RefreshProjections()
    {
        RebuildTree();
        RefreshInspector();
        Error.Set(Session.LastError, Session.LastError is null ? null : "Settings.Layout.EditorAddFailed");
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CurrentProfile));
    }

    private void RebuildPalette()
    {
        Palette.Clear();
        foreach (var definition in _registry.Items.OrderBy(x => x.Metadata.SortOrder))
        {
            if (definition.Metadata.TypeId == ComponentTypeIds.PlaybackCommand)
            {
                foreach (var command in new[]
                {
                    MediaCommandKind.Previous,
                    MediaCommandKind.PlayPause,
                    MediaCommandKind.Next,
                    MediaCommandKind.SelectSource
                })
                {
                    AddPaletteItem(
                        definition,
                        $"{BuiltInWidgetTypeIds.Command}|{(int)command}",
                        GetCommandResourceKey(command));
                }
                continue;
            }

            if (definition.Metadata.TypeId == ComponentTypeIds.MediaText)
            {
                foreach (var kind in new[]
                {
                    MediaTextKind.Title,
                    MediaTextKind.Artist,
                    MediaTextKind.TitleAndArtist
                })
                {
                    AddPaletteItem(
                        definition,
                        $"{BuiltInWidgetTypeIds.MediaText}|{(int)kind}",
                        GetMediaTextResourceKey(kind));
                }
                continue;
            }

            var token = definition.Metadata.TypeId switch
            {
                ComponentTypeIds.StaticContainer => "container:static",
                ComponentTypeIds.HoverSwitchContainer => "container:hover",
                ComponentTypeIds.CollapseContainer => "container:edge",
                ComponentTypeIds.OutputDevice => $"{BuiltInWidgetTypeIds.Command}|{(int)MediaCommandKind.SelectOutputDevice}",
                ComponentTypeIds.Volume => $"{BuiltInWidgetTypeIds.Command}|{(int)MediaCommandKind.AdjustVolume}",
                _ => definition.Metadata.TypeId
            };
            AddPaletteItem(definition, token, definition.Metadata.NameResourceKey);
        }

        PaletteGroups.Clear();
        foreach (var group in Palette.GroupBy(x => x.Category))
        {
            PaletteGroups.Add(new ComponentPaletteGroupViewModel(
                group.Key,
                _localize(GetCategoryResourceKey(group.Key)),
                group.ToArray()));
        }
    }

    private void AddPaletteItem(
        IComponentDefinition definition,
        string token,
        string nameResourceKey) =>
        Palette.Add(new ComponentPaletteItemViewModel(
            definition,
            token,
            _localize(nameResourceKey),
            _localize(definition.Metadata.DescriptionResourceKey)));

    private static string GetCommandResourceKey(MediaCommandKind command) => command switch
    {
        MediaCommandKind.Previous => "Main.Control.Previous",
        MediaCommandKind.Next => "Main.Control.Next",
        MediaCommandKind.SelectSource => "Main.Menu.ShowSource",
        _ => "Main.Control.Play"
    };

    private static string GetMediaTextResourceKey(MediaTextKind kind) => kind switch
    {
        MediaTextKind.Artist => "Settings.Layout.PropertyTextArtist",
        MediaTextKind.TitleAndArtist => "Settings.Layout.PropertyTextTitleAndArtist",
        _ => "Settings.Layout.PropertyTextTitle"
    };

    private static string GetCategoryResourceKey(ComponentCategory category) => category switch
    {
        ComponentCategory.Container => "Settings.Layout.CategoryLayout",
        ComponentCategory.Media => "Settings.Layout.CategoryMedia",
        ComponentCategory.Playback => "Settings.Layout.CategoryControls",
        ComponentCategory.Audio => "Settings.Layout.CategoryAudio",
        ComponentCategory.System => "Settings.Layout.CategorySystem",
        _ => "Settings.Layout.CategoryLayout"
    };

    private void RebuildTree()
    {
        Roots.Clear();
        var profile = CurrentProfile;
        foreach (var container in profile.Containers)
        {
            Roots.Add(BuildContainer(container, null));
        }
        foreach (var collapse in profile.CollapseContainers)
        {
            var item = new LayoutTreeItemViewModel(
                collapse.InstanceId,
                "Settings.Layout.ContainerAutoCollapse",
                _localize("Settings.Layout.ContainerAutoCollapse"),
                LayoutEditorNodeKind.CollapseContainer,
                collapse);
            AddSlot(item, collapse.ExpandedSlot);
            Roots.Add(item);
        }
        MarkSelected(Roots);
    }

    private LayoutTreeItemViewModel BuildContainer(
        LayoutContainerElement container,
        LayoutTreeItemViewModel? parent)
    {
        var item = new LayoutTreeItemViewModel(
            container.InstanceId,
            container.ContainerKind == LayoutContainerKind.HoverSwitch
                ? "Settings.Layout.ContainerHoverSwitch"
                : "Settings.Layout.ContainerStatic",
            _localize(container.ContainerKind == LayoutContainerKind.HoverSwitch
                ? "Settings.Layout.ContainerHoverSwitch"
                : "Settings.Layout.ContainerStatic"),
            LayoutEditorNodeKind.Container,
            container,
            parent);
        AddSlot(item, container.PrimarySlot);
        if (container.ContainerKind == LayoutContainerKind.HoverSwitch)
        {
            AddSlot(item, container.SecondarySlot);
        }
        return item;
    }

    private void AddSlot(LayoutTreeItemViewModel parent, LayoutSlot slot)
    {
        var slotItem = new LayoutTreeItemViewModel(
            slot.SlotId,
            "Settings.Layout.EditorContent",
            _localize("Settings.Layout.EditorContent"),
            LayoutEditorNodeKind.Slot,
            slot,
            parent);
        parent.Children.Add(slotItem);
        foreach (var child in slot.Children)
        {
            switch (child)
            {
                case LayoutWidgetElement widget:
                    slotItem.Children.Add(new LayoutTreeItemViewModel(
                        widget.InstanceId,
                        $"Component:{widget.TypeId}",
                        ResolveWidgetName(widget),
                        LayoutEditorNodeKind.Widget,
                        widget,
                        slotItem));
                    break;
                case LayoutContainerElement nested:
                    slotItem.Children.Add(BuildContainer(nested, slotItem));
                    break;
            }
        }
    }

    private string ResolveWidgetName(LayoutWidgetElement widget)
    {
        if (ComponentDefinitionAdapter.TryMapSettings(widget, out var settings) &&
            _registry.TryGet(settings.TypeId, out var definition))
        {
            return _localize(definition.Metadata.NameResourceKey);
        }
        return widget.TypeId;
    }

    private void RefreshInspector()
    {
        var model = SelectedInstanceId is null
            ? null
            : LayoutElementQueryService.Find(CurrentProfile, SelectedInstanceId);
        if (model is LayoutWidgetElement widget &&
            ComponentDefinitionAdapter.TryMapSettings(widget, out var mappedSettings) &&
            _registry.TryGet(mappedSettings.TypeId, out var definition))
        {
            var grid = LayoutGridSettings.Normalize(CurrentProfile.Grid);
            var context = new ComponentMeasureContext(
                grid.Columns,
                grid.Rows,
                grid.CellSizeDip,
                CurrentProfile.LayoutMode == PlayerLayoutMode.Vertical);
            var settings = mappedSettings;
            Inspector.SetSelection(
                widget.InstanceId,
                widget,
                definition.Measure(settings, context),
                definition.Validate(settings));
            return;
        }
        Inspector.SetSelection(SelectedInstanceId, model);
    }

    private void MarkSelected(IEnumerable<LayoutTreeItemViewModel> items)
    {
        foreach (var item in items)
        {
            item.IsSelected = string.Equals(item.InstanceId, SelectedInstanceId, StringComparison.Ordinal);
            MarkSelected(item.Children);
        }
    }
}

public sealed class LayoutDocumentChangedEventArgs(
    LayoutDocument previous,
    LayoutDocument current) : EventArgs
{
    public LayoutDocument Previous { get; } = previous;
    public LayoutDocument Current { get; } = current;
}
