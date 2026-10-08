using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using Microsoft.Win32;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 监听电源、会话与显示器状态，把它们变成三个可以随时读取的布尔值。
/// Watches the power, session, and display state and turns them into three booleans that can be read at any time.
///
/// 三条来源各有覆盖面，缺一不可：
/// - `WM_POWERBROADCAST`（本类自己的隐藏消息窗口）：睡眠前后、以及显示器开关（`GUID_CONSOLE_DISPLAY_STATE`）。
/// - `SystemEvents.PowerModeChanged`：睡眠/唤醒的兜底，和上面的广播互为保险。
/// - `SystemEvents.SessionSwitch`：锁屏与解锁，这是最常见的"用户不在"信号，而且锁屏时显示器广播可能先于或晚于它到达。
/// Three sources with different coverage, none of them redundant:
/// - `WM_POWERBROADCAST`, on this class's own hidden message window: before and after suspend, plus the display state
///   (`GUID_CONSOLE_DISPLAY_STATE`).
/// - `SystemEvents.PowerModeChanged`: a fallback for suspend and resume that insures the broadcast above.
/// - `SystemEvents.SessionSwitch`: lock and unlock, the most common "the user is away" signal, whose arrival order relative to the display
///   broadcast is not fixed.
///
/// 事件可能来自非 UI 线程，因此 <see cref="StateChanged"/> 一律在本类创建时所在线程（UI 线程）上触发；睡眠广播走窗口消息，本身就在 UI 线程上
/// **同步**触发，这一点很重要：深度回收必须来得及在机器睡下之前做完。
/// The events can arrive on a non-UI thread, so <see cref="StateChanged"/> always fires on the thread this class was created on, which is the UI
/// thread. The suspend broadcast comes through a window message and therefore fires **synchronously** on that thread, which matters: the deep
/// reclaim has to finish before the machine actually sleeps.
/// </summary>
public sealed class PowerStateMonitor : IDisposable
{
    private readonly AppLogService? _log;
    private readonly object _gate = new();
    private Dispatcher? _dispatcher;
    private HwndSource? _messageSink;
    private IntPtr _displayStateNotification;
    private bool _isSuspended;
    private bool _isDisplayOff;
    private bool _isSessionLocked;
    private bool _started;
    private volatile bool _disposed;

    /// <summary>
    /// 创建电源状态监听器；实际注册在 <see cref="Start"/> 中进行，且必须在 UI 线程上调用。
    /// Creates the power state monitor; the registrations happen in <see cref="Start"/>, which must be called on the UI thread.
    /// </summary>
    /// <param name="log">程序日志；省略时不记录。/ The application log, omitted for tests.</param>
    public PowerStateMonitor(AppLogService? log = null)
    {
        _log = log;
    }

    /// <summary>电源、会话或显示器状态发生变化时触发（已在 UI 线程上）。/ Raised when the power, session, or display state changes, already on the UI thread.</summary>
    public event EventHandler? StateChanged;

    /// <summary>系统是否正在睡眠（`PBT_APMSUSPEND` 之后、唤醒之前为真）。/ Whether the system is suspending: true after `PBT_APMSUSPEND` and until resume.</summary>
    public bool IsSuspended
    {
        get
        {
            lock (_gate)
            {
                return _isSuspended;
            }
        }
    }

    /// <summary>显示器是否已关闭或变暗。/ Whether the display is off or dimmed.</summary>
    public bool IsDisplayOff
    {
        get
        {
            lock (_gate)
            {
                return _isDisplayOff;
            }
        }
    }

    /// <summary>会话是否已锁定（或正在注销、断开会话）。/ Whether the session is locked, logging off, or disconnecting.</summary>
    public bool IsSessionLocked
    {
        get
        {
            lock (_gate)
            {
                return _isSessionLocked;
            }
        }
    }

    /// <summary>
    /// 显示器状态通知是否注册成功。注册失败不影响睡眠与锁屏信号，只是少了"屏幕熄灭但没锁屏"这一种情形。
    /// Whether the display-state notification was registered. A failure costs only the "screen off without a lock" case; suspend and lock signals
    /// still work.
    /// </summary>
    public bool SupportsDisplayStateNotifications { get; private set; }

