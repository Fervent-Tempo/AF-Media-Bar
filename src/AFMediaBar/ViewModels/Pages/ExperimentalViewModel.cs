// 协调实验开关与保存的宿主偏好，不直接创建窗口或执行音频操作。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>实验页状态，关闭项目保留其配置。</summary>
public sealed class ExperimentalViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    public ExperimentalViewModel(ISettingsConfiguration configuration)
    {
        _configuration = configuration;
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
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(AppSettings.Experiments))
        {
            OnPropertyChanged(nameof(NewAudioControl));
            OnPropertyChanged(nameof(VerticalTaskbar));
            OnPropertyChanged(nameof(FloatingBar));
        }
    }
    public void Dispose() => SettingsManager.SettingsChanged -= OnSettingsChanged;
}
