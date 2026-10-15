// 协调实验开关与保存的宿主偏好，不直接创建窗口或执行音频操作。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>实验页状态，关闭项目保留其配置。</summary>
public sealed class ExperimentalViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    private readonly BarRuntimeCoordinator? _runtime;
    public ExperimentalViewModel(ISettingsConfiguration configuration, BarRuntimeCoordinator? runtime = null)
    {
        _configuration = configuration;
        _runtime = runtime;
        if (_runtime is not null) _runtime.Changed += RuntimeChanged;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    public bool NewAudioControl
    {
        get => SettingsManager.Current.Experiments.NewAudioControl;
        set => _configuration.SetExperiments(SettingsManager.Current.Experiments with { NewAudioControl = value });
    }
    public bool VerticalTaskbar
    {
        get => SettingsManager.Current.Experiments.VerticalTaskbar;
        set => _configuration.SetExperiments(SettingsManager.Current.Experiments with { VerticalTaskbar = value });
    }
    public bool FloatingBar
    {
        get => SettingsManager.Current.Experiments.FloatingBar;
        set => _configuration.SetExperiments(SettingsManager.Current.Experiments with { FloatingBar = value });
    }
    public int RunningModeSelection
    {
        get => SettingsManager.Current.WindowMode == WindowMode.FloatingBar ? 1 : 0;
        set => _configuration.SetWindowMode(value == 1 ? WindowMode.FloatingBar : WindowMode.Taskbar);
    }
    public int FloatingOrientation
    {
        get => SettingsManager.Current.FloatingBar.Orientation == LayoutOrientation.Vertical ? 1 : 0;
        set => _configuration.SetFloatingBar(SettingsManager.Current.FloatingBar with { Orientation = value == 1 ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal });
    }
    public int FloatingLayer
    {
        get => SettingsManager.Current.FloatingBar.Layer == FloatingBarLayer.Topmost ? 1 : 0;
        set => _configuration.SetFloatingBar(SettingsManager.Current.FloatingBar with { Layer = value == 1 ? FloatingBarLayer.Topmost : FloatingBarLayer.Desktop });
    }
    public bool FloatingLocked
    {
        get => SettingsManager.Current.FloatingBar.Profile?.TaskbarBarPositionLocked == true;
        set
        {
            var settings = SettingsManager.Current.FloatingBar;
            _configuration.SetFloatingBar(settings with { Profile = (settings.Profile ?? BarDisplaySettings.Capture(SettingsManager.Current)) with { TaskbarBarPositionLocked = value } });
        }
    }
    public string RuntimeStatus => _runtime?.IsSwitching == true ? Translations.Get("Experimental.Floating.Switching") :
        _runtime?.LastFailure is not null ? Translations.Get("Experimental.Floating.Failed") :
        Translations.Get(_runtime?.RunningMode == WindowMode.FloatingBar ? "Experimental.Floating.Active" : "Experimental.Floating.TaskbarActive");
    private void RuntimeChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(RuntimeStatus));
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(AppSettings.Experiments) or nameof(AppSettings.FloatingBar) or nameof(AppSettings.WindowMode))
        {
            OnPropertyChanged(nameof(NewAudioControl));
            OnPropertyChanged(nameof(VerticalTaskbar));
            OnPropertyChanged(nameof(FloatingBar));
            OnPropertyChanged(nameof(RunningModeSelection));
            OnPropertyChanged(nameof(FloatingOrientation));
            OnPropertyChanged(nameof(FloatingLayer));
            OnPropertyChanged(nameof(FloatingLocked));
            OnPropertyChanged(nameof(RuntimeStatus));
        }
    }
    public void Dispose()
    {
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        if (_runtime is not null) _runtime.Changed -= RuntimeChanged;
    }
}
