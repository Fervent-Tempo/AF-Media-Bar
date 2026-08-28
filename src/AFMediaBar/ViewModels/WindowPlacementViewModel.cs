using CommunityToolkit.Mvvm.ComponentModel;
using AFMediaBar.Models;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.ViewModels;

/// <summary>
/// Observable projection of window placement state. It contains no HWND,
/// registry, COM, or taskbar API; the window shell supplies native snapshots.
/// </summary>
public sealed partial class WindowPlacementViewModel : ViewModelBase
{
    public WindowPlacementViewModel(WindowSettings settings)
    {
        ApplySettings(settings);
    }

    [ObservableProperty]
    private WindowSettings settings;

    [ObservableProperty]
    private PlayerLayoutMode layoutMode;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private int? left;

    [ObservableProperty]
    private int? top;

    [ObservableProperty]
    private int width;

    [ObservableProperty]
    private int height;

    [ObservableProperty]
    private uint dpi = 96;

    [ObservableProperty]
    private bool isRecoveryPending;

    [ObservableProperty]
    private string? recoveryReason;

    public double DpiScale => Dpi > 0 ? Dpi / 96d : 1d;

    public void ApplySettings(WindowSettings value)
    {
        Settings = value;
        LayoutMode = value.LayoutMode;
    }

    public void ApplyBounds(int? windowLeft, int? windowTop, int windowWidth, int windowHeight, uint windowDpi)
    {
        Left = windowLeft;
        Top = windowTop;
        Width = Math.Max(0, windowWidth);
        Height = Math.Max(0, windowHeight);
        Dpi = windowDpi > 0 ? windowDpi : 96;
        OnPropertyChanged(nameof(DpiScale));
    }

    public void SetPresentation(bool visible, bool expanded)
    {
        IsVisible = visible;
        IsExpanded = expanded;
    }

    public void SetRecovery(string? reason)
    {
        RecoveryReason = reason;
        IsRecoveryPending = !string.IsNullOrWhiteSpace(reason);
    }
}
