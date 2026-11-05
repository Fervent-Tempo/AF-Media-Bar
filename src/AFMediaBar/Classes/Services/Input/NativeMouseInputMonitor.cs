// 应用拥有 Raw Input 接收与点击确认；UI 只表达输入意图，不进入系统低级 Hook 链。
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using Microsoft.Extensions.Hosting;

namespace AFMediaBar.Classes.Services;

/// <summary>托盘滚轮时的修饰键快照。</summary>
public sealed record TrayWheelEventArgs(int Delta, bool IsShiftDown, bool IsLeftButtonDown, bool IsRightButtonDown);
/// <summary>全局左键事件的物理屏幕坐标。</summary>
public sealed record NativeMouseButtonEventArgs(int ScreenX, int ScreenY);
/// <summary>按下时的屏幕坐标与 HWND 所属链快照；链在 Raw Input 线程采集，UI 只读。</summary>
internal sealed record NativeMousePressEventArgs(int ScreenX, int ScreenY, IReadOnlyList<nint> WindowChain, long CapturedAt);

/// <summary>独立 Raw Input 线程观察鼠标和 Shift，异步确认组合滚轮后的松键点击。</summary>
public sealed class NativeMouseInputMonitor : IHostedService, IDisposable
{
    // 左/右/中/侧键按下的 RAWMOUSE 标志，与旧钩子的 WM_*BUTTONDOWN 集合一致。
    private const ushort PressMask = 0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0100;
    private readonly ShellTrayIconService _trayIcon;
    private readonly AppLogService? _log;
    private readonly PowerStateMonitor? _power;
    private readonly Dispatcher _dispatcher;
    private readonly RawMouseInputSource _source;
    private readonly RepeatedFailureReporter _failures = new();
    private readonly object _gate = new();
    private readonly MousePressTracker _rawLeft = new();
    private readonly MousePressTracker _rawRight = new();
    private readonly MousePressTracker _localLeft = new();
    private readonly MousePressTracker _localRight = new();
    private readonly Dictionary<IntPtr, int> _devices = [];
    private readonly Queue<Notification> _wheels = new();
    private readonly Queue<Notification> _buttons = new();
    private bool _posted;
    private bool _leftDown;
    private bool _rightDown;
    private int _initialMouseButtons;
    private volatile bool _disposed;
    private int _generation;
    private uint _minimumInputTime = unchecked((uint)Environment.TickCount);
    private InputManager? _inputManager;

    /// <summary>组合根组装唯一监听器，Host 负责启动和停止。</summary>
    public NativeMouseInputMonitor(ShellTrayIconService trayIcon, AppLogService? log = null, PowerStateMonitor? power = null)
        : this(trayIcon, Application.Current.Dispatcher, log, power) { }

    internal NativeMouseInputMonitor(ShellTrayIconService trayIcon, Dispatcher dispatcher, AppLogService? log = null, PowerStateMonitor? power = null)
    {
        _trayIcon = trayIcon; _log = log; _power = power;
        _dispatcher = dispatcher;
        _source = new(OnRawMouse, OnDeviceRemoved, ReportFailure);
    }

    public event EventHandler<TrayWheelEventArgs>? WheelChanged;
    public event EventHandler<NativeMouseButtonEventArgs>? LeftButtonPressed;
    public event EventHandler<NativeMouseButtonEventArgs>? LeftButtonReleased;
    internal event EventHandler<NativeMousePressEventArgs>? ButtonPressed;
    public bool IsLeftButtonDown => KeyDown(1);
    public bool IsRightButtonDown => KeyDown(2);
    public bool IsShiftDown => KeyDown(NativeMethods.VK_SHIFT);
    internal bool IsRawInputRegistered => _source.Registered;
    private static bool KeyDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    public bool TryGetPointerPosition(out Point position)
    {
        if (NativeMethods.GetCursorPos(out var point)) { position = new(point.X, point.Y); return true; }
        position = default;
        return false;
    }

