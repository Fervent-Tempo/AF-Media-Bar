using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>共享点击、滚轮与托盘交互设置。 / Shared click, wheel, and tray interaction settings.</summary>
public partial class InteractionViewModel : ObservableObject
{
    public MediaInteractionMode Mode { get => Current.Mode; set => Update(Current with { Mode = value }); }
    public WheelAction PrimaryWheelAction { get => Current.PrimaryWheelAction; set => Update(Current with { PrimaryWheelAction = value }); }
    public bool ChordWheelEnabled { get => Current.ChordWheelEnabled; set { Update(Current with { ChordWheelEnabled = value }); OnPropertyChanged(nameof(CanConfigureChord)); } }
    public MouseChordButton ChordButton { get => Current.ChordButton; set => Update(Current with { ChordButton = value }); }
    public WheelAction ChordWheelAction { get => Current.ChordWheelAction; set => Update(Current with { ChordWheelAction = value }); }
    public TrayClickAction TrayClickAction { get => Current.TrayClickAction; set => Update(Current with { TrayClickAction = value }); }
    public bool TrayUsesGlobalWheel { get => Current.TrayUsesGlobalWheel; set => Update(Current with { TrayUsesGlobalWheel = value }); }
    public bool CanConfigureChord => ChordWheelEnabled;
    public bool TransportButtonsVisible => Mode != MediaInteractionMode.Gestures;
    private static GlobalInteractionSettings Current => SettingsManager.Current.Interaction;

    public InteractionViewModel() => SettingsManager.SettingsChanged += OnSettingsChanged;

    private void Update(GlobalInteractionSettings settings)
    {
        SettingsManager.SetInteractionSettings(settings.Normalize());
        RaiseAll();
    }

    public void ResetInteraction() => SettingsManager.ResetInteraction();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.ResetScope is SettingsResetScope.Interaction or SettingsResetScope.All) RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Mode)); OnPropertyChanged(nameof(PrimaryWheelAction));
        OnPropertyChanged(nameof(ChordWheelEnabled)); OnPropertyChanged(nameof(ChordButton));
        OnPropertyChanged(nameof(ChordWheelAction)); OnPropertyChanged(nameof(TrayClickAction));
        OnPropertyChanged(nameof(TrayUsesGlobalWheel)); OnPropertyChanged(nameof(CanConfigureChord));
        OnPropertyChanged(nameof(TransportButtonsVisible));
    }
}
