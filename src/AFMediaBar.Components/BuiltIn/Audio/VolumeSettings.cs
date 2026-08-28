using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Audio;

public sealed record VolumeSettings(int ButtonSizeDip = 24, int WheelStepPercent = 2) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.Volume;
}
