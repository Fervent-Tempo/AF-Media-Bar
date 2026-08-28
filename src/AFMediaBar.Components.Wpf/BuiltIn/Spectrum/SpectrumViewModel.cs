using CommunityToolkit.Mvvm.ComponentModel;
using AFMediaBar.Components.BuiltIn.Audio;

namespace AFMediaBar.Components.Wpf.BuiltIn.Spectrum;

public partial class SpectrumViewModel : ComponentViewModelBase
{
    public SpectrumViewModel(string instanceId, SpectrumSettings settings) : base(instanceId) => Settings = settings;
    public SpectrumSettings Settings { get; }
    [ObservableProperty] private IReadOnlyList<float> values = Array.Empty<float>();
    [ObservableProperty] private bool isAudioAvailable = true;

    partial void OnIsAudioAvailableChanged(bool value) => WarningCode = value ? null : "Spectrum.AudioUnavailable";

    public void SetValues(IReadOnlyList<float> source)
    {
        var count = Math.Min(source.Count, SpectrumDefinition.MaximumBandCount);
        var snapshot = new float[count];
        for (var index = 0; index < count; index++) snapshot[index] = Math.Clamp(source[index], 0, 1);
        Values = snapshot;
    }
}