    public bool IsPointerOverTray() => _trayIcon.TryGetBounds(out var bounds) &&
        NativeMethods.GetCursorPos(out var point) && bounds.Contains(point.X, point.Y);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        await _dispatcher.InvokeAsync(() =>
        {
            if (_disposed || _inputManager is not null) return;
            _inputManager = InputManager.Current;
            _inputManager.PreProcessInput += OnLocalInput;
            if (_power is not null) _power.StateChanged += OnPowerChanged;
        }, DispatcherPriority.Send, cancellationToken);
        try
        {
            await _source.Start().WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            var initialButtons = (IsLeftButtonDown ? 1 : 0) | (IsRightButtonDown ? 2 : 0);
            lock (_gate)
            {
                if (_devices.Count == 0)
                {
                    var now = unchecked((uint)Environment.TickCount);
                    _initialMouseButtons = initialButtons;
                    _leftDown = (_initialMouseButtons & 1) != 0; _rightDown = (_initialMouseButtons & 2) != 0;
                    _rawLeft.SetDown(_leftDown, now); _rawRight.SetDown(_rightDown, now);
                }
            }
            if (_source.Registered) _log?.Info("Input", "Raw Input 监听已启动（仅鼠标），未安装低级鼠标 Hook。");
        }
        catch (OperationCanceledException) { _source.Dispose(); throw; }
        catch (Exception exception)
        {
            _source.Dispose();
            _log?.Error("Input", "Raw Input 注册失败，托盘滚轮与全局点击判定将不可用。", exception);
        }
    }

    /// <summary>先排空已排队的 Raw Input，再按本次按压确认；不阻塞 UI 或系统输入。</summary>
    public async Task<bool> ConsumeSuppressedClickAsync(MouseButton button = MouseButton.Left, int? timestamp = null)
    {
        if (_disposed) return true;
        var generation = Volatile.Read(ref _generation);
        var clickTime = unchecked((uint)(timestamp ?? NativeMethods.GetMessageTime()));
        uint? floor;
        lock (_gate)
        {
            if (unchecked((int)(clickTime - _minimumInputTime)) < 0) return true;
            floor = timestamp.HasValue ? Local(button).StartedBefore(clickTime) : null;
        }
        if (_source.Registered)
        {
            try
            {
                // 同步失败按“未抑制”处理：Raw Input 线程异常不应吞掉真实点击。
                if (!await _source.SynchronizeAsync().WaitAsync(TimeSpan.FromMilliseconds(250)))
                {
                    ReportFailure(new TimeoutException("Raw Input 同步失败，未确认本次点击是否应被抑制。"));
                    return false;
                }
            }
            catch (TimeoutException exception) { ReportFailure(exception); return false; }
        }
        if (_disposed || generation != Volatile.Read(ref _generation)) return true;
        lock (_gate)
        {
            // 两份账本都消费，不能因短路留下另一份标记，误吞下一次真实点击。
            var raw = Raw(button).Consume(clickTime, floor);
            var local = Local(button).Consume(clickTime);
            return raw | local;
        }
    }

    private MousePressTracker Raw(MouseButton button) => button == MouseButton.Right ? _rawRight : _rawLeft;
    private MousePressTracker Local(MouseButton button) => button == MouseButton.Right ? _localRight : _localLeft;

    private void OnLocalInput(object sender, PreProcessInputEventArgs args)
    {
        if (_disposed) return;
        var input = args.StagingItem.Input;
        lock (_gate)
        {
            if (unchecked((int)(unchecked((uint)input.Timestamp) - _minimumInputTime)) < 0) return;
            if (input is MouseButtonEventArgs button && button.ChangedButton is MouseButton.Left or MouseButton.Right)
            {
                if (input.RoutedEvent == Mouse.PreviewMouseDownEvent) Local(button.ChangedButton).SetDown(true, unchecked((uint)button.Timestamp));
                if (input.RoutedEvent == Mouse.PreviewMouseUpEvent) Local(button.ChangedButton).SetDown(false, unchecked((uint)button.Timestamp));
            }
            else if (input is MouseWheelEventArgs wheel && input.RoutedEvent == Mouse.PreviewMouseWheelEvent)
            { _localLeft.Wheel(unchecked((uint)wheel.Timestamp)); _localRight.Wheel(unchecked((uint)wheel.Timestamp)); }
        }
    }

    private void OnRawMouse(RawMousePacket packet)
    {
        if (_disposed) return;
        bool pressed;
        bool released;
        bool press = false;
        TrayWheelEventArgs? wheel = null;
        int generation;
        lock (_gate)
        {
            if (unchecked((int)(packet.Time - _minimumInputTime)) < 0) return;
            generation = _generation;
            press = (packet.Buttons & PressMask) != 0;
            if (!_devices.ContainsKey(packet.Device) && _devices.Count >= 64)
            {
                var idle = _devices.Where(item => item.Value == 0).Take(1).ToArray();
                if (idle.Length == 0) return;
                _devices.Remove(idle[0].Key);
            }
            var buttons = _devices.GetValueOrDefault(packet.Device);
            if ((packet.Buttons & 3) != 0) _initialMouseButtons &= ~1;
            if ((packet.Buttons & 12) != 0) _initialMouseButtons &= ~2;
            if ((packet.Buttons & 1) != 0) buttons |= 1;
            if ((packet.Buttons & 2) != 0) buttons &= ~1;
            if ((packet.Buttons & 4) != 0) buttons |= 2;
            if ((packet.Buttons & 8) != 0) buttons &= ~2;
            _devices[packet.Device] = buttons;
            var left = (_initialMouseButtons & 1) != 0 || _devices.Values.Any(value => (value & 1) != 0);
            var right = (_initialMouseButtons & 2) != 0 || _devices.Values.Any(value => (value & 2) != 0);
            pressed = left && !_leftDown; released = !left && _leftDown;
            if (left != _leftDown) _rawLeft.SetDown(left, packet.Time);
            if (right != _rightDown) _rawRight.SetDown(right, packet.Time);
            _leftDown = left; _rightDown = right;
            if ((packet.Buttons & 0x400) != 0)
            {
                _rawLeft.Wheel(packet.Time); _rawRight.Wheel(packet.Time);
                wheel = new(packet.Wheel, IsShiftDown, left, right);
            }
        }
        if (pressed) Enqueue(new(1, packet.X, packet.Y, packet.Time, generation, null));
        if (released) Enqueue(new(2, packet.X, packet.Y, packet.Time, generation, null));
        if (wheel is not null) Enqueue(new(3, packet.X, packet.Y, packet.Time, generation, wheel));
        if (press) PublishButtonPress(packet.X, packet.Y);
    }

    private void OnDeviceRemoved(IntPtr device)
    {
        lock (_gate)
        {
            _devices.Remove(device);
            _leftDown = (_initialMouseButtons & 1) != 0 || _devices.Values.Any(value => (value & 1) != 0);
            _rightDown = (_initialMouseButtons & 2) != 0 || _devices.Values.Any(value => (value & 2) != 0);
            if (!_leftDown) _rawLeft.Clear();
            if (!_rightDown) _rawRight.Clear();
        }
    }

    private void Enqueue(Notification value)
    {
        var post = false;
        lock (_gate)
        {
            if (_disposed || value.Generation != _generation || unchecked((int)(value.Time - _minimumInputTime)) < 0) return;
            if (value.Kind is 1 or 2) { if (_buttons.Count == 16) _buttons.Dequeue(); _buttons.Enqueue(value); }
            else { if (_wheels.Count == 64) _wheels.Dequeue(); _wheels.Enqueue(value); }
            if (!_posted) { _posted = true; post = true; }
        }
        if (!post || _dispatcher.HasShutdownStarted) return;
        try { _dispatcher.BeginInvoke(DrainNotifications, DispatcherPriority.Input); }
        catch (InvalidOperationException) { /* Dispatcher 关闭后不再发布。 */ }
    }

    private void DrainNotifications()
    {
        Notification[] buttons;
        Notification[] wheels;
        lock (_gate)
        {
            buttons = _buttons.ToArray(); wheels = _wheels.ToArray();
            _buttons.Clear(); _wheels.Clear(); _posted = false;
        }
        if (_disposed) return;
        var now = unchecked((uint)Environment.TickCount);
        foreach (var button in buttons)
        {
            if (_disposed) return;
            if (button.Generation != _generation) continue;
            // 按压与释放可能连续到达；放宽窗口，避免 UI 忙时丢掉拖动修复依赖的释放。
            if (button.Kind == 1 && unchecked(now - button.Time) <= 1000)
                LeftButtonPressed?.Invoke(this, new(button.X, button.Y));
            else if (button.Kind == 2 && !IsLeftButtonDown)
                LeftButtonReleased?.Invoke(this, new(button.X, button.Y));
        }
        foreach (var wheel in wheels)
        {
            if (_disposed) return;
            if (wheel.Generation == _generation && unchecked(now - wheel.Time) <= 250 && _trayIcon.TryGetBounds(out var bounds) && bounds.Contains(wheel.X, wheel.Y) &&
                NativeMethods.GetCursorPos(out var current) && bounds.Contains(current.X, current.Y))
                WheelChanged?.Invoke(this, wheel.Wheel!);
        }
    }

    internal void PublishButtonPress(int screenX, int screenY)
    {
        var handlers = ButtonPressed;
        if (_disposed || handlers is null) return;
        var capturedAt = Stopwatch.GetTimestamp();
        // Raw Input 线程只快照 USER32 的窗口身份，再交给 UI；不等待目标窗口或执行关闭逻辑。
        var hit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = screenX, Y = screenY });
        var root = NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);
        var chain = new List<nint>();
        const uint Owner = 4;
        for (var current = root; current != nint.Zero && chain.Count < 32; current = NativeMethods.GetWindow(current, Owner))
            chain.Add(current);
        var args = new NativeMousePressEventArgs(screenX, screenY, chain.ToArray(), capturedAt);
        if (_dispatcher.HasShutdownStarted) return;
        try
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (!_disposed) handlers(this, args);
            }, DispatcherPriority.Input);
        }
        catch (InvalidOperationException)
        {
            // Dispatcher 关闭后不再发布。
        }
    }

    private void OnPowerChanged(object? sender, EventArgs args)
    {
        var buttons = (IsLeftButtonDown ? 1 : 0) | (IsRightButtonDown ? 2 : 0);
        lock (_gate)
        {
            Interlocked.Increment(ref _generation);
            _minimumInputTime = unchecked((uint)Environment.TickCount);
            _devices.Clear(); _rawLeft.Clear(); _rawRight.Clear(); _localLeft.Clear(); _localRight.Clear();
            _initialMouseButtons = buttons;
            _leftDown = (_initialMouseButtons & 1) != 0; _rightDown = (_initialMouseButtons & 2) != 0;
            _rawLeft.SetDown(_leftDown, _minimumInputTime); _rawRight.SetDown(_rightDown, _minimumInputTime);
            _wheels.Clear(); _buttons.Clear();
        }
    }

    private void ReportFailure(Exception exception)
    {
        try { _failures.Report(_log, "Input", "raw-input", "Raw Input 监听或点击确认失败", exception); }
        catch { /* 诊断失败不能逃出原生窗口回调。 */ }
    }

