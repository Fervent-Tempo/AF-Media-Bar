// Owns spectrum and performance settings for one page scope; it releases settings and language subscriptions on disposal.
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>Component-specific view model, independent of media discovery, launchers, and notifications.</summary>
public sealed class ComponentsSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    private readonly LocalizationService _localization;
    private readonly Dispatcher _dispatcher = DispatcherHelper.Current;
    private bool _disposed;

    /// <summary>Creates one component editor for its page context and observes only settings and language.</summary>
    public ComponentsSettingsViewModel(ISettingsConfiguration configuration, LocalizationService localization)
    {
        _configuration = configuration;
        _localization = localization;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        localization.LanguageChanged += OnLanguageChanged;
    }
    public int SpectrumBandCount
    {
        get => _configuration.Current.SpectrumComponent.BandCount;
        set { _configuration.SetSpectrum(_configuration.Current.SpectrumComponent with { BandCount = value }); OnPropertyChanged(); OnPropertyChanged(nameof(SpectrumBandCountText)); }
    }

    /// <summary>柱数滑杆旁的读数。/ The reading next to the bar-count slider.</summary>
    public string SpectrumBandCountText => Translations.Format("Media.Spectrum.BandCount.Value", SpectrumBandCount);

    public int SpectrumRefreshRateHz
    {
        get => _configuration.Current.SpectrumComponent.RefreshRateHz;
        set { _configuration.SetSpectrum(_configuration.Current.SpectrumComponent with { RefreshRateHz = value }); OnPropertyChanged(); }
    }

    public int SpectrumSensitivityPercent
    {
        get => _configuration.Current.SpectrumComponent.SensitivityPercent;
        set { _configuration.SetSpectrum(_configuration.Current.SpectrumComponent with { SensitivityPercent = value }); OnPropertyChanged(); }
    }

    /// <summary>
    /// 频谱呈现样式。柱数决定频谱占用宽度，样式只决定这些宽度怎么画，因此两者互不影响。
    /// Spectrum presentation style. The bar count decides the width the spectrum occupies and the style only decides how that
    /// width is painted, so neither interferes with the other.
    /// </summary>
    public SpectrumStyle SpectrumStyle
    {
        get => _configuration.Current.SpectrumComponent.Style;
        set { _configuration.SetSpectrum(_configuration.Current.SpectrumComponent with { Style = value }); OnPropertyChanged(); }
    }

    /// <summary>
    /// 频谱内容区的横轴尺寸（横向任务栏就是高度）。它只影响柱子能长多高，宽度仍由柱数决定。
    /// Cross-axis size of the spectrum content area, which is the height on a horizontal taskbar. It only decides how tall the bars can grow;
    /// the width still follows the bar count.
    /// </summary>
    public double SpectrumContentHeightDip
    {
        get => SpectrumComponentSettings.SnapContentHeightDip(_configuration.Current.SpectrumComponent.ContentHeightDip);
        set
        {
            var dip = SpectrumComponentSettings.SnapContentHeightDip(value);
            _configuration.SetSpectrum(_configuration.Current.SpectrumComponent with { ContentHeightDip = dip });
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpectrumContentHeightText));
        }
    }

    /// <summary>频谱尺寸滑杆旁的读数。/ The reading next to the spectrum-size slider.</summary>
    public string SpectrumContentHeightText => Translations.Format("Media.Spectrum.ContentHeight.Value", SpectrumContentHeightDip);

    /// <summary>频率尺寸滑杆的下限，来自持久化常量而不是界面字面量。 / Lower bound of the spectrum-size slider, taken from the persistence constant rather than a UI literal.</summary>
    public double MinimumSpectrumContentHeightDip => SpectrumComponentSettings.MinimumContentHeightDip;

    /// <inheritdoc cref="MinimumSpectrumContentHeightDip" />
    public double MaximumSpectrumContentHeightDip => SpectrumComponentSettings.MaximumContentHeightDip;

    /// <inheritdoc cref="MinimumSpectrumContentHeightDip" />
    public double SpectrumContentHeightStepDip => SpectrumComponentSettings.ContentHeightStepDip;

    /// <summary>柱数滑杆的下限，来自持久化常量而不是界面字面量。 / Lower bound of the bar-count slider, taken from the persistence constant rather than a UI literal.</summary>
    public int MinimumSpectrumBandCount => SpectrumComponentSettings.MinimumBandCount;

    /// <inheritdoc cref="MinimumSpectrumBandCount" />
    public int MaximumSpectrumBandCount => SpectrumComponentSettings.MaximumBandCount;

    /// <summary>灵敏度滑杆的下限，来自持久化常量而不是界面字面量。 / Lower bound of the sensitivity slider, taken from the persistence constant rather than a UI literal.</summary>
    public int MinimumSpectrumSensitivityPercent => SpectrumComponentSettings.MinimumSensitivityPercent;

    /// <inheritdoc cref="MinimumSpectrumBandCount" />
    public int MaximumSpectrumSensitivityPercent => SpectrumComponentSettings.MaximumSensitivityPercent;

    /// <inheritdoc cref="MinimumSpectrumBandCount" />
    public int SpectrumSensitivityStepPercent => SpectrumComponentSettings.SensitivityStepPercent;

    /// <summary>
    /// 性能组件的采样间隔，界面以秒为单位。设置里存的仍是毫秒，写入前吸附到滑杆步长上，
    /// 因此读数与滑杆位置永远一致。
    /// Sampling interval of the performance component, expressed in seconds for the interface. The stored value stays in
    /// milliseconds and is snapped onto the slider step before it is written, so the reading and the slider position always
    /// agree.
    /// </summary>
    public double PerformanceRefreshIntervalSeconds
    {
        get => _configuration.Current.PerformanceComponent.RefreshIntervalMilliseconds / 1000d;
        set
        {
            var milliseconds = PerformanceComponentSettings.SnapRefreshIntervalMilliseconds((int)Math.Round(value * 1000));
            _configuration.SetPerformance(
                _configuration.Current.PerformanceComponent with { RefreshIntervalMilliseconds = milliseconds });
            OnPropertyChanged();
            OnPropertyChanged(nameof(PerformanceRefreshIntervalText));
        }
    }

    /// <summary>采样间隔滑杆旁的读数，带单位。/ The reading next to the sampling-interval slider, with its unit.</summary>
    public string PerformanceRefreshIntervalText => Translations.Format("Media.Performance.RefreshInterval.Value", PerformanceRefreshIntervalSeconds);

    /// <summary>采样间隔滑杆的下限（秒）。 / Lower bound of the sampling-interval slider, in seconds.</summary>
    public double MinimumPerformanceRefreshIntervalSeconds => PerformanceComponentSettings.MinimumRefreshIntervalMilliseconds / 1000d;

    /// <inheritdoc cref="MinimumPerformanceRefreshIntervalSeconds" />
    public double MaximumPerformanceRefreshIntervalSeconds => PerformanceComponentSettings.MaximumRefreshIntervalMilliseconds / 1000d;

    /// <inheritdoc cref="MinimumPerformanceRefreshIntervalSeconds" />
    public double PerformanceRefreshIntervalStepSeconds => PerformanceComponentSettings.RefreshIntervalStepMilliseconds / 1000d;

    public bool OpenTaskManagerOnMetricsClick
    {
        get => _configuration.Current.PerformanceComponent.OpenTaskManagerOnClick;
        set { _configuration.SetPerformance(_configuration.Current.PerformanceComponent with { OpenTaskManagerOnClick = value }); OnPropertyChanged(); }
    }

    public bool ShowSystemMemory { get => HasMetric(MetricKind.SystemMemory); set => SetMetric(MetricKind.SystemMemory, value); }
    public bool ShowSystemCpu { get => HasMetric(MetricKind.SystemCpu); set => SetMetric(MetricKind.SystemCpu, value); }
    public bool ShowSystemGpu { get => HasMetric(MetricKind.SystemGpu); set => SetMetric(MetricKind.SystemGpu, value); }
    public bool ShowProcessMemory { get => HasMetric(MetricKind.ProcessMemory); set => SetMetric(MetricKind.ProcessMemory, value); }

    /// <summary>
    /// 该指标当前能否取消勾选。性能组件至少需要一个指标，因此最后一个勾选项的复选框必须禁用；
    /// 原实现只是静默忽略取消操作、复选框却照常可点，用户会以为界面失灵。
    /// Whether a metric may currently be unchecked. The performance component needs at least one metric, so the
    /// last checked box must be disabled; the previous behaviour silently ignored the click while leaving the
    /// box enabled, which read as a broken control.
    /// </summary>
    public bool CanUncheckSystemMemory => CanUncheck(MetricKind.SystemMemory);

    /// <inheritdoc cref="CanUncheckSystemMemory" />
    public bool CanUncheckSystemCpu => CanUncheck(MetricKind.SystemCpu);

    /// <inheritdoc cref="CanUncheckSystemMemory" />
    public bool CanUncheckSystemGpu => CanUncheck(MetricKind.SystemGpu);

    /// <inheritdoc cref="CanUncheckSystemMemory" />
    public bool CanUncheckProcessMemory => CanUncheck(MetricKind.ProcessMemory);

    /// <summary>
    /// 频谱组件是否显示在静置层。它和参数放在同一页，这样“这个组件要不要用”和“它怎么表现”
    /// 不会分处两个页面。
    /// Whether the spectrum component shows on the rest layer. It lives on the same page as its parameters so
    /// "should this component exist" and "how does it behave" are never split across two pages.
    /// </summary>
    public bool SpectrumVisible
    {
        get => _configuration.Current.TaskbarExperience.SpectrumVisible;
        set
        {
            _configuration.SetTaskbarExperience(
                _configuration.Current.TaskbarExperience with { SpectrumVisible = value });
            OnPropertyChanged();
        }
    }

    /// <inheritdoc cref="SpectrumVisible" />
    public bool PerformanceVisible
    {
        get => _configuration.Current.TaskbarExperience.PerformanceVisible;
        set
        {
            _configuration.SetTaskbarExperience(
                _configuration.Current.TaskbarExperience with { PerformanceVisible = value });
            OnPropertyChanged();
        }
    }

    private bool HasMetric(MetricKind metric) => _configuration.Current.PerformanceComponent.Metrics!.Contains(metric);

    private bool CanUncheck(MetricKind metric) =>
        !HasMetric(metric) || _configuration.Current.PerformanceComponent.Metrics!.Count > 1;

    private void SetMetric(MetricKind metric, bool enabled)
    {
        var metrics = _configuration.Current.PerformanceComponent.Metrics!.ToList();
        if (enabled && !metrics.Contains(metric)) metrics.Add(metric);
        if (!enabled && metrics.Count > 1) metrics.Remove(metric);
        _configuration.SetPerformance(_configuration.Current.PerformanceComponent with { Metrics = metrics });
        RaiseMetricProperties();
    }

    private void RaiseMetricProperties()
    {
        OnPropertyChanged(nameof(ShowSystemMemory));
        OnPropertyChanged(nameof(ShowSystemCpu));
        OnPropertyChanged(nameof(ShowSystemGpu));
        OnPropertyChanged(nameof(ShowProcessMemory));
        OnPropertyChanged(nameof(CanUncheckSystemMemory));
        OnPropertyChanged(nameof(CanUncheckSystemCpu));
        OnPropertyChanged(nameof(CanUncheckSystemGpu));
        OnPropertyChanged(nameof(CanUncheckProcessMemory));
    }
    /// <summary>Restores only spectrum and performance defaults through the existing reset contract.</summary>
    public void ResetComponents() { if (!_disposed && _configuration.IsActive) SettingsManager.ResetComponents(); }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args) => DispatcherHelper.Run(_dispatcher, () =>
    {
        if (_disposed || !_configuration.IsActive) return;
        OnPropertyChanged(string.Empty);
    });
    private void OnLanguageChanged(object? sender, EventArgs args) { if (!_disposed) OnPropertyChanged(string.Empty); }
    /// <summary>Unsubscribes when the cached page scope is removed.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
