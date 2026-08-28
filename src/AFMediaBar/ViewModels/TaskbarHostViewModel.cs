using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels;

/// <summary>
/// Bindable taskbar-host snapshot. Native transitions remain in
/// <c>TaskbarHostService</c>; this projection is safe to test without Windows handles.
/// </summary>
public sealed partial class TaskbarHostViewModel : ViewModelBase
{
    [ObservableProperty]
    private nint taskbarHandle;

    [ObservableProperty]
    private bool isEmbedded;

    [ObservableProperty]
    private bool isFloating;

    [ObservableProperty]
    private bool isRecoveryPending;

    [ObservableProperty]
    private string? recoveryReason;

    public void ApplySnapshot(nint handle, bool embedded, bool floating)
    {
        TaskbarHandle = handle;
        IsEmbedded = embedded;
        IsFloating = floating;
    }

    public void SetRecovery(string? reason)
    {
        RecoveryReason = reason;
        IsRecoveryPending = !string.IsNullOrWhiteSpace(reason);
    }
}
