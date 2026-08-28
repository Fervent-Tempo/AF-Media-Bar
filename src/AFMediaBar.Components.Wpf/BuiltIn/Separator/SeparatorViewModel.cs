using CommunityToolkit.Mvvm.ComponentModel;
using AFMediaBar.Components.BuiltIn.Layout;

namespace AFMediaBar.Components.Wpf.BuiltIn.Separator;

public partial class SeparatorViewModel : ComponentViewModelBase
{
    public SeparatorViewModel(string instanceId, SeparatorSettings settings, bool isVertical = true)
        : base(instanceId)
    {
        Settings = settings;
        IsVertical = isVertical;
    }

    public SeparatorSettings Settings { get; }

    [ObservableProperty]
    private bool isVertical;

    public double ThicknessDip => Math.Clamp(Settings.ThicknessDip, 1, 8);
    public double LengthDip => Math.Clamp(Settings.LengthDip, 4, 256);
}
