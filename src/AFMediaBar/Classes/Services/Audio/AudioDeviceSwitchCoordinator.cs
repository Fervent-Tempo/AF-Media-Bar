// 统一候选、延迟及代际；服务拥有待应用任务，调用方仅发布输入意图及取消所属请求。
using AFMediaBar.Classes.Models;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>切换协调器的设备平台边界。</summary>
public interface IAudioDeviceSwitchOperations
{
    int Version { get; }
    int PolicyVersion { get; }
    bool IsAllowed(AudioDeviceOption device);
    event EventHandler? Changed;
    Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, CancellationToken token);
    Task<ApplicationAudioRoute> ReadRouteAsync(ApplicationAudioSnapshot app, AudioDeviceDirection direction, CancellationToken token);
    Task<AudioDeviceOperationResult> ApplyAsync(AudioDeviceTarget target, AudioDeviceOption? device, CancellationToken token, Func<bool> isCurrent);
}

/// <summary>将设备与应用路由连接到统一切换流程，输出成功时刷新频谱环境。</summary>
public sealed class AudioDeviceSwitchOperations(AudioDeviceService devices, ApplicationAudioRoutingService routing,
    AudioMonitorService monitor) : IAudioDeviceSwitchOperations
{
    public int Version => devices.Version;
    public int PolicyVersion => devices.PolicyVersion;
    public bool IsAllowed(AudioDeviceOption device) => device.IsSystemDefaultChoice || devices.IsAllowed(device.Direction, device.PolicyId);
    public event EventHandler? Changed { add => devices.Changed += value; remove => devices.Changed -= value; }
    public Task<IReadOnlyList<AudioDeviceOption>> GetDevicesAsync(AudioDeviceDirection direction, CancellationToken token) =>
        devices.GetDevicesAsync(direction, cancellationToken: token);
    public Task<ApplicationAudioRoute> ReadRouteAsync(ApplicationAudioSnapshot app, AudioDeviceDirection direction, CancellationToken token) =>
        routing.ReadRouteAsync(app, direction, token);
    public async Task<AudioDeviceOperationResult> ApplyAsync(AudioDeviceTarget target, AudioDeviceOption? device, CancellationToken token, Func<bool> isCurrent)
    {
        var result = target.Application is { } app
            ? await routing.SetRouteAsync(app, target.Direction, device, token, isCurrent)
            : device is null ? new AudioDeviceOperationResult(AudioDeviceOperationStatus.Unavailable)
            : await devices.SetDefaultAsync(device, token, isCurrent);
        if (target.Direction == AudioDeviceDirection.Output &&
            result.Status is AudioDeviceOperationStatus.Applied or AudioDeviceOperationStatus.Partial)
            monitor.ResetAfterEnvironmentChange();
        return result;
    }
}

/// <summary>所有 UI 入口共享的设备候选与执行结果。</summary>
public sealed record AudioDeviceSwitchUpdate(AudioDeviceTarget Target, AudioDeviceOption? Candidate, AudioDeviceOperationResult? Result);

/// <summary>按方向和应用隔离滚轮候选，只应用最终请求。</summary>
public sealed class AudioDeviceSwitchCoordinator : IDisposable, IHostedService
{
    private readonly IAudioDeviceSwitchOperations _operations;
    private readonly AppLogService? _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, Pending> _pending = new();
    private readonly HashSet<Task> _activeTasks = [];
    private bool _disposed;
    public event Action<AudioDeviceSwitchUpdate>? Updated;
    public event EventHandler? Invalidated;
    public AudioDeviceSwitchCoordinator(IAudioDeviceSwitchOperations operations, AppLogService? log = null)
    {
        _operations = operations;
        _log = log;
        operations.Changed += OnChanged;
    }

    public bool HasPending(AudioDeviceTarget target) { lock (_gate) return _pending.ContainsKey(target.Key); }
    public AudioDeviceOption? GetPreview(AudioDeviceTarget target)
    { lock (_gate) return _pending.GetValueOrDefault(target.Key)?.Candidate; }

    public async Task<IReadOnlyList<AudioDeviceOption>> GetChoicesAsync(AudioDeviceTarget target, CancellationToken token = default)
    {
        var choices = await _operations.GetDevicesAsync(target.Direction, token);
        return target.Application is null ? choices : new[] { DefaultChoice(target.Direction) }.Concat(choices).ToArray();
    }
    public static AudioDeviceOption DefaultChoice(AudioDeviceDirection direction) =>
        new("", "", Translations.Get("Audio.Route.FollowDefault"), false) { Direction = direction, IsSystemDefaultChoice = true };

    public Task<ApplicationAudioRoute> ReadRouteAsync(AudioDeviceTarget target, CancellationToken token = default) =>
        target.Application is null ? Task.FromResult(new ApplicationAudioRoute(null, false, true)) :
        _operations.ReadRouteAsync(target.Application, target.Direction, token);

