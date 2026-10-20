// 应用级快捷键拥有消息窗口和 RegisterHotKey 注册；停止时注销，音频操作仍交给切换协调器。
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using Microsoft.Extensions.Hosting;

namespace AFMediaBar.Classes.Services.Input;

/// <summary>系统设备快捷键的注册状态与执行入口；不安装键盘 Hook。</summary>
public sealed class AudioDeviceHotkeyService(AudioDeviceSwitchCoordinator switches) : IHostedService, IDisposable
{
    private sealed record Binding(string Identity, AudioDeviceDirection Direction, string Gesture);
    private readonly Dictionary<int, Binding> _registered = [];
    private readonly Dictionary<string, string> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly int[] _versions = new int[2];
    private readonly int[] _steps = new int[2];
    private HwndSource? _window;
    private CancellationTokenSource _requests = new();
    private string? _signature;
    private bool _disposed;
    private int _recordingCount;
    private sealed class RegistrationPause(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
    /// <summary>录制期间暂停 AF 注册，使已注册组合键仍能到达录制框；租约由页面状态释放。</summary>
    public IDisposable PauseForRecording()
    {
        if (_disposed) return new RegistrationPause(() => { });
        _recordingCount++;
        Reconfigure();
        return new RegistrationPause(() =>
        {
            if (_disposed) return;
            _recordingCount--;
            Reconfigure();
        });
    }
    public event EventHandler? Changed;
    public string GetStatus(AudioDeviceDirection direction, string? endpointId) =>
        _statuses.TryGetValue(Identity(direction, endpointId), out var key) ? Translations.Get(key) : string.Empty;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_disposed) return;
            SettingsManager.SettingsChanged += OnSettingsChanged;
            Reconfigure();
        });
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (_disposed || args.PropertyName is not (null or nameof(AppSettings.Experiments) or nameof(AppSettings.AudioDevices))) return;
        if (Application.Current.Dispatcher.CheckAccess()) Reconfigure();
        else Application.Current.Dispatcher.BeginInvoke(() => { if (!_disposed) Reconfigure(); });
    }

    private void Reconfigure()
    {
        if (_disposed) return;
        var settings = SettingsManager.Current;
        var enabled = _recordingCount == 0;
        var bindings = enabled ? new[] { new Binding(Identity(AudioDeviceDirection.Output, null), AudioDeviceDirection.Output, settings.AudioDevices.OutputCycleHotkey),
                new Binding(Identity(AudioDeviceDirection.Input, null), AudioDeviceDirection.Input, settings.AudioDevices.InputCycleHotkey) }
            .Where(binding => !string.IsNullOrWhiteSpace(binding.Gesture)).ToArray() : [];
        var signature = string.Join("|", bindings.Select(binding => $"{binding.Identity}={binding.Gesture}")) + ":" +
            string.Join("|", settings.AudioDevices.Preferences
                .Select(entry => $"{entry.Direction}:{entry.EndpointId}:{settings.AudioDevices.IsAllowed(entry.Direction, entry.EndpointId)}"));
        if (_signature == signature) return;
        _signature = signature;
        _requests.Cancel(); _requests.Dispose(); _requests = new();
        Array.Clear(_steps);
        for (var i = 0; i < _versions.Length; i++) _versions[i]++;
        if (_window is not null)
        {
            foreach (var id in _registered.Keys) UnregisterHotKey(_window.Handle, id);
            if (bindings.Length == 0) { _window.RemoveHook(OnMessage); _window.Dispose(); _window = null; }
        }
        if (bindings.Length > 0 && _window is null)
        {
            _window = new HwndSource(new HwndSourceParameters("AF device hotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
            _window.AddHook(OnMessage);
        }
        _registered.Clear(); _statuses.Clear();
        var parsed = bindings.Select(binding => (Binding: binding, Valid: TryParse(binding.Gesture, out var modifiers, out var key), Modifiers: modifiers, Key: key)).ToArray();
        var idCounter = 1;
        foreach (var item in parsed)
        {
            var status = !item.Valid ? "Device.Hotkey.Invalid" : parsed.Count(other => other.Valid && other.Modifiers == item.Modifiers && other.Key == item.Key) > 1
                ? "Device.Hotkey.Duplicate" : null;
            if (status is null)
            {
                var id = idCounter++;
                if (RegisterHotKey(_window!.Handle, id, item.Modifiers | 0x4000, item.Key)) _registered[id] = item.Binding;
                else status = "Device.Hotkey.Conflict";
            }
            _statuses[item.Binding.Identity] = status ?? "Device.Hotkey.Registered";
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0; virtualKey = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts[..^1])
        {
            var flag = part.ToUpperInvariant() switch { "CTRL" or "CONTROL" => 2u, "ALT" => 1u, "SHIFT" => 4u, _ => 0u };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        var name = parts[^1];
        if (name.Length == 1 && char.IsAsciiDigit(name[0])) name = "D" + name;
        if ((modifiers & 3) == 0 || !Enum.TryParse<Key>(name, true, out var key) || !Enum.IsDefined(key) ||
            key is Key.F12 or Key.None or Key.System or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_disposed && message == 0x0312 && _registered.TryGetValue(wParam.ToInt32(), out var binding))
        {
            handled = true;
            var direction = (int)binding.Direction;
            _steps[direction]++;
            _ = ExecuteAsync(binding, ++_versions[direction], _requests.Token);
        }
        return IntPtr.Zero;
    }

    private async Task ExecuteAsync(Binding binding, int version, CancellationToken token)
    {
        try
        {
            var target = new AudioDeviceTarget(binding.Direction);
            var choices = await switches.GetChoicesAsync(target, token);
            var direction = (int)binding.Direction;
            if (_disposed || token.IsCancellationRequested || version != _versions[direction]) return;
            var candidates = choices.Where(device => device.IsActive && !device.IsSystemDefaultChoice &&
                SettingsManager.Current.AudioDevices.IsAllowed(binding.Direction, device.PolicyId)).ToArray();
            var current = switches.GetPreview(target)?.PolicyId ?? choices.FirstOrDefault(device => device.IsDefault)?.PolicyId;
            var index = Array.FindIndex(candidates, device => device.PolicyId.Equals(current, StringComparison.OrdinalIgnoreCase));
            var steps = _steps[direction]; _steps[direction] = 0;
            var next = candidates.Length == 0 ? null : candidates[((index + steps) % candidates.Length + candidates.Length) % candidates.Length];
            if (next is null) { SetStatus(binding.Identity, "Device.Hotkey.Unavailable"); return; }
            var result = await switches.SelectAsync(target, next, this, token);
            if (_disposed || token.IsCancellationRequested || version != _versions[direction]) return;
            SetStatus(binding.Identity, result.Status is AudioDeviceOperationStatus.Applied or AudioDeviceOperationStatus.Unchanged
                ? "Device.Hotkey.Registered" : "Device.Hotkey.Failed");
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_disposed && !token.IsCancellationRequested && version == _versions[(int)binding.Direction]) SetStatus(binding.Identity, "Device.Hotkey.Failed"); }
    }

    private void SetStatus(string identity, string key) { _statuses[identity] = key; Changed?.Invoke(this, EventArgs.Empty); }
    private static string Identity(AudioDeviceDirection direction, string? endpointId) => $"{direction}:{endpointId ?? "cycle"}";

    public async Task StopAsync(CancellationToken cancellationToken) => await Application.Current.Dispatcher.InvokeAsync(Dispose);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _requests.Cancel();
        void Release()
        {
            if (_window is null) return;
            foreach (var id in _registered.Keys) UnregisterHotKey(_window.Handle, id);
            _registered.Clear();
            _window.RemoveHook(OnMessage); _window.Dispose(); _window = null;
        }
        if (_window?.Dispatcher.CheckAccess() != false) Release();
        else if (!_window.Dispatcher.HasShutdownStarted) _window.Dispatcher.BeginInvoke(Release);
        _requests.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
