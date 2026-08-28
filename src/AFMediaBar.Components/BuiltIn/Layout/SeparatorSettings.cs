using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Layout;

public sealed record SeparatorSettings(int ThicknessDip = 1, int LengthDip = 22) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.Separator;
}
