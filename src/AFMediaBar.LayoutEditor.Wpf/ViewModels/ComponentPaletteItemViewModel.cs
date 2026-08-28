using AFMediaBar.Components.Abstractions;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.LayoutEditor.Wpf.ViewModels;

/// <summary>
/// Palette metadata is independent from WPF controls. The host decides how a
/// token is rendered and the constraint service decides whether placement is valid.
/// </summary>
public sealed class ComponentPaletteItemViewModel
{
    public ComponentPaletteItemViewModel(
        IComponentDefinition definition,
        string token,
        string displayName,
        string description)
    {
        Definition = definition;
        Token = token;
        DisplayName = displayName;
        Description = description;
    }

    public IComponentDefinition Definition { get; }
    public ComponentMetadata Metadata => Definition.Metadata;
    public string TypeId => Metadata.TypeId;
    public ComponentCategory Category => Metadata.Category;
    public string NameResourceKey => Metadata.NameResourceKey;
    public string DescriptionResourceKey => Metadata.DescriptionResourceKey;
    public string Token { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool IsContainer => Definition.Kind == ComponentKind.Container;

    public bool Supports(LayoutProfile profile) => profile.LayoutMode switch
    {
        PlayerLayoutMode.Vertical => Metadata.SupportsVertical,
        _ => Metadata.SupportsHorizontal
    };
}

public sealed record ComponentPaletteGroupViewModel(
    ComponentCategory Category,
    string DisplayName,
    IReadOnlyList<ComponentPaletteItemViewModel> Items);
