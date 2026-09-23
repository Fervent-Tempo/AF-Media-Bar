using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 管理 AF Media Bar 的 Shell 通知区域图标及 Explorer 重启恢复。
/// Manages the AF Media Bar Shell notification icon and Explorer restart recovery.
/// </summary>
public sealed class ShellTrayIconService : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = NativeMethods.WM_APP + 17;
    private readonly HwndSource _messageWindow;
    private readonly uint _taskbarCreatedMessage;
    private readonly AppIconService _appIconService;
    private AppIconService.TrayIcon? _trayIcon;
    private IntPtr _icon;
    private string _tooltipText = "AF Media Bar";
    private bool _isAdded;
    private bool _disposed;

    /// <summary>
    /// 创建隐藏消息窗口、注册 Explorer 重建消息并立即安装通知区域图标。
    /// Creates the hidden message window, registers for Explorer recreation, and installs the notification icon immediately.
    /// </summary>
    /// <param name="appIconService">按主题提供托盘图标的服务。/ Service that supplies the tray icon for the active theme.</param>
    public ShellTrayIconService(AppIconService appIconService)
    {
        _appIconService = appIconService;
        _messageWindow = new HwndSource(new HwndSourceParameters("AFMediaBar.ShellTrayWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = NativeMethods.WS_POPUP,
            ExtendedWindowStyle = NativeMethods.WS_EX_NOACTIVATE
        });
        _messageWindow.AddHook(WindowHook);
        _taskbarCreatedMessage = unchecked((uint)NativeMethods.RegisterWindowMessage("TaskbarCreated"));
        LoadApplicationIcon();

        // 主题换图时托盘跟着换：通知区域本身就是浅色/深色两套，一套图形在另一套上会看不清。
        // The tray follows an artwork change: the notification area itself comes in light and dark, and one artwork is hard to read
        // on the other.
        _appIconService.IconChanged += OnApplicationIconChanged;
        AddIcon();
    }

    public event EventHandler? LeftClicked;
    public event EventHandler? ContextMenuRequested;
    public event EventHandler? TooltipOpening;
    public event EventHandler? ShellRestarted;

    /// <summary>
    /// 用户点击了气泡通知（不是超时或关闭）。调用方据此把用户带到相关界面。
    /// The user clicked the balloon notification, rather than letting it time out or dismissing it. Callers use this
    /// to take the user to the relevant surface.
    /// </summary>
    public event EventHandler? NotificationClicked;

    /// <summary>
    /// 弹出一次 Shell 气泡通知（Windows 10/11 上以系统通知的形式呈现）。
    ///
    /// 只做一次 Shell 调用，不排队也不重试：通知是"顺便告知"，失败不应该影响调用它的业务流程。
    /// 标题与正文按 Shell 的上限截断（标题 63、正文 255），超长会让整个调用被拒绝。
    /// Shows one Shell balloon notification, presented as a system notification on Windows 10/11.
    ///
    /// It is a single Shell call with no queueing and no retry: a notification is an aside, and its failure must not
    /// disturb the flow that asked for it. Title and body are truncated to the Shell's limits (63 and 255), because
    /// over-long text makes the whole call fail.
    /// </summary>
    /// <param name="title">通知标题。/ Notification title.</param>
    /// <param name="message">通知正文。/ Notification body.</param>
    /// <returns>Shell 是否接受了这次通知。/ Whether the Shell accepted the notification.</returns>
    public bool TryShowNotification(string title, string message)
    {
        if (!_isAdded)
        {
            return false;
        }

        var data = CreateData();
        data.uFlags = NativeMethods.NIF_INFO;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(message, 255);
        data.dwInfoFlags = NativeMethods.NIIF_INFO;

        // NOTIFYICON_VERSION_4 之后 Shell 自己决定停留时长，这里的取值不会被使用，但保持有意义的默认值。
        // Since NOTIFYICON_VERSION_4 the Shell decides how long the balloon stays, so this value is unused while
        // still carrying a meaningful default.
        data.uTimeoutOrVersion = 10000;
        return NativeMethods.ShellNotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private static string Truncate(string? text, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    /// <summary>
    /// 查询通知图标的物理屏幕边界，图标尚未安装或 Shell 查询失败时返回 <see langword="false"/>。
    /// Queries the notification icon bounds in physical screen coordinates; returns <see langword="false"/> when unavailable.
    /// </summary>
    public bool TryGetBounds(out TrayIconBounds bounds)
    {
        bounds = default;
        if (!_isAdded)
        {
            return false;
        }

        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
            hWnd = _messageWindow.Handle,
            uID = IconId
        };
        if (NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect) != 0)
        {
            return false;
        }

        bounds = new TrayIconBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    /// <summary>
    /// 规范化并截断提示文本，然后在图标存在时同步更新 Shell 状态。
    /// Normalizes and truncates tooltip text, then updates Shell state when the icon is installed.
    /// </summary>
    public void UpdateTooltip(string? text)
    {
        var normalized = string.IsNullOrWhiteSpace(text)
            ? "AF Media Bar"
            : string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        _tooltipText = normalized.Length <= 127 ? normalized : normalized[..127];
        if (!_isAdded)
        {
            return;
        }

        var data = CreateData();
        data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        _ = NativeMethods.ShellNotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private IntPtr WindowHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (unchecked((uint)message) == _taskbarCreatedMessage)
        {
            _isAdded = false;
            AddIcon();
            ShellRestarted?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        if (message != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        var notification = unchecked((int)(lParam.ToInt64() & 0xFFFF));
        if (notification is NativeMethods.NIN_SELECT or NativeMethods.NIN_KEYSELECT)
        {
            LeftClicked?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (notification == NativeMethods.WM_CONTEXTMENU)
        {
            ContextMenuRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (notification == NativeMethods.NIN_POPUPOPEN)
        {
            TooltipOpening?.Invoke(this, EventArgs.Empty);
        }
        else if (notification == NativeMethods.NIN_BALLOONUSERCLICK)
        {
            NotificationClicked?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 取当前主题对应的托盘图标。
    ///
    /// 旧实现是从 exe 的 Win32 图标资源里抽一份：那份图标是静态的，在深色任务栏上永远看不清，而通知区域本身就有浅色与
    /// 深色两套。现在图标由 <see cref="AppIconService"/> 按主题给出，主题变化时由 <see cref="OnApplicationIconChanged"/>
    /// 换成另一套。
    /// Takes the tray icon for the current theme. The previous implementation extracted the executable's static Win32 icon: that one
    /// can never be read on a dark taskbar, and the notification area itself comes in a light and a dark set. The icon is now
    /// supplied by <see cref="AppIconService"/> per theme, and <see cref="OnApplicationIconChanged"/> swaps it when the theme moves.
    /// </summary>
    private void LoadApplicationIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = _appIconService.ResolveTrayIcon();
        _icon = _trayIcon?.Handle ?? IntPtr.Zero;
    }

    private void OnApplicationIconChanged()
    {
        if (_disposed)
        {
            return;
        }

        var next = _appIconService.ResolveTrayIcon();
        if (next is null)
        {
            // 解析失败就继续用当前图标：托盘留成空白比图标旧一点糟得多。
            // Resolution failed, so the current icon stays: a blank tray is far worse than a slightly stale icon.
            return;
        }

        var previous = _trayIcon;
        _trayIcon = next;
        _icon = next.Handle;
        if (_isAdded)
        {
            // uFlags 必须写明这次要改的是图标：NIM_MODIFY 的 uFlags 是"改哪些字段"的位掩码，`CreateData` 不设它
            // （只有 AddIcon 会设），留着 0 就等于告诉 Shell"什么都不用改"，调用返回成功而托盘图标一动不动。
            // uFlags has to name the field being changed: for NIM_MODIFY it is a bitmask of what to update, and `CreateData`
            // leaves it unset (only AddIcon sets it). Leaving it at 0 tells the Shell "change nothing": the call succeeds and the
            // tray icon never moves.
            var data = CreateData();
            data.uFlags = NativeMethods.NIF_ICON;
            NativeMethods.ShellNotifyIcon(NativeMethods.NIM_MODIFY, ref data);
        }

        // 换图之后再释放旧句柄：Shell 读取的是本次调用时传进去的那一个。
        // The previous handle is released only after the swap: the Shell read the one handed to it in this call.
        previous?.Dispose();
    }

    private void AddIcon()
    {
        var data = CreateData();
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON |
            NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        _isAdded = NativeMethods.ShellNotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (!_isAdded)
        {
            Debug.WriteLine($"[ShellTrayIconService] Add failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        data.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.ShellNotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
    }

    private NativeMethods.NOTIFYICONDATA CreateData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _messageWindow.Handle,
        uID = IconId,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltipText,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    /// <summary>
    /// 幂等移除通知图标、消息钩子和本服务拥有的原生图标句柄。
    /// Idempotently removes the notification icon, message hook, and native icon handle owned by this service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_isAdded)
        {
            var data = CreateData();
            NativeMethods.ShellNotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _isAdded = false;
        }

        _messageWindow.RemoveHook(WindowHook);
        _messageWindow.Dispose();
        _appIconService.IconChanged -= OnApplicationIconChanged;

        // 句柄属于 TrayIcon 所有权包，因此这里只释放包本身；再单独 DestroyIcon 会变成一次二次释放。
        // The handle belongs to the TrayIcon ownership wrapper, so only the wrapper is released here; a separate DestroyIcon call
        // would free it a second time.
        _trayIcon?.Dispose();
        _trayIcon = null;
        _icon = IntPtr.Zero;
    }
}
