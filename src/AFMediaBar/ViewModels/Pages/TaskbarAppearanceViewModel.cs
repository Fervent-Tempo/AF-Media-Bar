// 提供共用媒体栏外观与当前模式的几何；页面作用域负责释放订阅。
using AFMediaBar.Classes.Models;
using System.Collections.ObjectModel;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.Input;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>提供共用媒体栏外观与当前模式布局，分别由两个设置页组合呈现。</summary>
public partial class TaskbarAppearanceViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    private readonly TaskbarLengthConstraintsService _taskbarLengthConstraints;
    /// <summary>Cancels asynchronous UI work when this editor context becomes inactive.</summary>
    public CancellationToken ContextCancellationToken => _configuration.CancellationToken;

    private readonly LocalizationService _localization;
    private readonly bool _hasFloatingBudget;
    private bool _isRefreshing;
    private bool _disposed;
    private string _idleNoteColorHex;

    /// <summary>Creates a taskbar-only appearance editor for the cached page scope.</summary>
    public TaskbarAppearanceViewModel(ISettingsConfiguration configuration, TaskbarLengthConstraintsService constraints, LocalizationService localization, FloatingBarLengthConstraints? floatingConstraints = null)
    {
        _configuration = configuration;
        _idleNoteColorHex = configuration.Current.Appearance.IdleNoteColor ?? configuration.Current.Appearance.AccentColor;
        configuration.Activated += OnActivated;
        _hasFloatingBudget = configuration.Target == BarConfigurationTarget.FloatingBar && floatingConstraints is not null;
        if (_hasFloatingBudget) constraints = floatingConstraints!.Range;
        _taskbarLengthConstraints = constraints;
        _localization = localization;
        constraints.Changed += OnTaskbarLengthConstraintsChanged;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        localization.LanguageChanged += OnLanguageChanged;
        RefreshRestOrderEntries();
    }

    /// <summary>媒体文字色在任务栏与桌面悬浮模式之间共用。</summary>
    public PlayerForegroundMode PlayerForegroundMode
    {
        get => _configuration.Current.Appearance.PlayerForegroundMode;
        set => _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { PlayerForegroundMode = value });
    }

    /// <summary>关闭自定义色后小音符跟随强调色，不修改媒体封面的取色。</summary>
    public bool UseCustomIdleNoteColor
    {
        get => _configuration.Current.Appearance.IdleNoteColor is not null;
        set
        {
            if (_disposed || !_configuration.IsActive || _isRefreshing || value == UseCustomIdleNoteColor) return;
            var color = ColorHex.TryParse(_idleNoteColorHex, out var parsed)
                ? ColorHex.Format(parsed) : AppearanceSettings.DefaultAccentColorHex;
            _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { IdleNoteColor = value ? color : null });
        }
    }

    /// <summary>保留未输入完整的文本，只在颜色有效且开启自定义时写入设置。</summary>
    public string IdleNoteColorHex
    {
        get => _idleNoteColorHex;
        set
        {
            if (_disposed || !_configuration.IsActive) return;
            if (!SetProperty(ref _idleNoteColorHex, value ?? string.Empty)) return;
            if (_isRefreshing || !UseCustomIdleNoteColor ||
                !ColorHex.TryParse(_idleNoteColorHex, out var color)) return;
            _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { IdleNoteColor = ColorHex.Format(color) });
        }
    }

    [RelayCommand]
    private void SetIdleNoteColor(string? hex)
    {
        if (_disposed || !_configuration.IsActive || !ColorHex.TryParse(hex, out var color)) return;
        _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { IdleNoteColor = ColorHex.Format(color) });
    }

    [RelayCommand]
    private void ResetIdleNoteColor() => UseCustomIdleNoteColor = false;

    private void RefreshIdleNoteColor()
    {
        if (_configuration.Current.Appearance.IdleNoteColor is { } hex)
            SetProperty(ref _idleNoteColorHex, hex, nameof(IdleNoteColorHex));
        OnPropertyChanged(nameof(UseCustomIdleNoteColor));
    }
    /// <summary>Media text scale, independent of the settings window's font selectors.</summary>
    public int MediaFontSizePercent
    {
        get => _configuration.Current.TaskbarExperience.Normalize().MediaFontSizePercent;
        set => _configuration.SetTaskbarExperience(_configuration.Current.TaskbarExperience with { MediaFontSizePercent = value });
    }
    /// <summary>Whether horizontal-only ordering and spacing controls are meaningful for this environment.</summary>
    public bool IsHorizontalLayout => _configuration.Context.Orientation != AFMediaBar.Classes.Models.Layout.LayoutOrientation.Vertical;
    public bool CanConfigureBarLayout => _configuration.Context.Mode is AFMediaBar.Classes.Models.Settings.SettingsMode.Taskbar
        or AFMediaBar.Classes.Models.Settings.SettingsMode.FloatingBar;

    /// <summary>是否启用当前任务栏的背景层。</summary>
    public bool UseFrostedTaskbarBackground
    {
        get => _configuration.Current.Appearance.TaskbarBackgroundMaterial == TaskbarBackgroundMaterial.Frosted;
        set => _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with
        {
            TaskbarBackgroundMaterial = value ? TaskbarBackgroundMaterial.Frosted : TaskbarBackgroundMaterial.Transparent
        });
    }

    public bool ShowTaskbarBackgroundWhenIdle
    {
        get => _configuration.Current.Appearance.ShowTaskbarBackgroundWhenIdle;
        set
        {
            if (_disposed || !_configuration.IsActive || _isRefreshing || value == ShowTaskbarBackgroundWhenIdle) return;
            _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { ShowTaskbarBackgroundWhenIdle = value });
        }
    }

    public bool ShowTaskbarBackgroundTint
    {
        get => _configuration.Current.Appearance.ShowTaskbarBackgroundTint;
        set
        {
            if (_disposed || !_configuration.IsActive || _isRefreshing || value == ShowTaskbarBackgroundTint) return;
            _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { ShowTaskbarBackgroundTint = value });
        }
    }

    public bool ShowTaskbarBackgroundBorder
    {
        get => _configuration.Current.Appearance.ShowTaskbarBackgroundBorder;
        set
        {
            if (_disposed || !_configuration.IsActive || _isRefreshing || value == ShowTaskbarBackgroundBorder) return;
            _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { ShowTaskbarBackgroundBorder = value });
        }
    }

    public bool ShowTaskbarBackgroundTexture
    {
        get => _configuration.Current.Appearance.ShowTaskbarBackgroundTexture;
        set
        {
            if (_disposed || !_configuration.IsActive || _isRefreshing || value == ShowTaskbarBackgroundTexture) return;
            _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { ShowTaskbarBackgroundTexture = value });
        }
    }

    /// <summary>任务栏背景层的风格。</summary>
    public TaskbarFrostedStyle TaskbarFrostedStyle
    {
        get => _configuration.Current.Appearance.TaskbarFrostedStyle;
        set => _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { TaskbarFrostedStyle = value });
    }
    /// <summary>任务栏背景层的浓度，范围与步长由设置模型约束。</summary>
    public int TaskbarBackgroundOpacityPercent
    {
        get => _configuration.Current.Appearance.ResolveTaskbarBackgroundOpacityPercent();
        set => _configuration.SetTaskbarAppearance(_configuration.Current.Appearance with { TaskbarBackgroundOpacityPercent = value });
    }

    public bool CanEditTaskbarBackgroundOpacity => UseFrostedTaskbarBackground && ShowTaskbarBackgroundTint;

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
            FixedLengthDip = _configuration.Current.TaskbarExperience.FixedLengthDip
        });
    }

    /// <summary>整体长度方式与已有布尔入口共用同一配置字段。</summary>
    public TaskbarLengthMode LengthMode
    {
        get => _configuration.Current.TaskbarExperience.LengthMode;
        set => FollowMediaTextLength = value == TaskbarLengthMode.FollowContent;
    }

    public bool UsesFixedTaskbarLength => !FollowMediaTextLength;
    private bool UsesStoredFixedLengthRange => !_taskbarLengthConstraints.HasSources;
    private bool HasFixedTaskbarLengthRange => (_configuration.Target == BarConfigurationTarget.Taskbar || _hasFloatingBudget) &&
        (UsesStoredFixedLengthRange || _taskbarLengthConstraints.HasAvailableRange) &&
        FixedTaskbarLengthMinimum <= FixedTaskbarLengthMaximum;
    public bool CanEditFixedTaskbarLength => UsesFixedTaskbarLength && HasFixedTaskbarLengthRange;
    public double FixedTaskbarLengthMinimum => UsesStoredFixedLengthRange
        ? TaskbarExperienceSettings.MinimumStoredFixedLengthDip
        : Math.Ceiling(Math.Max(TaskbarExperienceSettings.MinimumStoredFixedLengthDip, _taskbarLengthConstraints.MinimumLengthDip));
    public double FixedTaskbarLengthMaximum => UsesStoredFixedLengthRange
        ? TaskbarExperienceSettings.MaximumStoredFixedLengthDip
        : Math.Floor(Math.Min(TaskbarExperienceSettings.MaximumStoredFixedLengthDip, _taskbarLengthConstraints.MaximumLengthDip));

    public double FixedTaskbarLengthDip
    {
        get => HasFixedTaskbarLengthRange
            ? Math.Clamp(_configuration.Current.TaskbarExperience.FixedLengthDip, FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum)
            : _configuration.Current.TaskbarExperience.FixedLengthDip;
        set
        {
            // Binding coercion after an environment change is presentation, not a new user preference.
            if (_isRefreshing || !HasFixedTaskbarLengthRange || !double.IsFinite(value) || value == FixedTaskbarLengthDip)
                return;
            UpdateTaskbarExperience(_configuration.Current.TaskbarExperience with
            {
                FixedLengthDip = Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), FixedTaskbarLengthMinimum, FixedTaskbarLengthMaximum)
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
        if (_disposed || !_configuration.IsActive || _isRefreshing || value.Equals(_configuration.Current.TaskbarExperience))
            return;

        _configuration.SetTaskbarExperience(value.Normalize());
    }

    private void RaiseTaskbarAppearance()
    {
        OnPropertyChanged(nameof(LengthMode));
        OnPropertyChanged(nameof(UseFrostedTaskbarBackground));
        OnPropertyChanged(nameof(ShowTaskbarBackgroundWhenIdle));
        OnPropertyChanged(nameof(ShowTaskbarBackgroundTint));
        OnPropertyChanged(nameof(ShowTaskbarBackgroundBorder));
        OnPropertyChanged(nameof(ShowTaskbarBackgroundTexture));
        OnPropertyChanged(nameof(TaskbarFrostedStyle));
        OnPropertyChanged(nameof(TaskbarBackgroundOpacityPercent));
        OnPropertyChanged(nameof(CanEditTaskbarBackgroundOpacity));
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
        if (_disposed || !_configuration.IsActive) return;
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
        var order = TaskbarRestLayoutPolicy.ResolveOrder(_configuration.Current.TaskbarExperience.RestComponentOrder).ToArray();
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
        if (entry is null) return;
        ReorderRestComponent(new SettingsReorderRequest(entry, RestOrderEntries.IndexOf(entry) + offset, RestOrderEntries));
    }

    [RelayCommand]
    private void ReorderRestComponent(SettingsReorderRequest? request)
    {
        if (_disposed || !_configuration.IsActive || request?.Item is not TaskbarRestComponentSettingItem { CanMove: true } ||
            !request.TryResolve(RestOrderEntries, entry => entry.Component, out var index, out var target, TaskbarRestLayoutPolicy.FixedOrder.Count)) return;
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
    public void ResetAppearance() { if (!_disposed && _configuration.IsActive) _configuration.Reset(SettingsResetScope.Appearance); }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || !_configuration.IsActive) return;
        _isRefreshing = true;
        try
        {
            if (args.PropertyName == nameof(AppSettings.Appearance) || args.ResetScope is SettingsResetScope.Appearance or SettingsResetScope.All)
                RefreshIdleNoteColor();
            RaiseTaskbarAppearance();
            OnPropertyChanged(nameof(PlayerForegroundMode));
            OnPropertyChanged(nameof(MediaFontSizePercent));
        }
        finally { _isRefreshing = false; }
    }
    private void OnLanguageChanged(object? sender, EventArgs args) { if (!_disposed) { RaiseTaskbarAppearance(); OnPropertyChanged(string.Empty); } }
    private void OnActivated(object? sender, EventArgs e) { RefreshIdleNoteColor(); RefreshRestOrderEntries(); OnPropertyChanged(string.Empty); }
    /// <summary>Releases subscriptions owned by this page scope.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _configuration.Activated -= OnActivated;
        _taskbarLengthConstraints.Changed -= OnTaskbarLengthConstraintsChanged;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
