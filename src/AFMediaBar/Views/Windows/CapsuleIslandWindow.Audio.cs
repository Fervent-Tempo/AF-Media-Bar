using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Resources;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 胶囊岛的音频入口（输出设备 / 当前应用音量）：卡片上的两个按钮与托盘左键都走这里，
/// 弹出的浮层是与任务栏悬停层同一份 <see cref="TaskbarCompactFlyoutWindow"/>，因此菜单只有一个实现。
/// Audio half of the capsule island (output device / current application volume): the card's two buttons and the tray
/// left-click both come in here, and the flyout they open is the very same
/// <see cref="TaskbarCompactFlyoutWindow"/> the taskbar hover layer uses, so there is only one menu implementation.
/// </summary>
public partial class CapsuleIslandWindow
{
    private readonly AudioInteractionService _audioInteractionService;
    private TaskbarCompactFlyoutWindow? _compactFlyout;
    private DispatcherTimer? _outputDeviceApplyTimer;
    private DispatcherTimer? _volumeApplyTimer;
    private IReadOnlyList<AudioDeviceOption> _outputDevices = [];
    private AudioDeviceOption? _pendingOutputDevice;
    private ApplicationVolumeSnapshot? _currentVolume;
    private int? _pendingVolume;

    /// <summary>卡片上的输出设备按钮：再次点击同一个菜单即收起。/ The card's output-device button; clicking it while its own menu is open dismisses it.</summary>
    private async void OutputDevice_Click(object sender, RoutedEventArgs e) =>
        await ShowCompactMenuAsync(TaskbarCompactFlyoutMode.OutputDevice, anchor: null);

    /// <summary>卡片上的音量按钮：再次点击同一个菜单即收起。/ The card's volume button; clicking it while its own menu is open dismisses it.</summary>
    private async void Volume_Click(object sender, RoutedEventArgs e) =>
        await ShowCompactMenuAsync(TaskbarCompactFlyoutMode.Volume, anchor: null);

