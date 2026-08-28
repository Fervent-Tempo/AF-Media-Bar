using AFMediaBar.Components.Abstractions;

namespace AFMediaBar.Components.BuiltIn.Audio;

public sealed record SpectrumSettings(int BandCount = 9, int RefreshRateHz = 20, int SensitivityPercent = 100) : IComponentSettings
{
    public string TypeId => ComponentTypeIds.Spectrum;
}