    public async Task<AudioDeviceOption?> GetSelectedAsync(AudioDeviceTarget target, IReadOnlyList<AudioDeviceOption> choices, CancellationToken token = default)
    {
        if (GetPreview(target) is { } preview) return choices.FirstOrDefault(choice => choice.PolicyId == preview.PolicyId);
        if (target.Application is null) return choices.FirstOrDefault(choice => choice.IsDefault);
        var route = await _operations.ReadRouteAsync(target.Application, target.Direction, token);
        if (!route.IsSupported || route.IsMixed || route.Error is not null) return null;
        return choices.FirstOrDefault(choice => route.EndpointId is null ? choice.IsSystemDefaultChoice :
            string.Equals(choice.PolicyId, route.EndpointId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AudioDeviceOption?> PreviewAsync(AudioDeviceTarget target, int signedSteps, object owner,
        CancellationToken token = default)
    {
        if (signedSteps == 0) return GetPreview(target);
        Pending request;
        AudioDeviceOption? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var old = _pending.GetValueOrDefault(target.Key);
            previous = old?.Candidate;
            if (previous is null) signedSteps += old?.Steps ?? 0;
            ReplaceRequest(target.Key);
            request = new(owner, _operations.PolicyVersion, CancellationTokenSource.CreateLinkedTokenSource(token)) { Steps = signedSteps };
            _pending[target.Key] = request;
        }
        try
        {
            var route = await ReadRouteAsync(target, request.Cancellation.Token);
            if (!route.IsSupported) { Complete(target, request, new(AudioDeviceOperationStatus.Unsupported)); return null; }
            var choices = await GetChoicesAsync(target, request.Cancellation.Token);
            if (choices.Count == 0) { Complete(target, request, new(AudioDeviceOperationStatus.Unavailable)); return null; }
            var selected = previous ?? await GetSelectedAsync(target, choices, request.Cancellation.Token);
            var index = selected is null ? -1 : choices.ToList().FindIndex(choice => choice.PolicyId == selected.PolicyId);
            var next = index < 0 ? (signedSteps > 0 ? (signedSteps - 1) % choices.Count :
                ((signedSteps % choices.Count) + choices.Count) % choices.Count) : ((index + signedSteps) % choices.Count + choices.Count) % choices.Count;
            lock (_gate)
            {
                if (!IsCurrentLocked(target, request)) { request.Cancellation.Dispose(); return null; }
                request.Candidate = choices[next];
            }
            if (!_operations.IsAllowed(request.Candidate!))
            {
                Complete(target, request, new(AudioDeviceOperationStatus.Unavailable));
                return null;
            }
            Publish(new(target, request.Candidate, null));
            Track(ApplyDelayedAsync(target, request));
            return request.Candidate;
        }
        catch (OperationCanceledException) { Complete(target, request, new(AudioDeviceOperationStatus.Cancelled)); return null; }
        catch (Exception exception) { Complete(target, request, new(AudioDeviceOperationStatus.Failed, exception.Message)); return null; }
    }

    private async Task ApplyDelayedAsync(AudioDeviceTarget target, Pending request)
    {
        try
        {
            await Task.Delay(AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds, request.Cancellation.Token);
            var result = await ApplyAsync(target, request);
            Complete(target, request, result);
        }
        catch (OperationCanceledException) { Complete(target, request, new(AudioDeviceOperationStatus.Cancelled)); }
        catch (Exception exception) { Complete(target, request, new(AudioDeviceOperationStatus.Failed, exception.Message)); }
    }

    public async Task<AudioDeviceOperationResult> SelectAsync(AudioDeviceTarget target, AudioDeviceOption choice,
        object owner, CancellationToken token = default)
    {
        Pending request;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ReplaceRequest(target.Key);
            request = new(owner, _operations.PolicyVersion, CancellationTokenSource.CreateLinkedTokenSource(token)) { Candidate = choice };
            _pending[target.Key] = request;
        }
        Publish(new(target, choice, null));
        AudioDeviceOperationResult result;
        try { result = await ApplyAsync(target, request); }
        catch (OperationCanceledException) { result = new(AudioDeviceOperationStatus.Cancelled); }
        catch (Exception exception) { result = new(AudioDeviceOperationStatus.Failed, exception.Message); }
        Complete(target, request, result);
        return result;
    }

    private Task<AudioDeviceOperationResult> ApplyAsync(AudioDeviceTarget target, Pending request)
    {
        bool Current() { lock (_gate) return IsCurrentLocked(target, request); }
        if (!Current()) return Task.FromResult(new AudioDeviceOperationResult(AudioDeviceOperationStatus.Cancelled));
        return _operations.ApplyAsync(target, request.Candidate?.IsSystemDefaultChoice == true ? null : request.Candidate,
            request.Cancellation.Token, Current);
    }
    private bool IsCurrentLocked(AudioDeviceTarget target, Pending request) =>
        !_disposed && ReferenceEquals(_pending.GetValueOrDefault(target.Key), request) &&
        !request.Cancellation.IsCancellationRequested && request.PolicyVersion == _operations.PolicyVersion;
    private void Complete(AudioDeviceTarget target, Pending request, AudioDeviceOperationResult result)
    {
        bool current;
        bool logCurrent;
        lock (_gate)
        {
            current = !_disposed && ReferenceEquals(_pending.GetValueOrDefault(target.Key), request);
            logCurrent = current && !request.Cancellation.IsCancellationRequested && request.PolicyVersion == _operations.PolicyVersion;
            if (current) _pending.Remove(target.Key);
        }
        if (logCurrent)
        {
            var message = $"设备切换结果: target={target.Key} device={request.Candidate?.DisplayName ?? "none"} " +
                $"status={result.Status} reason={result.Message ?? "none"}";
            if (result.Status == AudioDeviceOperationStatus.Applied) _log?.Info("Audio", message);
            else if (result.Status is AudioDeviceOperationStatus.Unchanged or AudioDeviceOperationStatus.Cancelled) _log?.Verbose("Audio", message);
            else _log?.Warn("Audio", message);
        }
        if (current) Publish(new(target, request.Candidate, result));
        request.Cancellation.Dispose();
    }
    private void ReplaceRequest(string key)
    {
        if (_pending.Remove(key, out var old)) old.Cancellation.Cancel();
    }
    private void Track(Task task)
    {
        lock (_gate) _activeTasks.Add(task);
        _ = ObserveAsync(task);
    }
    private async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception exception) { _log?.Warn("Audio", "设备切换后台任务失败", exception); }
        finally { lock (_gate) _activeTasks.Remove(task); }
    }
    /// <summary>等待已启动的延迟请求结束，供停止服务及隔离验证使用。</summary>
    public Task DrainAsync(CancellationToken token = default)
    {
        lock (_gate) return Task.WhenAll(_activeTasks.ToArray()).WaitAsync(token);
    }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task StopAsync(CancellationToken cancellationToken) { Dispose(); await DrainAsync(cancellationToken); }

    public void CancelOwner(object owner)
    {
        lock (_gate)
            foreach (var key in _pending.Where(pair => ReferenceEquals(pair.Value.Owner, owner)).Select(pair => pair.Key).ToArray())
                ReplaceRequest(key);
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        KeyValuePair<string, Pending>[] requests;
        lock (_gate)
        {
            if (_disposed) return;
            requests = _pending.ToArray();
            if (args is AudioDeviceInvalidationEventArgs { CancelPending: true })
            {
                foreach (var key in _pending.Keys.ToArray()) ReplaceRequest(key);
                requests = [];
            }
        }
        // 原生默认设备变化不能取消另一个方向的候选。仅淘汰已不可选的端点。
        foreach (var pair in requests)
        {
            if (pair.Value.Candidate is { } candidate && !_operations.IsAllowed(candidate))
                CancelRequest(pair.Key, pair.Value);
            else Track(ValidateCandidateAsync(pair.Key, pair.Value));
        }
        if (!_disposed) Invalidated?.Invoke(this, EventArgs.Empty);
    }
    private async Task ValidateCandidateAsync(string key, Pending request)
    {
        try
        {
            var candidate = request.Candidate;
            if (candidate is null || candidate.IsSystemDefaultChoice) return;
            var choices = await _operations.GetDevicesAsync(candidate.Direction, request.Cancellation.Token);
            if (!choices.Any(choice => string.Equals(choice.PolicyId, candidate.PolicyId, StringComparison.OrdinalIgnoreCase)))
                CancelRequest(key, request);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception exception) { _log?.Warn("Audio", "设备切换环境更新失败", exception); CancelRequest(key, request); }
    }
    private void CancelRequest(string key, Pending request)
    {
        lock (_gate) if (ReferenceEquals(_pending.GetValueOrDefault(key), request)) ReplaceRequest(key);
    }

    private void Publish(AudioDeviceSwitchUpdate update)
    {
        if (_disposed || Updated is not { } handlers) return;
        foreach (Action<AudioDeviceSwitchUpdate> handler in handlers.GetInvocationList())
            try { handler(update); } catch (Exception exception) { _log?.Warn("Audio", "设备切换结果订阅者失败", exception); }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var key in _pending.Keys.ToArray()) ReplaceRequest(key);
        }
        _operations.Changed -= OnChanged;
    }
    private sealed class Pending(object owner, int policyVersion, CancellationTokenSource cancellation)
    {
        public object Owner { get; } = owner;
        public int PolicyVersion { get; } = policyVersion;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public AudioDeviceOption? Candidate { get; set; }
        public int Steps { get; init; }
    }
}
