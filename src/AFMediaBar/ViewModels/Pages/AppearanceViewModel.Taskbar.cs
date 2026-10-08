// Presents taskbar geometry and control appearance on the Appearance page while retaining the existing settings schema.
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>提供外观页中的任务栏布局与交互按钮设置。</summary>
public partial class AppearanceViewModel
{
    public TaskbarInformationDensity InteractionButtonSize
    {
        get => SettingsManager.Current.TaskbarExperience.Density;
        set => UpdateTaskbarExperience(SettingsManager.Current.TaskbarExperience with { Density = value });
    }

    public TaskbarContentLayout ContentLayout
    {
        get => SettingsManager.Current.TaskbarExperience.ContentLayout;
        set => UpdateTaskbarExperience(SettingsManager.Current.TaskbarExperience with { ContentLayout = value });
    }

    public TaskbarMediaTextAlignment MediaTextAlignment
    {
        get => SettingsManager.Current.TaskbarExperience.MediaTextAlignment;
        set => UpdateTaskbarExperience(SettingsManager.Current.TaskbarExperience with { MediaTextAlignment = value });
    }

    public double ComponentSpacingDip
    {
        get => SettingsManager.Current.TaskbarExperience.ComponentSpacingDip;
        set => UpdateTaskbarExperience(SettingsManager.Current.TaskbarExperience with { ComponentSpacingDip = value });
    }

    public double HoverButtonSpacingDip
    {
        get => SettingsManager.Current.TaskbarExperience.HoverButtonSpacingDip;
        set => UpdateTaskbarExperience(SettingsManager.Current.TaskbarExperience with { HoverButtonSpacingDip = value });
    }

    public bool FollowMediaTextLength
    {
        get => SettingsManager.Current.TaskbarExperience.LengthMode == TaskbarLengthMode.FollowContent;
        set => UpdateTaskbarExperience(SettingsManager.Current.TaskbarExperience with
        {
            LengthMode = value ? TaskbarLengthMode.FollowContent : TaskbarLengthMode.Fixed,
            FixedLengthDip = SettingsManager.Current.TaskbarExperience.FixedLengthDip
        });
    }

    public bool UsesFixedTaskbarLength => !FollowMediaTextLength;
    private bool HasFixedTaskbarLengthRange => _taskbarLengthConstraints.HasAvailableRange &&
        Math.Ceiling(_taskbarLengthConstraints.MinimumLengthDip) <= Math.Floor(_taskbarLengthConstraints.MaximumLengthDip);
    public bool CanEditFixedTaskbarLength => UsesFixedTaskbarLength && HasFixedTaskbarLengthRange;
    public double FixedTaskbarLengthMinimum => HasFixedTaskbarLengthRange ? Math.Ceiling(_taskbarLengthConstraints.MinimumLengthDip) : 0;
    public double FixedTaskbarLengthMaximum => HasFixedTaskbarLengthRange ? Math.Floor(_taskbarLengthConstraints.MaximumLengthDip) : 0;

    public double FixedTaskbarLengthDip
    {
        get => Math.Clamp(SettingsManager.Current.TaskbarExperience.FixedLengthDip,
            FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum);
        set
        {
            // Binding coercion after an environment change is presentation, not a new user preference.
            if (_isRefreshing || !HasFixedTaskbarLengthRange || value == FixedTaskbarLengthDip)
                return;
            UpdateTaskbarExperience(SettingsManager.Current.TaskbarExperience with
            {
                FixedLengthDip = Math.Clamp(value, FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum)
            });
        }
    }

    public string FixedTaskbarLengthRangeText =>
        HasFixedTaskbarLengthRange
            ? Translations.Format("DisplayModes.Width.RangeText", FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum)
            : Translations.Get("DisplayModes.Width.NoCommonRange");

    public ObservableCollection<TaskbarRestComponentSettingItem> RestOrderEntries { get; } = [];

    private void UpdateTaskbarExperience(TaskbarExperienceSettings value)
    {
        if (_isRefreshing || value.Equals(SettingsManager.Current.TaskbarExperience))
            return;

        SettingsManager.SetTaskbarExperienceSettings(value.Normalize());
    }

