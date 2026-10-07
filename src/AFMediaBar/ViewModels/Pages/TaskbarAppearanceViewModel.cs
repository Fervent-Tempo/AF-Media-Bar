// Owns taskbar appearance for one context, not application theme or interface typography; its scope releases all subscriptions.
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>提供外观页中的任务栏布局与交互按钮设置。</summary>
public partial class TaskbarAppearanceViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    private readonly TaskbarLengthConstraintsService _taskbarLengthConstraints;
    private readonly LocalizationService _localization;
    private bool _isRefreshing;
    private bool _disposed;

    /// <summary>Creates a taskbar-only appearance editor for the cached page scope.</summary>
    public TaskbarAppearanceViewModel(ISettingsConfiguration configuration, TaskbarLengthConstraintsService constraints, LocalizationService localization)
    {
        _configuration = configuration;
        _taskbarLengthConstraints = constraints;
        _localization = localization;
        constraints.Changed += OnTaskbarLengthConstraintsChanged;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        localization.LanguageChanged += OnLanguageChanged;
        RefreshRestOrderEntries();
    }

    /// <summary>Media color belongs to the current mode, even while phase one uses the legacy appearance record.</summary>
    public PlayerForegroundMode PlayerForegroundMode
    {
        get => _configuration.Current.Appearance.PlayerForegroundMode;
        set => _configuration.SetAppearance(_configuration.Current.Appearance with { PlayerForegroundMode = value });
    }
    /// <summary>Media text scale, independent of the settings window's font selectors.</summary>
    public int MediaFontSizePercent
    {
        get => _configuration.Current.TaskbarExperience.Normalize().MediaFontSizePercent;
        set => _configuration.SetTaskbarExperience(_configuration.Current.TaskbarExperience with { MediaFontSizePercent = value });
    }
    /// <summary>Whether horizontal-only ordering and spacing controls are meaningful for this environment.</summary>
    public bool IsHorizontalLayout => _configuration.Context.Orientation != AFMediaBar.Classes.Models.Layout.LayoutOrientation.Vertical;

    public TaskbarInformationDensity InteractionButtonSize
    {
        get => _configuration.Current.TaskbarExperience.Density;
        set => UpdateTaskbarExperience(_configuration.Current.TaskbarExperience with { Density = value });
    }

    public TaskbarContentLayout ContentLayout
    {
        get => _configuration.Current.TaskbarExperience.ContentLayout;
        set => UpdateTaskbarExperience(_configuration.Current.TaskbarExperience with { ContentLayout = value });
    }

    public TaskbarMediaTextAlignment MediaTextAlignment
    {
        get => _configuration.Current.TaskbarExperience.MediaTextAlignment;
        set => UpdateTaskbarExperience(_configuration.Current.TaskbarExperience with { MediaTextAlignment = value });
    }

    public double ComponentSpacingDip
    {
        get => _configuration.Current.TaskbarExperience.ComponentSpacingDip;
        set => UpdateTaskbarExperience(_configuration.Current.TaskbarExperience with { ComponentSpacingDip = value });
    }

    public double HoverButtonSpacingDip
    {
        get => _configuration.Current.TaskbarExperience.HoverButtonSpacingDip;
        set => UpdateTaskbarExperience(_configuration.Current.TaskbarExperience with { HoverButtonSpacingDip = value });
    }

    public bool FollowMediaTextLength
    {
        get => _configuration.Current.TaskbarExperience.LengthMode == TaskbarLengthMode.FollowContent;
        set => UpdateTaskbarExperience(_configuration.Current.TaskbarExperience with
        {
            LengthMode = value ? TaskbarLengthMode.FollowContent : TaskbarLengthMode.Fixed,
            FixedLengthDip = value
                ? _configuration.Current.TaskbarExperience.FixedLengthDip
                : Math.Clamp(_configuration.Current.TaskbarExperience.FixedLengthDip,
                    FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum)
        });
    }

    public bool UsesFixedTaskbarLength => !FollowMediaTextLength;
    public double FixedTaskbarLengthMinimum => Math.Ceiling(_taskbarLengthConstraints.MinimumLengthDip);
    public double FixedTaskbarLengthMaximum => Math.Max(FixedTaskbarLengthMinimum, Math.Floor(_taskbarLengthConstraints.MaximumLengthDip));

    public double FixedTaskbarLengthDip
    {
        get => Math.Clamp(_configuration.Current.TaskbarExperience.FixedLengthDip,
            FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum);
        set => UpdateTaskbarExperience(_configuration.Current.TaskbarExperience with
        {
            FixedLengthDip = Math.Clamp(value, FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum)
        });
    }

    public string FixedTaskbarLengthRangeText =>
        Translations.Format("DisplayModes.Width.RangeText", FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum);

    public ObservableCollection<TaskbarRestComponentSettingItem> RestOrderEntries { get; } = [];

    private void UpdateTaskbarExperience(TaskbarExperienceSettings value)
    {
        if (_disposed || !_configuration.IsActive || _isRefreshing || value.Equals(_configuration.Current.TaskbarExperience))
            return;

        _configuration.SetTaskbarExperience(value.Normalize());
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
        OnPropertyChanged(nameof(FixedTaskbarLengthMinimum));
        OnPropertyChanged(nameof(FixedTaskbarLengthMaximum));
        OnPropertyChanged(nameof(FixedTaskbarLengthDip));
        OnPropertyChanged(nameof(FixedTaskbarLengthRangeText));
        RefreshRestOrderEntries();
    }

    private void OnTaskbarLengthConstraintsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(FixedTaskbarLengthMinimum));
        OnPropertyChanged(nameof(FixedTaskbarLengthMaximum));
        OnPropertyChanged(nameof(FixedTaskbarLengthDip));
        OnPropertyChanged(nameof(FixedTaskbarLengthRangeText));
    }

    private void RefreshRestOrderEntries()
    {
        var order = TaskbarRestLayoutPolicy.ResolveOrder(_configuration.Current.TaskbarExperience.RestComponentOrder);
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
        UpdateTaskbarExperience(_configuration.Current.TaskbarExperience with
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

    /// <summary>Restores only taskbar appearance, retaining application theme and interface fonts.</summary>
    public void ResetAppearance() { if (!_disposed && _configuration.IsActive) SettingsManager.ResetTaskbarAppearance(); }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || !_configuration.IsActive) return;
        _isRefreshing = true;
        try { RaiseTaskbarAppearance(); OnPropertyChanged(nameof(PlayerForegroundMode)); OnPropertyChanged(nameof(MediaFontSizePercent)); }
        finally { _isRefreshing = false; }
    }
    private void OnLanguageChanged(object? sender, EventArgs args) { if (!_disposed) { RaiseTaskbarAppearance(); OnPropertyChanged(string.Empty); } }
    /// <summary>Releases subscriptions owned by this page scope.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _taskbarLengthConstraints.Changed -= OnTaskbarLengthConstraintsChanged;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
