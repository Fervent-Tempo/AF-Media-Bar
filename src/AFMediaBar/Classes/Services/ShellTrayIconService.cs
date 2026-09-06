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
    private IntPtr _icon;
    private bool _isAdded;
    private bool _disposed;

    public ShellTrayIconService()
    {
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
        AddIcon();
    }

    public event EventHandler? LeftClicked;
    public event EventHandler? ContextMenuRequested;
    public event EventHandler? ShellRestarted;

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

    public void UpdateTooltip(string? text)
    {
        // 不设置 Shell 原生 tooltip，避免它与自绘音频反馈气泡叠加。
        // Do not set the Shell tooltip, so it cannot overlap the custom audio feedback bubble.
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

        return IntPtr.Zero;
    }

    private void LoadApplicationIcon()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        _ = NativeMethods.ExtractIconEx(executable, 0, out var large, out var small, 1);
        _icon = small != IntPtr.Zero ? small : large;
        if (_icon == small && large != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(large);
        }
    }

    private void AddIcon()
    {
        var data = CreateData();
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON;
        data.szTip = string.Empty;
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
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

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
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}