    /// <summary>
    /// 用户在系统范围内保持无输入（鼠标与键盘）的时长，由 `GetLastInputInfo` 读取。
    /// How long the user has been idle system-wide, that is without mouse or keyboard input, read from `GetLastInputInfo`.
    ///
    /// 它是**全系统**的空闲时长而不是本进程窗口的：锁屏或焦点在别的程序上时同样准确。
    /// It is the **system-wide** idle time rather than this process's window inactivity, so it stays accurate while the session is locked or the
    /// focus is in another application.
    /// </summary>
    public TimeSpan UserIdle
    {
        get
        {
            var info = new NativeMethods.LastInputInfo
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>()
            };

            if (!NativeMethods.GetLastInputInfo(ref info))
            {
                return TimeSpan.Zero;
            }

            // 用 32 位差值而不是 `Environment.TickCount64 - Time`：`Time` 只有 32 位，系统连续运行超过 49.7 天后两者会差出整整一圈，
            // 那会把空闲时长报成 49 天。无符号 32 位差值天然处理回绕，而空闲时长本来就不可能真的到 49 天。
            // The subtraction is 32-bit on purpose instead of `Environment.TickCount64 - Time`: `Time` is only 32 bits wide, so past 49.7 days of
            // uptime the two differ by a whole wrap and the idle time would read as 49 days. An unsigned 32-bit difference wraps correctly, and a
            // real idle time can never approach that.
            var elapsed = unchecked((uint)Environment.TickCount - info.Time);
            return TimeSpan.FromMilliseconds(elapsed);
        }
    }

    /// <summary>
    /// 注册电源、会话与显示器状态通知。必须在 UI 线程上调用，且只会生效一次。
    /// Registers the power, session, and display-state notifications. It must be called on the UI thread and only takes effect once.
    /// </summary>
    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        _dispatcher = Dispatcher.CurrentDispatcher;

        HookSystemEvents();
        CreateMessageSink();

        _log?.Info(
            "Prune",
            $"电源状态监听已启动（显示器通知 display notifications=" +
            $"{(SupportsDisplayStateNotifications ? "on" : "off")}）");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        UnhookSystemEvents();

        if (_displayStateNotification != IntPtr.Zero)
        {
            NativeMethods.UnregisterPowerSettingNotification(_displayStateNotification);
            _displayStateNotification = IntPtr.Zero;
        }

        if (_messageSink is { } sink)
        {
            sink.RemoveHook(OnMessageSinkMessage);
            sink.Dispose();
            _messageSink = null;
        }
    }

    /// <summary>
    /// 建立本类自己的隐藏消息窗口并注册显示器状态通知。
    /// Creates this class's own hidden message window and registers the display-state notification.
    ///
    /// 这里用一个**普通但不可见**的顶层窗口，而不是 message-only 窗口：广播类消息到不了 message-only 窗口，而电源广播正是广播。
    /// A plain but invisible top-level window is used here instead of a message-only one, because broadcast messages never reach a message-only
    /// window, and the power broadcast is exactly that.
    /// </summary>
    private void CreateMessageSink()
    {
        try
        {
            var parameters = new HwndSourceParameters("AFMediaBarPowerStateMonitor")
            {
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                Width = 0,
                Height = 0
            };

            _messageSink = new HwndSource(parameters);
            _messageSink.AddHook(OnMessageSinkMessage);

            var displayStateGuid = NativeMethods.GuidConsoleDisplayState;
            _displayStateNotification = NativeMethods.RegisterPowerSettingNotification(
                _messageSink.Handle,
                ref displayStateGuid,
                NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
            SupportsDisplayStateNotifications = _displayStateNotification != IntPtr.Zero;
            if (!SupportsDisplayStateNotifications)
            {
                _log?.Warn(
                    "Prune",
                    "注册显示器状态通知失败，只能依赖睡眠与锁屏信号" +
                    "lock signals remain");
            }
        }
        catch (Exception ex)
        {
            _log?.Warn("Prune", $"创建电源消息窗口失败: {ex.Message}");
        }
    }

    private void HookSystemEvents()
    {
        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch (Exception ex)
        {
            // 无消息泵的环境（例如部分测试宿主）会在这里失败；此时只保留窗口消息一路。
            // Environments without a message pump fail here; the window-message path stays available.
            _log?.Warn("Prune", $"订阅系统电源事件失败: {ex.Message}");
        }
    }

    private void UnhookSystemEvents()
    {
        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        catch (Exception ex)
        {
            _log?.Warn("Prune", $"退订系统电源事件失败: {ex.Message}");
        }
    }

    private IntPtr OnMessageSinkMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed || message != NativeMethods.WM_POWERBROADCAST)
        {
            return IntPtr.Zero;
        }

        switch ((int)wParam)
        {
            case NativeMethods.PBT_APMSUSPEND:
                SetSuspended(true);
                handled = true;
                break;
            case NativeMethods.PBT_APMRESUMEAUTOMATIC:
            case NativeMethods.PBT_APMRESUMESUSPEND:
                SetSuspended(false);
                handled = true;
                break;
            case NativeMethods.PBT_POWERSETTINGCHANGE:
                ApplyDisplayStateChange(lParam);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ApplyDisplayStateChange(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
        {
            return;
        }

        var setting = Marshal.PtrToStructure<NativeMethods.PowerBroadcastSetting>(lParam);
        if (setting.PowerSetting != NativeMethods.GuidConsoleDisplayState ||
            setting.DataLength < sizeof(uint))
        {
            return;
        }

        // 「变暗」按关闭处理：它出现在屏保或即将熄灭的时候，那一刻用户已经不在看了，而屏幕一亮就会立刻收到打开通知。
        // "Dimmed" counts as off: it happens on a screen saver or just before the display goes dark, when nobody is watching any more, and the
        // display being turned back on arrives immediately as its own notification.
        var isOff = setting.Data is NativeMethods.ConsoleDisplayStateOff or NativeMethods.ConsoleDisplayStateDimmed;
        lock (_gate)
        {
            if (_disposed || _isDisplayOff == isOff)
            {
                return;
            }

            _isDisplayOff = isOff;
        }

        _log?.Info("Prune", isOff ? "显示器已关闭" : "显示器已打开");
        RaiseStateChanged();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                SetSuspended(true);
                break;
            case PowerModes.Resume:
                SetSuspended(false);
                break;
            default:
                return;
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var locked = e.Reason switch
        {
            SessionSwitchReason.SessionLock => true,
            SessionSwitchReason.SessionLogoff => true,
            SessionSwitchReason.ConsoleDisconnect => true,
            SessionSwitchReason.RemoteDisconnect => true,
            SessionSwitchReason.SessionUnlock => false,
            SessionSwitchReason.ConsoleConnect => false,
            SessionSwitchReason.RemoteConnect => false,
            _ => (bool?)null
        };

        if (locked is not { } value)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || _isSessionLocked == value)
            {
                return;
            }

            _isSessionLocked = value;
        }

        _log?.Info("Prune", value ? "会话已锁定" : "会话已解锁");
        RaiseStateChanged();
    }

    private void SetSuspended(bool value)
    {
        lock (_gate)
        {
            if (_disposed || _isSuspended == value)
            {
                return;
            }

            _isSuspended = value;
        }

        _log?.Info("Prune", value ? "系统即将睡眠" : "系统已唤醒");
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        var dispatcher = _dispatcher;
        if (_disposed || dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;

        void Publish()
        {
            if (!_disposed && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                StateChanged?.Invoke(this, EventArgs.Empty);
        }

        if (dispatcher.CheckAccess())
        {
            Publish();
            return;
        }

        try
        {
            // SystemEvents 的回调在它自己的线程上；参与者的资源（DispatcherTimer、WPF 位图、缓存）都归 UI 线程，
            // 因此在别的线程上直接回调会把"释放资源"变成一次跨线程访问。
            // SystemEvents calls back on its own thread, while the participants' resources — dispatcher timers, WPF bitmaps, caches — belong to
            // the UI thread, so calling straight back from that thread would turn "release the resources" into a cross-thread access.
            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Publish));
        }
        catch (Exception ex)
        {
            _log?.Warn("Prune", $"投递电源状态变化失败: {ex.Message}");
        }
    }
}
