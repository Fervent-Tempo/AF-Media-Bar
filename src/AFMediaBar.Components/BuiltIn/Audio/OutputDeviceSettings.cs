using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Audio;

public sealed record OutputDeviceSettings(int ButtonSizeDip = 24) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.OutputDevice;
}
