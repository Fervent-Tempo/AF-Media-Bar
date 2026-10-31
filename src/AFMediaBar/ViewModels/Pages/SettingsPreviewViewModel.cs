// 预览只投影当前配置和固定内容，不持有媒体、设备、采样或动画资源。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using System.ComponentModel;
using System.Windows.Threading;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>预览中的有序组件；性能轮换固定展示首项。</summary>
public sealed record SettingsPreviewItem(TaskbarRestComponent Component, MetricKind? Metric = null);
/// <summary>预览对应的实际呈现层，不参与配置持久化。</summary>
public enum SettingsPreviewMode { Rest, QuickControls, FullPanel }
/// <summary>作用域内的只读预览投影，释放后不再发布结果。</summary>
public sealed class SettingsPreviewViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    private readonly LocalizationService _localization;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _isDisposed;
    private bool _showIdle;
    private SettingsPreviewMode _mode;
    public SettingsPreviewMode Mode { get => _mode; set { if (SetProperty(ref _mode, value)) Refresh(); } }
    public bool IsRest => Mode == SettingsPreviewMode.Rest;
    public bool IsFull => Mode == SettingsPreviewMode.FullPanel;
    public string ScaleLabel => Translations.Format("Integration.Preview.Scale", Experience.FullPanel.ScalePercent);
    public bool ShowIdle { get => _showIdle; set { if (SetProperty(ref _showIdle, value)) Refresh(); } }
    public IReadOnlyList<SettingsPreviewItem> Items { get; private set; } = [];
    public TaskbarExperienceSettings Experience { get; private set; } = TaskbarExperienceSettings.Default;
    public AppearanceSettings Appearance { get; private set; } = AppearanceSettings.Default;
    public PerformanceComponentSettings Performance { get; private set; } = PerformanceComponentSettings.Default;
    public SpectrumComponentSettings Spectrum { get; private set; } = SpectrumComponentSettings.Default;
    public double LengthScale { get; private set; } = 1d;
    public double ThicknessScale { get; private set; } = 1d;
    public bool IsVertical { get; private set; }
    public bool IsEmpty => Items.Count == 0;
    public string EmptyText => Translations.Get(IsRest ? "Settings.Preview.Empty" : "Settings.Preview.LayerDisabled");

    public SettingsPreviewViewModel(ISettingsConfiguration configuration, LocalizationService localization)
    {
        _configuration = configuration;
        _localization = localization;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        configuration.Activated += OnActivated;
        localization.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_isDisposed || !_configuration.IsActive) return;
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(Refresh); return; }
        var current = _configuration.Current;
        Experience = current.TaskbarExperience.Normalize();
        Appearance = current.Appearance;
        Performance = current.PerformanceComponent.Normalize();
        Spectrum = current.SpectrumComponent.Normalize();
        LengthScale = current.LayoutLengthScalePercent / 100d;
        ThicknessScale = current.LayoutThicknessScalePercent / 100d;
        IsVertical = _configuration.Context.Orientation == AFMediaBar.Classes.Models.Layout.LayoutOrientation.Vertical;
        var state = Mode == SettingsPreviewMode.QuickControls ? BarLayoutState.Hover : ShowIdle ? BarLayoutState.Idle : BarLayoutState.Resident;
        Items = (Mode == SettingsPreviewMode.QuickControls && !Experience.HoverLayerEnabled ? [] : ComponentLayoutPolicy.Resolve(current, state))
            .Where(entry => entry.IsVisible && (entry.Component != TaskbarRestComponent.BluetoothBattery || current.Experiments.BluetoothBattery))
            .Select(entry => new SettingsPreviewItem(entry.Component, ComponentLayoutPolicy.IsMetric(entry.Component) ? ComponentLayoutPolicy.Metric(entry.Component) :
                entry.Component == TaskbarRestComponent.Performance ? Performance.GetOrderedMetrics().First() : null)).ToArray();
        OnPropertyChanged(new PropertyChangedEventArgs(string.Empty));
    }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args) => Refresh();
    private void OnActivated(object? sender, EventArgs args) => Refresh();
    private void OnLanguageChanged(object? sender, EventArgs args) => Refresh();
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _configuration.Activated -= OnActivated;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
