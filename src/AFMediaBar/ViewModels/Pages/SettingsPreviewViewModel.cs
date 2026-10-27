// 局部预览只读取当前设置并生成固定示例；不连接播放器、采集器或计时器。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using System.ComponentModel;
using System.Windows;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>设置示意图中的一个可见组件。</summary>
public sealed record SettingsPreviewItem(TaskbarRestComponent Component, string Text, string Detail);

/// <summary>常驻状态和个性化共用的只读示例投影，订阅由页面作用域释放。</summary>
public sealed partial class SettingsPreviewViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    private readonly LocalizationService _localization;
    private bool _disposed;
    private bool _showIdle;

    public bool ShowIdle
    {
        get => _showIdle;
        set { if (SetProperty(ref _showIdle, value)) Refresh(); }
    }
    public IReadOnlyList<SettingsPreviewItem> Items { get; private set; } = [];
    public bool IsEmpty => Items.Count == 0;
    public bool ShowProgress => !ShowIdle && _configuration.Current.TaskbarExperience.RestProgressVisible;
    public bool SmallArtwork => !ShowIdle && _configuration.Current.TaskbarExperience.SmallArtwork;
    public bool ArtworkProgress => !ShowIdle && _configuration.Current.TaskbarExperience.ArtworkProgressVisible;
    public double MediaFontSize => 14d * _configuration.Current.TaskbarExperience.MediaFontSizePercent / 100d;
    public double Spacing => _configuration.Current.TaskbarExperience.ComponentSpacingDip;
    public Thickness ItemMargin => new(0d, 0d, Math.Min(Spacing, 16d), 8d);

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
        if (_disposed || !_configuration.IsActive) return;
        var current = _configuration.Current;
        var rest = current.TaskbarExperience;
        var visibility = new TaskbarRestLayoutPolicy.Visibility(!ShowIdle, rest.IdleComponents,
            rest.SpectrumVisible, rest.PerformanceVisible, rest.OutputDeviceVisible, rest.VolumeVisible,
            rest.ArtworkVisible, rest.InputDeviceVisible,
            current.Experiments.BluetoothBattery && current.BluetoothBatteryWidget.Enabled && !string.IsNullOrEmpty(current.BluetoothBatteryWidget.DeviceId));
        Items = TaskbarRestLayoutPolicy.ResolveOrder(rest.RestComponentOrder)
            .Where(component => TaskbarRestLayoutPolicy.IsVisible(component, visibility))
            .Select(component => new SettingsPreviewItem(component, component switch
            {
                TaskbarRestComponent.Artwork => ShowIdle ? "♪" : "◒",
                TaskbarRestComponent.MediaText => Translations.Get("Settings.Preview.Track"),
                TaskbarRestComponent.Spectrum => "▂▅▃▇▆▃▅▂",
                TaskbarRestComponent.Performance => "43%",
                TaskbarRestComponent.Volume => "68%",
                TaskbarRestComponent.BluetoothBattery => "72%",
                _ => Translations.Get(component == TaskbarRestComponent.InputDevice ? "Common.InputDevice" : "Common.OutputDevice")
            }, component == TaskbarRestComponent.MediaText ? Translations.Get("Settings.Preview.Artist") :
                component == TaskbarRestComponent.Performance ? Translations.Get("Media.Metric.SystemMemory.Title") : string.Empty)).ToArray();
        OnPropertyChanged(new PropertyChangedEventArgs(string.Empty));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args) => Refresh();
    private void OnActivated(object? sender, EventArgs args) => Refresh();
    private void OnLanguageChanged(object? sender, EventArgs args) => Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _configuration.Activated -= OnActivated;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
