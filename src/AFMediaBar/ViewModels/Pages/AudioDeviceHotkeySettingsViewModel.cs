// 交互页拥有录制状态和暂停注册租约；快捷键运行仍由应用级服务管理，页面关闭不注销正常快捷键。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Input;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>全局输入、输出循环快捷键编辑器，不枚举或持有音频设备。</summary>
public sealed class AudioDeviceHotkeySettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    private readonly AudioDeviceHotkeyService? _hotkeys;
    private readonly Dictionary<AudioDeviceDirection, IDisposable> _recordings = [];
    private bool _disposed;
    private bool _isRecordingOutputCycle;
    private bool _isRecordingInputCycle;
    public bool IsEnabled => !_disposed && _configuration.IsActive;
    public bool IsRecordingOutputCycle { get => _isRecordingOutputCycle; set { if (SetProperty(ref _isRecordingOutputCycle, value)) SetRecording(AudioDeviceDirection.Output, value); } }
    public bool IsRecordingInputCycle { get => _isRecordingInputCycle; set { if (SetProperty(ref _isRecordingInputCycle, value)) SetRecording(AudioDeviceDirection.Input, value); } }
    public string OutputCycleHotkey { get => _configuration.Current.AudioDevices.OutputCycleHotkey; set => SetHotkey(AudioDeviceDirection.Output, value); }
    public string InputCycleHotkey { get => _configuration.Current.AudioDevices.InputCycleHotkey; set => SetHotkey(AudioDeviceDirection.Input, value); }
    public string OutputCycleStatus => _hotkeys?.GetStatus(AudioDeviceDirection.Output, null) ?? string.Empty;
    public string InputCycleStatus => _hotkeys?.GetStatus(AudioDeviceDirection.Input, null) ?? string.Empty;
    public AudioDeviceHotkeySettingsViewModel(ISettingsConfiguration configuration, AudioDeviceHotkeyService? hotkeys = null)
    {
        _configuration = configuration; _hotkeys = hotkeys;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        if (hotkeys is not null) hotkeys.Changed += OnStatusChanged;
    }
    private void SetHotkey(AudioDeviceDirection direction, string value)
    {
        if (_disposed || !_configuration.IsActive || !IsEnabled) return;
        var settings = _configuration.Current.AudioDevices;
        _configuration.SetAudioDeviceSettings(direction == AudioDeviceDirection.Output
            ? settings with { OutputCycleHotkey = value } : settings with { InputCycleHotkey = value });
    }
    private void SetRecording(AudioDeviceDirection direction, bool recording)
    {
        if (_recordings.Remove(direction, out var previous)) previous.Dispose();
        if (!_disposed && _configuration.IsActive && recording && IsEnabled && _hotkeys is not null)
            _recordings[direction] = _hotkeys.PauseForRecording();
    }
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || args.PropertyName is not (null or nameof(AppSettings.Experiments) or nameof(AppSettings.AudioDevices))) return;
        if (!IsEnabled) { IsRecordingOutputCycle = false; IsRecordingInputCycle = false; }
        OnPropertyChanged(string.Empty);
    }
    private void OnStatusChanged(object? sender, EventArgs args) { if (!_disposed) OnPropertyChanged(string.Empty); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var lease in _recordings.Values) lease.Dispose();
        _recordings.Clear();
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        if (_hotkeys is not null) _hotkeys.Changed -= OnStatusChanged;
    }
}
