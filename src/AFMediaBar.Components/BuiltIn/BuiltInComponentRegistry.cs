using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn.Audio;
using AFMediaBar.Components.BuiltIn.Containers;
using AFMediaBar.Components.BuiltIn.Layout;
using AFMediaBar.Components.BuiltIn.Media;
using AFMediaBar.Components.BuiltIn.Playback;
using AFMediaBar.Components.BuiltIn.System;

namespace AFMediaBar.Components.BuiltIn;

public sealed class BuiltInComponentRegistry : IComponentRegistry
{
    private static readonly IReadOnlyList<IComponentDefinition> Definitions = CreateDefinitions();
    public IReadOnlyList<IComponentDefinition> Items => Definitions;

    public bool TryGet(string typeId, out IComponentDefinition definition)
    {
        definition = Definitions.FirstOrDefault(x => string.Equals(x.Metadata.TypeId, typeId, StringComparison.Ordinal))!;
        return definition is not null;
    }

    private static IReadOnlyList<IComponentDefinition> CreateDefinitions()
    {
        var list = new List<IComponentDefinition>();
        list.Add(new StaticContainerDefinition());
        list.Add(new HoverSwitchContainerDefinition());
        list.Add(new CollapseContainerDefinition());
        list.Add(new ArtworkDefinition());
        list.Add(new MediaTextDefinition());
        list.Add(new MediaSourceDefinition());
        list.Add(new PlaybackCommandDefinition());
        list.Add(new OutputDeviceDefinition());
        list.Add(new VolumeDefinition());
        list.Add(new SpectrumDefinition());
        list.Add(new MetricsDefinition());
        list.Add(new SeparatorDefinition());
        return list;
    }

}
