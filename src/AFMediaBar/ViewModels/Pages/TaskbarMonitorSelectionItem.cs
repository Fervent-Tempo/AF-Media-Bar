using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>任务栏目标显示器的一项，可独立勾选并参与单选或多选。/ One independently selectable taskbar target monitor, supporting one or many selections.</summary>
public partial class TaskbarMonitorSelectionItem : ObservableObject
{
    public string DeviceId { get; }
    public string DisplayName { get; }
    public bool IsPrimary { get; }
    public bool IsAvailable { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _canToggle = true;

    public event Action<TaskbarMonitorSelectionItem>? SelectionChanged;

    public TaskbarMonitorSelectionItem(
        string deviceId,
        string displayName,
        bool isPrimary,
        bool isAvailable,
        bool isSelected)
    {
        DeviceId = deviceId;
        DisplayName = displayName;
        IsPrimary = isPrimary;
        IsAvailable = isAvailable;
        _isSelected = isSelected;
    }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this);
}