    /// <summary>
    /// 指针停在卡片输出设备按钮上滚轮：逐个切换输出设备，与任务栏悬停层按钮是同一语义。
    /// Wheel over the card's output-device button: steps through the output devices, the same semantics the taskbar hover
    /// layer's button has.
    ///
    /// 与任务栏一致，滚轮只更新提示与（已打开菜单的）预览，停手一个延迟后才真正切换默认设备，
    /// 因此连滚多格不会每格都切一次系统设备；并且这里吞掉滚轮，菜单与未来窗口级的滚轮手势都不会同时收到它。
    /// As on the taskbar the wheel only moves the tooltip and — when its menu is open — the in-menu preview, and the change
    /// reaches the default device once the user stops. A wheel flick therefore never switches the system device per notch,
    /// and the wheel is marked handled so neither the menu nor any future window-level wheel gesture sees it as well.
    /// </summary>
    private async void OutputDeviceIcon_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        await PreviewOutputDeviceAsync(e.Delta);
    }

    /// <summary>
    /// 指针进入输出设备按钮时刷新提示：有尚未落地的滚轮候选时显示候选，否则重新枚举并显示当前默认设备。
    /// Refreshes the tooltip when the pointer enters the output-device button: a pending wheel candidate is shown as it is,
    /// otherwise the devices are enumerated again and the current default is shown.
    /// </summary>
    private async void OutputDeviceIcon_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_pendingOutputDevice is { } pending)
        {
            CardOutputDevice.ToolTip = AudioTooltipPolicy.BuildOutputDevice(pending);
            return;
        }

        if (_isClosing)
            return;

        // 每次悬停都重新枚举：默认设备可能已被系统或其它程序换掉，缓存下来就会显示旧设备。
        // Re-enumerated on every hover: the default device may have been changed by the system or another application, and
        // a cache would then report the previous one.
        _outputDevices = await _audioInteractionService.GetOutputDevicesAsync();
        if (_isClosing)
            return;

        CardOutputDevice.ToolTip = AudioTooltipPolicy.BuildOutputDevice(
            _outputDevices.FirstOrDefault(device => device.IsDefault) ?? _outputDevices.FirstOrDefault());
    }

    /// <summary>指针停在卡片音量按钮上滚轮：按 2% 步进调节当前媒体音量，语义与任务栏悬停层按钮相同。
    /// Wheel over the card's volume button: adjusts the current media volume in 2% steps, the same semantics the taskbar
    /// hover layer's button has.</summary>
    private async void VolumeIcon_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        await PreviewVolumeAsync(e.Delta);
    }

    /// <summary>指针进入音量按钮时刷新提示：有待应用的滚轮候选就显示候选，否则读一次当前值。
    /// Refreshes the tooltip when the pointer enters the volume button: a pending wheel candidate is shown as it is,
    /// otherwise the current value is read once.</summary>
    private async void VolumeIcon_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_pendingVolume is { } pending)
        {
            CardVolume.ToolTip = AudioTooltipPolicy.BuildMediaVolume(VolumeSourceName, pending);
            return;
        }

        if (_isClosing)
            return;

        _currentVolume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (_isClosing)
            return;

        CardVolume.ToolTip = AudioTooltipPolicy.BuildMediaVolume(
            VolumeSourceName,
            _currentVolume?.VolumePercent);
    }

    /// <summary>
    /// 音量提示里的来源名：快照还没有来源时用通用标签，而不是把可读的音量谎报成"不可用"。
    /// The source name used by the volume tooltip: a generic label while the snapshot has no source yet, rather than
    /// reporting a readable volume as unavailable.
    /// </summary>
    private string VolumeSourceName => string.IsNullOrWhiteSpace(_snapshot.SourceName)
        ? Translations.Get("Panel.Volume.CurrentMedia")
        : _snapshot.SourceName;

    /// <summary>
    /// 打开（或收起）输出设备 / 当前应用音量的紧凑菜单。
    /// Opens — or dismisses — the compact output-device / current-application-volume menu.
    ///
    /// 这是**唯一**入口：卡片按钮传空的锚点（菜单贴着岛），托盘左键传托盘图标的物理矩形。
    /// 菜单内容与浮层行为都来自共用实现，因此两条路径不可能长出两套语义。
    /// This is the **only** entry point: the card buttons pass a null anchor (the menu sits against the island) and the
    /// tray left-click passes the tray icon's physical bounds. Content and flyout behaviour both come from the shared
    /// implementation, so the two paths cannot grow two sets of semantics.
    /// </summary>
    /// <param name="mode">要打开的面板：输出设备或当前应用音量。/ Panel to open: output device or current application volume.</param>
    /// <param name="anchor">菜单锚点（物理像素）；为空时贴岛窗口。/ Menu anchor in physical pixels; null hugs the island window.</param>
    public async Task ShowCompactMenuAsync(TaskbarCompactFlyoutMode mode, TrayIconBounds? anchor)
    {
        if (_isClosing || _compactFlyout is not { } flyout ||
            mode is not (TaskbarCompactFlyoutMode.OutputDevice or TaskbarCompactFlyoutMode.Volume))
        {
            return;
        }

        if (flyout.IsShowing(mode))
        {
            flyout.Dismiss();
            return;
        }

        var target = anchor ?? GetCompactFlyoutAnchor();
        if (mode == TaskbarCompactFlyoutMode.OutputDevice)
        {
            _outputDevices = await _audioInteractionService.GetOutputDevicesAsync();
            if (_isClosing)
                return;

            flyout.ShowOutputDevices(
                _outputDevices,
                _outputDevices.FirstOrDefault(device => device.IsDefault),
                target);
            return;
        }

        _currentVolume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (_isClosing)
            return;

        flyout.ShowVolume(_snapshot.SourceName, _currentVolume?.VolumePercent, target);
    }

    /// <summary>
    /// 岛窗口的屏幕物理像素矩形，作为紧凑菜单的锚点：浮层据此把菜单放到岛的上方，上方放不下时自动翻到下方。
    /// The island window's screen bounds in physical pixels, used as the compact menu's anchor: the flyout places the menu
    /// above the island and flips it below when there is no room, all inside the shared implementation.
    /// </summary>
    private TrayIconBounds GetCompactFlyoutAnchor()
    {
        var point = PointToScreen(new Point(0, 0));
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        var scaleX = dpi?.M11 ?? 1d;
        var scaleY = dpi?.M22 ?? 1d;
        var width = double.IsNaN(ActualWidth) || ActualWidth <= 0 ? Width : ActualWidth;
        var height = double.IsNaN(ActualHeight) || ActualHeight <= 0 ? Height : ActualHeight;
        return new TrayIconBounds(
            (int)Math.Round(point.X),
            (int)Math.Round(point.Y),
            (int)Math.Round(point.X + width * scaleX),
            (int)Math.Round(point.Y + height * scaleY));
    }

    /// <summary>
    /// 建立共用的紧凑浮层与它的延迟应用计时器。
    /// Creates the shared compact flyout and its deferred-apply timers.
    ///
    /// 延迟应用的形状与任务栏宿主一致（设备 1200ms、音量 100ms，取 <see cref="AudioApplyPolicy"/> 的同一组常量）：
    /// 滚轮连续滚动期间只更新菜单里的预览，停手之后才真正落到系统上，因此不会每滚一格就切一次默认设备。
    /// The deferred apply matches the taskbar host (1200 ms for devices, 100 ms for volume, from the same
    /// <see cref="AudioApplyPolicy"/> constants): while the wheel keeps turning only the in-menu preview moves, and the
    /// change reaches the system once the user stops — so a wheel flick never switches the default device per notch.
    /// </summary>
    private void InitializeCompactFlyout(Func<TaskbarCompactFlyoutWindow> compactFlyoutFactory)
    {
        var flyout = compactFlyoutFactory();
        _compactFlyout = flyout;
        flyout.OutputDeviceSelected += OnCompactFlyoutOutputDeviceSelected;
        flyout.OutputDeviceWheelRequested += OnCompactFlyoutOutputDeviceWheelRequested;
        flyout.VolumeWheelRequested += OnCompactFlyoutVolumeWheelRequested;
        flyout.VolumeValueRequested += OnCompactFlyoutVolumeValueRequested;

        _outputDeviceApplyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds)
        };
        _outputDeviceApplyTimer.Tick += async (_, _) =>
        {
            _outputDeviceApplyTimer.Stop();
            var pending = _pendingOutputDevice;
            _pendingOutputDevice = null;
            if (pending is null || _isClosing)
                return;

            await _audioInteractionService.SetOutputDeviceAsync(pending);
            if (!_isClosing && _compactFlyout?.IsShowing(TaskbarCompactFlyoutMode.OutputDevice) == true)
                _compactFlyout.Dismiss();
        };

        _volumeApplyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AudioApplyPolicy.ApplicationVolumeDelayMilliseconds)
        };
        _volumeApplyTimer.Tick += async (_, _) =>
        {
            _volumeApplyTimer.Stop();
            var pending = _pendingVolume;
            var volume = _currentVolume;
            _pendingVolume = null;
            if (pending is null || volume is null || _isClosing)
                return;

            await Task.Run(() => _audioInteractionService.SetApplicationVolume(volume.ProcessName, pending.Value));
            _currentVolume = volume with { VolumePercent = pending.Value, IsMuted = false };
        };
    }

    /// <summary>菜单里选中设备即切换并收起菜单（与任务栏按钮点开的那份菜单逐句一致）。/ Selecting a device in the menu switches it and closes the menu, exactly as the taskbar menu does.</summary>
    private async void OnCompactFlyoutOutputDeviceSelected(AudioDeviceOption device)
    {
        _outputDeviceApplyTimer?.Stop();
        _pendingOutputDevice = null;
        await _audioInteractionService.SetOutputDeviceAsync(device);
    }

    /// <summary>菜单内滚轮切设备：候选先同步到菜单预览，延迟后真实应用。/ In-menu device wheel: the candidate previews in the menu first and is applied after the delay.</summary>
    private async void OnCompactFlyoutOutputDeviceWheelRequested(int delta) =>
        await PreviewOutputDeviceAsync(delta);

    /// <summary>菜单内滚轮调音量：与任务栏同一套 2% 步进与延迟应用。/ In-menu volume wheel: the same 2% steps and deferred apply as the taskbar.</summary>
    private async void OnCompactFlyoutVolumeWheelRequested(int delta) =>
        await PreviewVolumeAsync(delta);

    /// <summary>菜单内拖动音量滑杆：同样延迟应用，滚轮预览与拖动共用同一份待应用值。/ Dragging the in-menu volume slider defers the apply as well, sharing one pending value with the wheel preview.</summary>
    private void OnCompactFlyoutVolumeValueRequested(int value) => QueueVolume(value);

    private async Task PreviewOutputDeviceAsync(int delta)
    {
        if (_outputDevices.Count == 0)
            _outputDevices = await _audioInteractionService.GetOutputDevicesAsync();
        if (_outputDevices.Count == 0 || _isClosing)
            return;

        var devices = _outputDevices.ToList();
        var current = _pendingOutputDevice is null
            ? devices.FindIndex(device => device.IsDefault)
            : devices.FindIndex(device => string.Equals(device.Id, _pendingOutputDevice.Id, StringComparison.OrdinalIgnoreCase));
        var index = DeferredCircularSelection.Move(current, delta, devices.Count);
        if (index < 0)
            return;

        _pendingOutputDevice = devices[index];
        // 提示先于延迟应用更新：滚轮停下来时用户已经能看到"切到哪个设备"，1.2 秒后它才真的成为默认设备。
        // The tooltip updates before the deferred apply: by the time the wheel stops the user can already read which device
        // is about to become the default, which happens 1.2 s later.
        CardOutputDevice.ToolTip = AudioTooltipPolicy.BuildOutputDevice(_pendingOutputDevice);
        _compactFlyout?.SetOutputDevicePreview(_pendingOutputDevice);
        _outputDeviceApplyTimer?.Stop();
        _outputDeviceApplyTimer?.Start();
    }

    private async Task PreviewVolumeAsync(int delta)
    {
        _currentVolume ??= await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (_currentVolume is null || _isClosing)
            return;

        var steps = Math.Max(1, Math.Abs(delta) / Mouse.MouseWheelDeltaForOneLine) * (delta > 0 ? 1 : -1);
        var value = Math.Clamp((_pendingVolume ?? _currentVolume.VolumePercent) + steps * 2, 0, 100);
        QueueVolume(value);
    }

    private void QueueVolume(int value)
    {
        if (_currentVolume is null)
            return;

        _pendingVolume = Math.Clamp(value, 0, 100);
        CardVolume.ToolTip = AudioTooltipPolicy.BuildMediaVolume(VolumeSourceName, _pendingVolume.Value);
        _compactFlyout?.SetVolumePreview(_pendingVolume.Value);
        _volumeApplyTimer?.Stop();
        _volumeApplyTimer?.Start();
    }

    /// <summary>收起浮层并把延迟应用一起停掉：岛关闭时不能让一个还在排队的设备/音量变更落到系统上。
    /// Dismisses the flyout and stops its deferred applies: a closing island must not let a queued device or volume change land on the system.</summary>
    private void DisposeCompactFlyout()
    {
        _outputDeviceApplyTimer?.Stop();
        _volumeApplyTimer?.Stop();

        if (_compactFlyout is not { } flyout)
            return;

        flyout.OutputDeviceSelected -= OnCompactFlyoutOutputDeviceSelected;
        flyout.OutputDeviceWheelRequested -= OnCompactFlyoutOutputDeviceWheelRequested;
        flyout.VolumeWheelRequested -= OnCompactFlyoutVolumeWheelRequested;
        flyout.VolumeValueRequested -= OnCompactFlyoutVolumeValueRequested;
        flyout.Dispose();
        _compactFlyout = null;
    }
}