#if DEBUG
    private bool _diagnosticsEnabled;
    internal void SetDiagnosticEnabled(bool enabled) => _diagnosticsEnabled = enabled;
    internal void RequestDiagnosticHeartbeat() { if (_diagnosticsEnabled) _source.RequestHeartbeat(); }
    internal (bool Registered, double PendingMs, double HeartbeatMaxMs, double PacketMaxMs) ReadDiagnosticState() => _source.ReadDiagnostics();
#endif

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        await _source.Completion.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        if (!_dispatcher.HasShutdownStarted)
            await _dispatcher.InvokeAsync(DetachLocalInput, DispatcherPriority.Send, cancellationToken);
        if (_source.ShutdownFailure is { } failure) throw new InvalidOperationException("Raw Input 注册未能正常注销。", failure);
    }

    private void DetachLocalInput()
    {
        if (_inputManager is null) return;
        _inputManager.PreProcessInput -= OnLocalInput;
        _inputManager = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Increment(ref _generation);
            _buttons.Clear(); _wheels.Clear();
        }
        _source.Dispose();
        if (_power is not null) _power.StateChanged -= OnPowerChanged;
        if (_dispatcher.CheckAccess()) DetachLocalInput();
        else if (!_dispatcher.HasShutdownStarted)
        {
            try { _dispatcher.BeginInvoke(DetachLocalInput, DispatcherPriority.Send); }
            catch (InvalidOperationException) { /* Dispatcher 与停止请求并发关闭。 */ }
        }
        WheelChanged = null; LeftButtonPressed = null; LeftButtonReleased = null; ButtonPressed = null;
    }

    private sealed record Notification(int Kind, int X, int Y, uint Time, int Generation, TrayWheelEventArgs? Wheel);
}
