using TaskbarPlayer.Interop;

namespace TaskbarPlayer.Services;

internal sealed class TrayIconService : IDisposable
{
    internal const int CallbackMessage = NativeMethods.WmApp + 1;

    private const uint IconId = 1;
    private readonly nint _window;
    private readonly nint _icon;
    private readonly uint _taskbarCreatedMessage;
    private bool _isAdded;

    internal TrayIconService(nint window)
    {
        _window = window;
        _icon = NativeMethods.LoadIcon(nint.Zero, new nint(NativeMethods.IdiApplication));
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        AddIcon();
    }

    internal event EventHandler? ContextMenuRequested;
    internal event EventHandler? DoubleClicked;

    internal bool HandleWindowMessage(int message, nint wParam, nint lParam)
    {
        if ((uint)message == _taskbarCreatedMessage)
        {
            _isAdded = false;
            AddIcon();
            return false;
        }

        if (message != CallbackMessage)
        {
            return false;
        }

        var notification = (int)(lParam.ToInt64() & 0xFFFF);
        if (notification is NativeMethods.WmContextMenu or NativeMethods.WmRightButtonUp)
        {
            ContextMenuRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (notification == NativeMethods.WmLeftButtonDoubleClick)
        {
            DoubleClicked?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
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
        data.Tooltip = "网易云任务栏播放器";

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
        if (!_isAdded)
        {
            return;
        }

        var data = CreateIconData();
        NativeMethods.ShellNotifyIcon(NativeMethods.NotifyIconDelete, ref data);
        _isAdded = false;
    }
}
