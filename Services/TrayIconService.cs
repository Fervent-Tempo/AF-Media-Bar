using System.Windows.Interop;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

/// <summary>
/// 管理 Shell 通知区域图标、回调消息及 Explorer 重启后的自动恢复。
/// Manages the Shell tray icon, callback messages, and recovery after Explorer restarts.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    internal const int CallbackMessage = NativeMethods.WmApp + 1;

    private const uint IconId = 1;
    private readonly HwndSource _messageSource;
    private readonly nint _window;
    private readonly nint _icon;
    private readonly uint _taskbarCreatedMessage;
    private bool _isAdded;

    internal TrayIconService()
    {
        var parameters = new HwndSourceParameters("AFMediaBar.ShellMessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)NativeMethods.WsPopup),
            ExtendedWindowStyle =
                NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate
        };
        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(WindowMessageHook);
        _window = _messageSource.Handle;
        _icon = NativeMethods.LoadIcon(
            NativeMethods.GetModuleHandle(null),
            new nint(NativeMethods.IdiApplication));
        if (_icon == nint.Zero)
        {
            _icon = NativeMethods.LoadIcon(
                nint.Zero,
                new nint(NativeMethods.IdiApplication));
        }

        // Explorer 重启会清空通知区域；TaskbarCreated 到达后必须重新添加图标。
        // Explorer restart clears the tray; TaskbarCreated requires adding the icon again.
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        AddIcon();
    }

    internal event EventHandler? ContextMenuRequested;
    internal event EventHandler? DoubleClicked;
    internal event EventHandler? ShellRestarted;

    private nint WindowMessageHook(
        nint window,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if ((uint)message == _taskbarCreatedMessage)
        {
            _isAdded = false;
            AddIcon();
            InvokeSafely(ShellRestarted, "tray-shell-restarted");
            return nint.Zero;
        }

        if (message != CallbackMessage)
        {
            return nint.Zero;
        }

        var notification = (int)(lParam.ToInt64() & 0xFFFF);
        if (notification is NativeMethods.WmContextMenu or NativeMethods.WmRightButtonUp)
        {
            InvokeSafely(ContextMenuRequested, "tray-context-menu");
            handled = true;
            return nint.Zero;
        }

        if (notification == NativeMethods.WmLeftButtonDoubleClick)
        {
            InvokeSafely(DoubleClicked, "tray-double-click");
            handled = true;
        }

        return nint.Zero;
    }

    private void InvokeSafely(EventHandler? handler, string category)
    {
        try
        {
            handler?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write(category, exception);
        }
    }

    internal void UpdateTooltip(string tooltip)
    {
        if (!_isAdded)
        {
            return;
        }

        var data = CreateIconData();
        data.Flags = NativeMethods.NotifyIconTip | NativeMethods.NotifyIconShowTip;
        data.Tooltip = TrimTooltip(tooltip);
        NativeMethods.ShellNotifyIcon(NativeMethods.NotifyIconModify, ref data);
    }

    private void AddIcon()
    {
        var data = CreateIconData();
        data.Flags = NativeMethods.NotifyIconMessage |
            NativeMethods.NotifyIconIcon |
            NativeMethods.NotifyIconTip |
            NativeMethods.NotifyIconShowTip;
        data.Tooltip = "AF Media Bar";

        _isAdded = NativeMethods.ShellNotifyIcon(NativeMethods.NotifyIconAdd, ref data);
        if (!_isAdded)
        {
            return;
        }

        data.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        NativeMethods.ShellNotifyIcon(NativeMethods.NotifyIconSetVersion, ref data);
    }

    private NativeMethods.NotifyIconData CreateIconData()
    {
        return NativeMethods.NotifyIconData.Create(
            _window,
            IconId,
            CallbackMessage,
            _icon);
    }

    private static string TrimTooltip(string value)
    {
        return value.Length < 128 ? value : value[..127];
    }

    public void Dispose()
    {
        // Shell 图标不会随托管对象自动消失，应在窗口销毁前显式删除。
        // Shell icons do not follow managed lifetime; remove the icon before window teardown.
        if (_isAdded)
        {
            var data = CreateIconData();
            NativeMethods.ShellNotifyIcon(NativeMethods.NotifyIconDelete, ref data);
            _isAdded = false;
        }

        ContextMenuRequested = null;
        DoubleClicked = null;
        ShellRestarted = null;
        _messageSource.RemoveHook(WindowMessageHook);
        _messageSource.Dispose();
    }
}
