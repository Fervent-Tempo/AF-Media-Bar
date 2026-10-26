// 仅连接浮窗输入与现有业务服务，窗口停止后拒绝旧结果。
using System.Diagnostics;
using System.Windows.Input;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using AFMediaBar.Components;

namespace AFMediaBar.Views.Windows;

public sealed partial class FloatingBarWindow
{
    private async void MediaControl_OutputDeviceMenuRequested(object? sender, EventArgs e) =>
        await ShowDeviceMenuAsync(AudioDeviceDirection.Output, EffectiveInteraction.OutputDeviceClickTarget, _mediaControl.GetOutputDeviceAnchor());
    private async void MediaControl_InputDeviceMenuRequested(object? sender, EventArgs e) =>
        await ShowDeviceMenuAsync(AudioDeviceDirection.Input, EffectiveInteraction.InputDeviceClickTarget, _mediaControl.GetInputDeviceAnchor());
    private bool CanPublishMenu(int environmentVersion) =>
        IsOperational && environmentVersion == _menuEnvironmentVersion;

    private async Task ShowDeviceMenuAsync(AudioDeviceDirection direction, AudioDeviceSwitchTarget scope, TrayIconBounds anchor)
    {
        if (!CanPublishMenu(_menuEnvironmentVersion) || scope == AudioDeviceSwitchTarget.Disabled) return;
        using var edgeHold = KeepEdgeExpanded();
        var environmentVersion = _menuEnvironmentVersion;
        var mode = direction == AudioDeviceDirection.Output ? TaskbarCompactFlyoutMode.OutputDevice : TaskbarCompactFlyoutMode.InputDevice;
        if (_compactFlyout.IsShowing(mode)) { _compactFlyout.Dismiss(); return; }
        try
        {
            var target = await _audioInteractionService.ResolveTargetAsync(direction, scope, this, false);
            if (!CanPublishMenu(environmentVersion)) return;
            if (target is null) { _mediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.App.Unavailable")); return; }
            var devices = await _audioInteractionService.DeviceSwitches.GetChoicesAsync(target);
            var selected = await _audioInteractionService.DeviceSwitches.GetSelectedAsync(target, devices);
            var route = await _audioInteractionService.DeviceSwitches.ReadRouteAsync(target);
            if (!CanPublishMenu(environmentVersion)) return;
            _deviceMenuTarget = target;
            _compactFlyout.ShowDevices(target, devices, selected, anchor, route.IsSupported && route.Error is null);
            if (!route.IsSupported || route.Error is not null)
            {
                _compactFlyout.ShowDeviceStatus(Translations.Get(route.Error ?? "Audio.Route.Unsupported"));
                return;
            }
            if (selected is null) _compactFlyout.ShowDeviceStatus(target.Application is null ?
                await _audioInteractionService.GetDeviceDescriptionAsync(direction) : Translations.Get("Audio.Route.HiddenOrDisconnected"));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.WriteLine(exception); if (IsOperational) _mediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.Route.Failed")); }
    }
    private async void MediaControl_OutputDeviceWheelRequested(object? sender, PlayerSurfaceWheelEventArgs e) =>
        await PreviewDeviceAsync(AudioDeviceDirection.Output, e.Delta);
    private async void MediaControl_InputDeviceWheelRequested(object? sender, PlayerSurfaceWheelEventArgs e) =>
        await PreviewDeviceAsync(AudioDeviceDirection.Input, e.Delta);
    private async Task PreviewDeviceAsync(AudioDeviceDirection direction, int delta)
    {
        try
        {
            var settings = EffectiveInteraction;
            var scope = direction == AudioDeviceDirection.Output ? settings.OutputDeviceWheelTarget : settings.InputDeviceWheelTarget;
            if (!CanPublishMenu(_menuEnvironmentVersion) || scope == AudioDeviceSwitchTarget.Disabled) return;
            using var edgeHold = KeepEdgeExpanded();
            var environmentVersion = _menuEnvironmentVersion;
            var target = await _audioInteractionService.ResolveTargetAsync(direction, scope, this, true);
            if (!CanPublishMenu(environmentVersion)) return;
            if (target is null) { _mediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.App.Unavailable")); return; }
            await _audioInteractionService.DeviceSwitches.PreviewAsync(target, TrayWheelPolicy.GetDeviceSteps(delta), this);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.WriteLine(exception); if (IsOperational) _mediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.Route.Failed")); }
    }
    private async void CompactFlyout_OutputDeviceWheelRequested(int delta)
    {
        if (_deviceMenuTarget is not { } target || _isClosing) return;
        try { await _audioInteractionService.DeviceSwitches.PreviewAsync(target, TrayWheelPolicy.GetDeviceSteps(delta), this); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private async void MediaControl_OutputDeviceSelected(AudioDeviceOption device)
    {
        if (_deviceMenuTarget is not { } target || _isClosing) return;
        try
        {
            var result = await _audioInteractionService.DeviceSwitches.SelectAsync(target, device, this);
            if (IsOperational) _mediaControl.SetDeviceTooltip(target.Direction, AudioDevicePresentation.GetResultText(result, target.Application is not null));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private async void MediaControl_OutputDeviceInfoRequested(object? sender, EventArgs e) => await RefreshDeviceTooltipAsync(AudioDeviceDirection.Output);
    private async void MediaControl_InputDeviceInfoRequested(object? sender, EventArgs e) => await RefreshDeviceTooltipAsync(AudioDeviceDirection.Input);
    private async Task RefreshDeviceTooltipAsync(AudioDeviceDirection direction)
    {
        using var edgeHold = KeepEdgeExpanded();
        var environmentVersion = _menuEnvironmentVersion;
        if (!CanPublishMenu(environmentVersion)) return;
        try
        {
            var settings = EffectiveInteraction;
            var scope = direction == AudioDeviceDirection.Output ? settings.OutputDeviceWheelTarget : settings.InputDeviceWheelTarget;
            if (scope == AudioDeviceSwitchTarget.Disabled) { _mediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.Device.Disabled")); return; }
            var target = await _audioInteractionService.ResolveTargetAsync(direction, scope, this, true);
            if (!IsOperational) return;
            if (target is null) { _mediaControl.SetDeviceTooltip(direction, Translations.Get("Audio.App.Unavailable")); return; }
            var choices = await _audioInteractionService.DeviceSwitches.GetChoicesAsync(target);
            var selected = await _audioInteractionService.DeviceSwitches.GetSelectedAsync(target, choices);
            var route = await _audioInteractionService.DeviceSwitches.ReadRouteAsync(target);
            var detail = !route.IsSupported || route.Error is not null ? Translations.Get(route.Error ?? "Audio.Route.Unsupported") :
                selected?.DisplayName ?? (target.Application is null ? await _audioInteractionService.GetDeviceDescriptionAsync(direction) :
                    Translations.Get("Audio.Route.HiddenOrDisconnected"));
            if (CanPublishMenu(environmentVersion)) _mediaControl.SetDeviceTooltip(direction, (target.Application is null ? string.Empty : target.Application.DisplayName + " · ") + detail);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private void OnAudioDeviceSwitchUpdated(AudioDeviceSwitchUpdate update) => Dispatcher.BeginInvoke(() =>
    {
        if (!IsOperational) return;
        var settings = EffectiveInteraction;
        var scope = update.Target.Direction == AudioDeviceDirection.Output ? settings.OutputDeviceWheelTarget : settings.InputDeviceWheelTarget;
        if ((scope == AudioDeviceSwitchTarget.SystemDefault && update.Target.Application is null) ||
            (scope == AudioDeviceSwitchTarget.ForegroundApplication && update.Target.Application is not null))
        {
            var detail = update.Result is null ? update.Candidate?.DisplayName ?? Translations.Get("Audio.Device.Unavailable") :
                AudioDevicePresentation.GetResultText(update.Result, update.Target.Application is not null);
            _mediaControl.SetDeviceTooltip(update.Target.Direction, string.IsNullOrEmpty(detail) ? string.Empty :
                (update.Target.Application is null ? string.Empty : update.Target.Application.DisplayName + " · ") + detail);
        }
        if (_deviceMenuTarget?.Key == update.Target.Key && _compactFlyout.IsVisible)
        {
            if (update.Result is null && update.Candidate is { } candidate) _compactFlyout.SetOutputDevicePreview(candidate);
            else if (update.Result is { } result)
            {
                _compactFlyout.ShowDeviceStatus(AudioDevicePresentation.GetResultText(result, update.Target.Application is not null));
                _ = RefreshOpenDeviceMenuAsync();
            }
        }
    });
    private void OnAudioDeviceInvalidated(object? sender, EventArgs args) => Dispatcher.BeginInvoke(() =>
    {
        if (IsOperational) _ = RefreshOpenDeviceMenuAsync();
    });
    private async Task RefreshOpenDeviceMenuAsync()
    {
        var environmentVersion = _menuEnvironmentVersion;
        if (_deviceMenuTarget is not { } target || !_compactFlyout.IsVisible ||
            !_compactFlyout.IsShowing(target.Direction == AudioDeviceDirection.Output ? TaskbarCompactFlyoutMode.OutputDevice : TaskbarCompactFlyoutMode.InputDevice)) return;
        // 菜单真的开着才需要保持展开：设备刷新事件可能每秒都来，先申请再判断会把折叠条反复叫醒。
        using var edgeHold = KeepEdgeExpanded();
        try
        {
            var devices = await _audioInteractionService.DeviceSwitches.GetChoicesAsync(target);
            var selected = await _audioInteractionService.DeviceSwitches.GetSelectedAsync(target, devices);
            if (!CanPublishMenu(environmentVersion) || !_compactFlyout.IsVisible || _deviceMenuTarget?.Key != target.Key) return;
            _compactFlyout.UpdateDevices(devices, selected, _audioInteractionService.DeviceSwitches.HasPending(target));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }

    private async void MediaControl_VolumeMenuRequested(object? sender, EventArgs e)
    {
        if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.Volume))
        {
            _compactFlyout.Dismiss();
            return;
        }

        if (!CanPublishMenu(_menuEnvironmentVersion)) return;
        using var edgeHold = KeepEdgeExpanded();
        var environmentVersion = _menuEnvironmentVersion;
        var openingAnchor = _mediaControl.GetVolumeAnchor();
        var volume = await ReadVolumeAsync();
        if (!CanPublishMenu(environmentVersion)) return;
        _currentVolume = volume;
        _compactFlyout.ShowVolume(_lastSnapshot.SourceName, volume?.VolumePercent, openingAnchor);
    }

    /// <summary>
    /// 在指定屏幕锚点处打开紧凑菜单（输出设备或当前应用音量）。托盘图标点击走这条入口：菜单内容与按钮点开时完全一致，
    /// 只有锚点不同，因此不会出现两份会各自漂移的实现。
    /// Opens a compact menu (output device or current application volume) at the given screen anchor. The tray icon's click uses
    /// this entry: the menu content is identical to the button-opened one and only the anchor differs, so there is no second
    /// implementation to drift.
    /// </summary>
    /// <param name="mode">要打开的面板：输出设备或当前应用音量。/ Panel to open: output device or current application volume.</param>
    /// <param name="anchor">菜单锚点；为空时退回媒体栏上的对应按钮。/ Menu anchor, falling back to the matching media-bar button when null.</param>
    public async Task ShowCompactMenuAsync(TaskbarCompactFlyoutMode mode, TrayIconBounds? anchor)
    {
        if (!CanPublishMenu(_menuEnvironmentVersion) || mode is not (TaskbarCompactFlyoutMode.OutputDevice or TaskbarCompactFlyoutMode.InputDevice or TaskbarCompactFlyoutMode.Volume))
            return;

        if (IsEdgeConcealed)
        {
            SetEdgeTarget(collapse: false, instant: true);
            anchor = null;
        }

        if (_compactFlyout.IsShowing(mode))
        {
            _compactFlyout.Dismiss();
            return;
        }


        if (mode is TaskbarCompactFlyoutMode.OutputDevice or TaskbarCompactFlyoutMode.InputDevice)
        {
            var direction = mode == TaskbarCompactFlyoutMode.OutputDevice ? AudioDeviceDirection.Output : AudioDeviceDirection.Input;
            await ShowDeviceMenuAsync(direction, AudioDeviceSwitchTarget.SystemDefault,
                anchor ?? (direction == AudioDeviceDirection.Output ? _mediaControl.GetOutputDeviceAnchor() : _mediaControl.GetInputDeviceAnchor()));
            return;
        }

        using var edgeHold = KeepEdgeExpanded();
        var environmentVersion = _menuEnvironmentVersion;
        var openingAnchor = anchor ?? _mediaControl.GetVolumeAnchor();
        var volume = await ReadVolumeAsync();
        if (!CanPublishMenu(environmentVersion)) return;
        _currentVolume = volume;
        _compactFlyout.ShowVolume(_lastSnapshot.SourceName, volume?.VolumePercent, openingAnchor);
    }

    private async void MediaControl_VolumeWheelRequested(object? sender, PlayerSurfaceWheelEventArgs e)
        => await PreviewVolumeAsync(e.Delta, updateFlyout: _compactFlyout.IsShowing(TaskbarCompactFlyoutMode.Volume));

    /// <summary>
    /// 指针进入音量按钮时刷新提示；已有延迟应用候选时显示该候选，避免提示回退到已应用值。
    /// Refreshes the tooltip when the pointer enters the volume button; a pending deferred candidate is shown instead so the
    /// tooltip cannot fall back to the applied value.
    /// </summary>
    private async void MediaControl_VolumeInfoRequested(object? sender, EventArgs e)
    {
        using var edgeHold = KeepEdgeExpanded();
        var environmentVersion = _menuEnvironmentVersion;
        if (!CanPublishMenu(environmentVersion)) return;
        if (_pendingVolume is { } pending)
        {
            _mediaControl.SetVolumePreview(pending);
            return;
        }

        var volume = await ReadVolumeAsync();
        if (!CanPublishMenu(environmentVersion)) return;
        _currentVolume = volume;
        if (volume is null)
            _mediaControl.SetVolumeUnavailable();
        else
            _mediaControl.SetVolumePreview(volume.VolumePercent);
    }

    private async void CompactFlyout_VolumeWheelRequested(int delta)
        => await PreviewVolumeAsync(delta, updateFlyout: true);

    private async Task PreviewVolumeAsync(int delta, bool updateFlyout)
    {
        using var edgeHold = KeepEdgeExpanded();
        var environmentVersion = _menuEnvironmentVersion;
        if (!CanPublishMenu(environmentVersion)) return;
        var volume = _currentVolume ?? await ReadVolumeAsync();
        if (!CanPublishMenu(environmentVersion)) return;
        _currentVolume ??= volume;
        if (_currentVolume is null)
        {
            _mediaControl.SetVolumeUnavailable();
            return;
        }

        var steps = Math.Max(1, Math.Abs(delta) / Mouse.MouseWheelDeltaForOneLine) * (delta > 0 ? 1 : -1);
        var value = Math.Clamp((_pendingVolume ?? _currentVolume.VolumePercent) + steps * 2, 0, 100);
        QueueVolume(value);
        _mediaControl.SetVolumePreview(value);
        if (updateFlyout) _compactFlyout.SetVolumePreview(value);
    }

    private void MediaControl_VolumeValueRequested(int value)
    {
        // 菜单内拖动滑杆同样要刷新按钮提示，与滚轮预览保持一致。
        // Dragging the slider inside the menu refreshes the button tooltip too, matching the wheel preview.
        _mediaControl.SetVolumePreview(value);
        QueueVolume(value);
    }

    private async Task<ApplicationVolumeSnapshot?> ReadVolumeAsync()
    {
        try { return await Task.Run(_audioInteractionService.GetCurrentMediaVolume); }
        catch (Exception exception) { _log.Error("FloatingBar", "读取应用音量失败", exception); return null; }
    }

    private void QueueVolume(int value)
    {
        if (_currentVolume is null || !CanPublishMenu(_menuEnvironmentVersion)) return;
        _pendingVolume = Math.Clamp(value, 0, 100);
        _volumeApplyTimer.Stop();
        _volumeApplyTimer.Start();
    }

    private async void MediaControl_QuickLaunchRequested(QuickLaunchEntry entry)
    {
        if (!CanPublishMenu(_menuEnvironmentVersion)) return;
        _quickLaunch?.Select(entry.Id);
        _quickLaunchApplyTimer.Stop();
        _pendingQuickLaunch = null;
        _compactFlyout.Dismiss();
        using var edgeHold = KeepEdgeExpanded();
        var environmentVersion = _menuEnvironmentVersion;
        var anchor = _mediaControl.GetQuickLaunchAnchor();
        var result = await LaunchQuickLaunchEntryAsync(entry);
        if (CanPublishMenu(environmentVersion)) ShowQuickLaunchResult(result, anchor);
    }

    private async Task<QuickLaunchResult?> LaunchQuickLaunchEntryAsync(QuickLaunchEntry entry)
    {
        // 已打开的列表可能仍持有删除前的条目；在后台命令入队前再次校验身份。
        if (!(SettingsManager.Current.QuickLaunch.Entries ?? []).Any(current => current.Id == entry.Id &&
            current.Kind == entry.Kind && current.Target == entry.Target)) return null;
        var token = _mediaControl.QuickLaunchRequestToken;
        try
        {
            var result = await _sourceActivationService.LaunchAsync(entry, token);
            return token.IsCancellationRequested ? null : result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            return QuickLaunchResult.Failed;
        }
    }

    private void ShowQuickLaunchResult(QuickLaunchResult? result, TrayIconBounds? anchor = null)
    {
        if (!CanPublishMenu(_menuEnvironmentVersion) || result is null or QuickLaunchResult.Success) return;
        _compactFlyout.ShowQuickLaunch(
            PresentationSettings.QuickLaunch.Entries ?? [],
            anchor ?? _mediaControl.GetQuickLaunchAnchor());
        _compactFlyout.ShowQuickLaunchStatus(DescribeQuickLaunchResult(result.Value));
    }

    /// <summary>
    /// 取快速启动的失败原因：文案在写进菜单的这一刻按当前界面语言解析，不缓存到字段里，因此界面上不会留下上一种语言
    /// 的那一句，也不需要额外的刷新路径。
    /// Resolves the quick-launch failure reason: the sentence is resolved in the active interface language at the moment it
    /// is handed to the menu and is never cached in a field, so no line is left behind in the previous language and no
    /// extra refresh path is needed.
    /// </summary>
    /// <param name="result">失败的启动结果。/ The failed launch result.</param>
    private static string DescribeQuickLaunchResult(QuickLaunchResult result) =>
        Translations.Get(result == QuickLaunchResult.InvalidTarget
            ? "Shell.QuickLaunch.Status.InvalidTarget"
            : "Shell.QuickLaunch.Status.Failed");

    private void MediaControl_QuickLaunchMenuRequested(object? sender, EventArgs e)
    {
        if (!CanPublishMenu(_menuEnvironmentVersion)) return;
        if (_compactFlyout.IsShowing(TaskbarCompactFlyoutMode.QuickLaunch))
        {
            _compactFlyout.Dismiss();
            return;
        }

        _compactFlyout.ShowQuickLaunch(
            PresentationSettings.QuickLaunch.Entries ?? [],
            _mediaControl.GetQuickLaunchAnchor());
    }

    private void MediaControl_QuickLaunchWheelRequested(object? sender, PlayerSurfaceWheelEventArgs e)
    {
        var entries = PresentationSettings.QuickLaunch.Entries ?? [];
        if (entries.Count == 0) return;
        if (_quickLaunch?.Settings.InteractionMode == QuickLaunchInteractionMode.SelectThenLaunch)
        {
            _quickLaunchApplyTimer.Stop();
            _pendingQuickLaunch = null;
            _quickLaunch.MoveSelection(e.Delta);
            return;
        }
        var current = _pendingQuickLaunch is null
            ? 0
            : entries.ToList().FindIndex(entry => entry.Id == _pendingQuickLaunch.Id);
        var index = DeferredCircularSelection.Move(current, e.Delta, entries.Count);
        if (index < 0) return;
        _pendingQuickLaunch = entries[index];
        _quickLaunch?.Select(_pendingQuickLaunch.Id);
        _mediaControl.SetQuickLaunchPreview(_pendingQuickLaunch);
        _quickLaunchApplyTimer.Stop();
        _quickLaunchApplyTimer.Start();
    }

    private void MediaControl_OpenTaskManagerRequested(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarWindow] Could not open Task Manager: {ex}");
        }
    }

    private void SynchronizeMetricsSubscription(PerformanceComponentSettings performance)
    {
        var metrics = performance.GetOrderedMetrics().ToArray();
        var interval = TimeSpan.FromMilliseconds(performance.RefreshIntervalMilliseconds);
        var shouldSubscribe = IsOperational && !IsEdgeFullyCollapsed && _mediaControl.IsPerformanceComponentVisible;
        var configurationChanged = _metricsSubscription is not null &&
                                   (!_subscribedMetricKinds.SequenceEqual(metrics) ||
                                    _subscribedMetricInterval != interval);
        var transition = MetricPresentationPolicy.ResolveSubscriptionTransition(
            shouldSubscribe,
            _metricsSubscription is not null,
            configurationChanged);
        if (transition == MetricSubscriptionTransition.None)
            return;

        if (transition == MetricSubscriptionTransition.Unsubscribe)
        {
            DisposeMetricsSubscription();
            return;
        }

        if (transition == MetricSubscriptionTransition.Renew)
            DisposeMetricsSubscription();
        _metricCycleIndex = 0;
        _metricSampleCount = 0;
        var generation = _metricsSubscriptionGeneration;
        _metricsSubscription = _metricsMonitor.Subscribe(
            metrics,
            interval,
            snapshot => ApplyMetricsSnapshot(generation, snapshot));
        _subscribedMetricKinds = metrics;
        _subscribedMetricInterval = interval;
    }

    private void ApplyMetricsSnapshot(int generation, SystemMetricsSnapshot snapshot)
    {
        if (generation != _metricsSubscriptionGeneration || !IsOperational || IsEdgeFullyCollapsed)
            return;
        var settings = PresentationSettings.PerformanceComponent.Normalize();
        var metrics = settings.GetOrderedMetrics();
        if (metrics.Count == 0)
            return;
        _metricCycleIndex = Math.Clamp(_metricCycleIndex, 0, metrics.Count - 1);
        _metricSampleCount++;
        _metricCycleIndex = MetricPresentationPolicy.Advance(_metricCycleIndex, _metricSampleCount, metrics.Count);
        _mediaControl.ApplyPerformanceSnapshot(snapshot, _metricCycleIndex,
            settings.OpenTaskManagerOnClick);
        _foregroundSamplingSession.RequestRefresh();
    }

    private void DisposeMetricsSubscription()
    {
        _metricsSubscriptionGeneration++;
        _metricsSubscription?.Dispose();
        _metricsSubscription = null;
        _subscribedMetricKinds = [];
        _subscribedMetricInterval = null;
    }

}