    private void RaiseTaskbarAppearance()
    {
        OnPropertyChanged(nameof(InteractionButtonSize));
        OnPropertyChanged(nameof(ContentLayout));
        OnPropertyChanged(nameof(MediaTextAlignment));
        OnPropertyChanged(nameof(ComponentSpacingDip));
        OnPropertyChanged(nameof(HoverButtonSpacingDip));
        OnPropertyChanged(nameof(FollowMediaTextLength));
        OnPropertyChanged(nameof(UsesFixedTaskbarLength));
        OnPropertyChanged(nameof(CanEditFixedTaskbarLength));
        OnPropertyChanged(nameof(FixedTaskbarLengthMinimum));
        OnPropertyChanged(nameof(FixedTaskbarLengthMaximum));
        OnPropertyChanged(nameof(FixedTaskbarLengthDip));
        OnPropertyChanged(nameof(FixedTaskbarLengthRangeText));
        RefreshRestOrderEntries();
    }

    private void OnTaskbarLengthConstraintsChanged(object? sender, EventArgs e)
    {
        var previousRefreshing = _isRefreshing;
        _isRefreshing = true;
        try
        {
            OnPropertyChanged(nameof(CanEditFixedTaskbarLength));
            OnPropertyChanged(nameof(FixedTaskbarLengthMinimum));
            OnPropertyChanged(nameof(FixedTaskbarLengthMaximum));
            OnPropertyChanged(nameof(FixedTaskbarLengthDip));
            OnPropertyChanged(nameof(FixedTaskbarLengthRangeText));
        }
        finally { _isRefreshing = previousRefreshing; }
    }

    private void RefreshRestOrderEntries()
    {
        var order = TaskbarRestLayoutPolicy.ResolveOrder(SettingsManager.Current.TaskbarExperience.RestComponentOrder);
        if (RestOrderEntries.Select(entry => entry.Component).SequenceEqual(order))
        {
            foreach (var entry in RestOrderEntries)
            {
                entry.DisplayName = Translations.Get(TaskbarRestComponentSettingItem.ResolveNameKey(entry.Component));
                entry.Description = Translations.Get(TaskbarRestComponentSettingItem.ResolveDescriptionKey(entry.Component));
            }
            return;
        }

        RestOrderEntries.Clear();
        foreach (var component in order)
            RestOrderEntries.Add(new TaskbarRestComponentSettingItem(
                component, canMove: !TaskbarRestLayoutPolicy.FixedOrder.Contains(component), isVisible: true));
    }

    [RelayCommand]
    private void MoveRestComponentUp(TaskbarRestComponentSettingItem? entry) => MoveRestComponent(entry, -1);

    [RelayCommand]
    private void MoveRestComponentDown(TaskbarRestComponentSettingItem? entry) => MoveRestComponent(entry, 1);

    private void MoveRestComponent(TaskbarRestComponentSettingItem? entry, int offset)
    {
        if (entry is null || !entry.CanMove)
            return;

        var index = RestOrderEntries.IndexOf(entry);
        var target = index + offset;
        if (index < 0 || target < TaskbarRestLayoutPolicy.FixedOrder.Count || target >= RestOrderEntries.Count)
            return;

        RestOrderEntries.Move(index, target);
        SaveRestComponentOrder();
    }

    private void SaveRestComponentOrder()
    {
        var ordered = RestOrderEntries.Where(entry => entry.CanMove).Select(entry => entry.Component).ToArray();
        UpdateTaskbarExperience(SettingsManager.Current.TaskbarExperience with
        {
            RestComponentOrder = ordered.SequenceEqual(TaskbarRestLayoutPolicy.DefaultTailOrder) ? null : ordered
        });
    }

    [RelayCommand]
    private void ResetRestComponentOrder()
    {
        var ordered = RestOrderEntries.OrderBy(entry => entry.CanMove
            ? TaskbarRestLayoutPolicy.DefaultTailOrder.ToList().IndexOf(entry.Component) + TaskbarRestLayoutPolicy.FixedOrder.Count
            : TaskbarRestLayoutPolicy.FixedOrder.ToList().IndexOf(entry.Component)).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var current = RestOrderEntries.IndexOf(ordered[index]);
            if (current != index)
                RestOrderEntries.Move(current, index);
        }
        SaveRestComponentOrder();
    }
}
