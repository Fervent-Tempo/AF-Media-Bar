// 仅维护已注册工具窗口的桌面/置顶层级；组合根持有，最后租约释放时停止 Hook 与计时器。
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using AFMediaBar.Classes.Settings;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>观察桌面及前台变化，恢复悬浮条的显示与层级，不拦截系统快捷键。</summary>
public sealed partial class DesktopWidgetZOrderService : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<nint, Entry> _entries = [];
    private readonly DispatcherTimer _timer;
    private readonly WinEventProc _callback;
    private GCHandle _callbackRoot;
    private nint _systemHook;
    private nint _desktopHook;
    private int _generation;
    private bool _queued;
    private bool _disposed;
    private readonly AppLogService? _log;

    public DesktopWidgetZOrderService(AppLogService? log = null)
    {
        _log = log;
        _callback = OnWindowEvent;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// 把一个悬浮窗口交给本服务维护层级。overlayTaskbar 开启时窗口始终压在任务栏之上，
    /// 任务栏刷新后重新置顶；关闭时按 layer 与桌面状态决定层级。
    /// </summary>
    public IDisposable Attach(nint handle, FloatingBarLayer layer, Func<bool> mayRestore, bool overlayTaskbar)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsWindow(handle)) throw new InvalidOperationException("Invalid floating window handle.");
        var entry = new Entry(handle, layer, mayRestore, overlayTaskbar);
        _entries[handle] = entry;
        SetWindowLong(handle, GWL_EXSTYLE, (GetWindowLong(handle, GWL_EXSTYLE) | 0x80) & ~0x40000);
        if (_entries.Count == 1) StartObservation();
        Refresh();
        return new Registration(this, entry);
    }

    private void StartObservation()
    {
        _generation++;
        _callbackRoot = GCHandle.Alloc(_callback);
        _systemHook = SetWinEventHook(3, 0x17, nint.Zero, _callback, 0, 0, 2);
        _desktopHook = SetWinEventHook(0x20, 0x20, nint.Zero, _callback, 0, 0, 2);
        if (_systemHook == nint.Zero || _desktopHook == nint.Zero)
            _log?.Warn("FloatingBar", "桌面事件监听未全部注册，使用层级观察计时器恢复窗口");
        _timer.Start();
    }

    private void OnWindowEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (_disposed || _queued || _entries.Count == 0 || _dispatcher.HasShutdownStarted) return;
        var generation = _generation;
        _queued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            _queued = false;
            if (!_disposed && generation == _generation) Refresh();
        }));
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (_disposed || _entries.Count == 0) return;
        var lowestApplication = nint.Zero;
        var desktopIsAboveApplications = false;
        var seenApplication = false;
        var inspected = 0;
        var taskbarOrder = int.MaxValue;
        var entryOrders = new Dictionary<nint, int>();
        for (var window = GetTopWindow(nint.Zero); window != nint.Zero && inspected++ < 512; window = GetWindow(window, 2))
        {
            if (_entries.ContainsKey(window)) { entryOrders[window] = inspected; continue; }
            if (!IsWindowVisible(window) || IsIconic(window)) continue;
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            var className = name.ToString();
            if (className is "Progman" or "WorkerW")
            {
                desktopIsAboveApplications = !seenApplication;
                break;
            }
            // 记下任务栏在 z 序里的位置，用来判断覆盖任务栏的悬浮条有没有被压到它后面。
            if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                taskbarOrder = inspected;
                continue;
            }
            if ((GetWindowLong(window, GWL_EXSTYLE) & 0x80) != 0) continue;
            seenApplication = true;
            lowestApplication = window;
        }
        foreach (var entry in _entries.Values.ToArray())
        {
            if (!IsWindow(entry.Handle) || !entry.MayRestore()) continue;
            var restore = IsIconic(entry.Handle) || !IsWindowVisible(entry.Handle);
            if (restore) ShowWindow(entry.Handle, 4);
            var wantsTopmost = entry.OverlayTaskbar || entry.Layer == FloatingBarLayer.Topmost || desktopIsAboveApplications;
            var after = wantsTopmost ? new nint(-1) :
                lowestApplication == nint.Zero ? new nint(-2) : lowestApplication;
            // 任务栏被点开或刷新后会把悬浮条压到后面，所以覆盖任务栏时每次观察都确认它还在前面。
            var buried = entry.OverlayTaskbar && taskbarOrder != int.MaxValue &&
                entryOrders.TryGetValue(entry.Handle, out var order) && order > taskbarOrder;
            // 置顶样式不会因为把窗口插到别的窗口后面就消失：桌面层必须先摘掉它，否则仍浮在所有应用之上。
            var needsTopmostReset = !wantsTopmost && (GetWindowLong(entry.Handle, GWL_EXSTYLE) & 0x8) != 0;
            var previous = GetWindow(entry.Handle, 3);
            if (restore || needsTopmostReset || entry.LastAfter != after || buried ||
                (entry.Layer == FloatingBarLayer.Desktop && desktopIsAboveApplications && previous != nint.Zero && !_entries.ContainsKey(previous) && (GetWindowLong(previous, GWL_EXSTYLE) & 8) == 0))
            {
                if (needsTopmostReset)
                    SetWidgetWindowPosition(entry.Handle, new nint(-2), 0, 0, 0, 0,
                        SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                SetWidgetWindowPosition(entry.Handle, after, 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                entry.LastAfter = after;
            }
        }
    }

    private void Detach(Entry entry)
    {
        _dispatcher.VerifyAccess();
        if (!_entries.TryGetValue(entry.Handle, out var current) || !ReferenceEquals(current, entry)) return;
        _entries.Remove(entry.Handle);
        if (_entries.Count == 0) StopObservation();
    }

    private void StopObservation()
    {
        _generation++;
        _queued = false;
        _timer.Stop();
        if (_systemHook != nint.Zero) UnhookWinEvent(_systemHook);
        if (_desktopHook != nint.Zero) UnhookWinEvent(_desktopHook);
        _systemHook = _desktopHook = nint.Zero;
        if (_callbackRoot.IsAllocated) _callbackRoot.Free();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _dispatcher.VerifyAccess();
        _disposed = true;
        StopObservation();
        _entries.Clear();
        _timer.Tick -= OnTick;
    }

    private sealed class Entry(nint handle, FloatingBarLayer layer, Func<bool> mayRestore, bool overlayTaskbar)
    {
        public nint Handle { get; } = handle;
        public FloatingBarLayer Layer { get; } = layer;
        public Func<bool> MayRestore { get; } = mayRestore;
        public bool OverlayTaskbar { get; } = overlayTaskbar;
        public nint? LastAfter { get; set; }
    }

    private sealed class Registration(DesktopWidgetZOrderService owner, Entry entry) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (_disposed) return; _disposed = true; owner.Detach(entry); }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);
    [LibraryImport("user32.dll")]
    private static partial nint GetTopWindow(nint window);
    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWidgetWindowPosition(nint window, nint after, int x, int y, int width, int height, uint flags);
}
