using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>四种显示模式及任务栏轻度自定义。 / Four display modes and light taskbar customization.</summary>
public partial class DisplayModesViewModel : ObservableObject
{
    private bool _isRefreshing;
    public WindowMode CurrentWindowMode => SettingsManager.Current.WindowMode;
    public bool IsTaskbarMode => CurrentWindowMode == WindowMode.Taskbar;
    public bool IsDynamicIslandMode => CurrentWindowMode == WindowMode.DynamicIsland;

    public bool HoverLayerEnabled
    {
        get => SettingsManager.Current.TaskbarExperience.HoverLayerEnabled;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { HoverLayerEnabled = value });
    }

    public bool FullLayerEnabled
    {
        get => SettingsManager.Current.TaskbarExperience.FullLayerEnabled;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { FullLayerEnabled = value });
    }

    public TaskbarInformationDensity Density
    {
        get => SettingsManager.Current.TaskbarExperience.Density;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { Density = value });
    }

    public TaskbarContentLayout ContentLayout
    {
        get => SettingsManager.Current.TaskbarExperience.ContentLayout;
        set => UpdateExperience(SettingsManager.Current.TaskbarExperience with { ContentLayout = value });
    }

    public LayoutOrientationMode Orientation
    {
        get => SettingsManager.Current.LayoutOrientationMode;
        set
        {
            if (_isRefreshing || SettingsManager.Current.LayoutOrientationMode == value) return;
            SettingsManager.Current.LayoutOrientationMode = value;
            SettingsManager.RaiseLayoutSettingsChanged(SettingsManager.Current.WindowMode, value);
            OnPropertyChanged();
        }
    }

    public bool IsTaskbarPositionLocked
    {
        get => SettingsManager.Current.TaskbarBarPositionLocked;
        set { SettingsManager.Current.TaskbarBarPositionLocked = value; OnPropertyChanged(); }
    }

    public bool IsTaskbarAvoidingIcons
    {
        get => SettingsManager.Current.TaskbarBarAvoidIcons;
        set
        {
            SettingsManager.Current.TaskbarBarAvoidIcons = value;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    public double TaskbarCrossAxisOffsetDip
    {
        get => SettingsManager.Current.TaskbarBarCrossAxisOffsetDip;
        set
        {
            SettingsManager.Current.TaskbarBarCrossAxisOffsetDip = value;
            SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
            OnPropertyChanged();
        }
    }

    public DisplayModesViewModel() => SettingsManager.SettingsChanged += OnSettingsChanged;

    [RelayCommand] private void SwitchToTaskbarMode() => SwitchMode(WindowMode.Taskbar);
    [RelayCommand] private void SwitchToDynamicIslandMode() => SwitchMode(WindowMode.DynamicIsland);

    [RelayCommand]
    private void ResetTaskbarPosition()
    {
        SettingsManager.Current.Position = TaskbarBarPosition.Start;
        SettingsManager.Current.TaskbarBarManualPadding = 0;
        TaskbarCrossAxisOffsetDip = 0;
        SettingsManager.RaiseLayoutSettingsChanged(CurrentWindowMode, Orientation);
    }

    private void SwitchMode(WindowMode mode)
    {
        if (CurrentWindowMode == mode) return;
        SettingsManager.Current.WindowMode = mode;
        SettingsManager.RaiseLayoutSettingsChanged(mode, Orientation);
        RaiseAll();
    }

    private void UpdateExperience(TaskbarExperienceSettings value)
    {
        if (_isRefreshing) return;
        SettingsManager.SetTaskbarExperienceSettings(value.Normalize());
        RaiseExperience();
    }

    public void ResetDisplayModes() => SettingsManager.ResetDisplayModes();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.ResetScope is SettingsResetScope.DisplayModes or SettingsResetScope.Layout or SettingsResetScope.All)
            RaiseAll();
    }

    private void RaiseAll()
    {
        _isRefreshing = true;
        try
        {
            OnPropertyChanged(nameof(CurrentWindowMode)); OnPropertyChanged(nameof(IsTaskbarMode)); OnPropertyChanged(nameof(IsDynamicIslandMode));
            RaiseExperience(); OnPropertyChanged(nameof(Orientation)); OnPropertyChanged(nameof(IsTaskbarPositionLocked));
            OnPropertyChanged(nameof(IsTaskbarAvoidingIcons)); OnPropertyChanged(nameof(TaskbarCrossAxisOffsetDip));
        }
        finally { _isRefreshing = false; }
    }

    private void RaiseExperience()
    {
        OnPropertyChanged(nameof(HoverLayerEnabled)); OnPropertyChanged(nameof(FullLayerEnabled));
        OnPropertyChanged(nameof(Density)); OnPropertyChanged(nameof(ContentLayout));
    }
}
