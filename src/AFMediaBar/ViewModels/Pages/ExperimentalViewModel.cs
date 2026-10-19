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

    public bool SystemMemoryCleanup
    {
        get => SettingsManager.Current.Experiments.SystemMemoryCleanup;
        set => _configuration.SetExperiments(SettingsManager.Current.Experiments with { SystemMemoryCleanup = value });
    }
    public bool MemoryTools
    {
        get => SettingsManager.Current.Experiments.MemoryTools;
        set => _configuration.SetExperiments(SettingsManager.Current.Experiments with { MemoryTools = value });
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
    public bool ExternalFullPanelHandle
    {
        get => SettingsManager.Current.Experiments.ExternalFullPanelHandle;
        set => _configuration.SetExperiments(SettingsManager.Current.Experiments with { ExternalFullPanelHandle = value });
    }
    public int ExternalFullPanelHandleTriggerIndex
    {
        get => (int)SettingsManager.Current.Experiments.Normalize().ExternalFullPanelHandleTrigger;
        set => _configuration.SetExperiments(SettingsManager.Current.Experiments with
        {
            ExternalFullPanelHandleTrigger = value == 1 ? FullPanelHandleTrigger.Hover : FullPanelHandleTrigger.Click
        });
    }
    public string RuntimeStatus => _runtime?.IsSwitching == true ? Translations.Get("Experimental.Floating.Switching") :
        _runtime?.LastFailure is not null ? Translations.Get("Experimental.Floating.Failed") :
        Translations.Get(_runtime?.RunningMode == WindowMode.FloatingBar ? "Experimental.Floating.Active" : "Experimental.Floating.TaskbarActive");
    private void RuntimeChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(RuntimeStatus));
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(AppSettings.Experiments) or nameof(AppSettings.FloatingBar) or nameof(AppSettings.WindowMode))
        {
            OnPropertyChanged(nameof(SystemMemoryCleanup));
            OnPropertyChanged(nameof(MemoryTools));
            OnPropertyChanged(nameof(NewAudioControl));
            OnPropertyChanged(nameof(VerticalTaskbar));
            OnPropertyChanged(nameof(FloatingBar));
            OnPropertyChanged(nameof(ExternalFullPanelHandle));
            OnPropertyChanged(nameof(ExternalFullPanelHandleTriggerIndex));
            OnPropertyChanged(nameof(RuntimeStatus));
        }
    }
    public void Dispose()
    {
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        if (_runtime is not null) _runtime.Changed -= RuntimeChanged;
    }
}
