// 独占后台 HWND、Raw Input 鼠标/Shift 注册及缓冲；停止和注销不依赖 UI Dispatcher。
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

internal readonly record struct RawMousePacket(IntPtr Device, ushort Buttons, short Wheel, uint Time, int X, int Y);

internal sealed class RawMouseInputSource : IDisposable
{
    private const uint SynchronizeMessage = NativeMethods.WM_APP + 71;
    private const uint HeartbeatMessage = NativeMethods.WM_APP + 72;
    private readonly Action<RawMousePacket> _mouse;
    private readonly Action<IntPtr> _removed;
    private readonly Action<Exception> _failure;
    private readonly object _gate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool>? _synchronizing;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _window;
    private volatile bool _disposed;
    private volatile bool _registered;
    private IntPtr _buffer;
    private long _heartbeat;
    private long _heartbeatMaximum;
    private long _packetMaximum;
    private Exception? _shutdownFailure;

    internal RawMouseInputSource(Action<RawMousePacket> mouse, Action<IntPtr> removed, Action<Exception> failure)
    { _mouse = mouse; _removed = removed; _failure = failure; }

    internal bool Registered => _registered && !_disposed;
    internal Task Completion => _stopped.Task;
    internal Exception? ShutdownFailure => Volatile.Read(ref _shutdownFailure);

    internal Task Start()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_thread is not null) return _ready.Task;
            _thread = new Thread(Run) { IsBackground = true, Name = "AFMediaBar.RawInput" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
        return _ready.Task;
    }

    internal Task<bool> SynchronizeAsync()
    {
        lock (_gate)
        {
            if (!Registered) return Task.FromResult(false);
            if (_synchronizing is { } pending) return pending.Task;
            var request = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _synchronizing = request;
            if (!NativeMethods.PostThreadMessage(_threadId, SynchronizeMessage, UIntPtr.Zero, IntPtr.Zero))
            { _synchronizing = null; request.TrySetResult(false); }
            return request.Task;
        }
    }

    private void Run()
    {
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);
            using var window = new HwndSource(new HwndSourceParameters("AFMediaBar.RawInput") { ParentWindow = new IntPtr(-3) });
            _window = window.Handle;
            window.AddHook(WindowMessage);
            if (_disposed) return;
            EnsureRegistrationAvailable();
            var devices = Devices(_window, 0x2100);
            if (!NativeMethods.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _registered = true;
            try
            {
                _buffer = Marshal.AllocHGlobal(64);
                _ready.TrySetResult();
                while (!_disposed && NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    if (message.Message == SynchronizeMessage) { DrainBeforeClick(); continue; }
                    if (message.Message == HeartbeatMessage) { CompleteHeartbeat(); continue; }
                    NativeMethods.TranslateMessage(ref message);
                    NativeMethods.DispatchMessage(ref message);
                }
            }
            finally
            {
                try { Unregister(); }
                catch (Exception exception) { _shutdownFailure = exception; throw; }
                finally { window.RemoveHook(WindowMessage); }
            }
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            _failure(exception);
        }
        finally
        {
            _registered = false;
            _window = IntPtr.Zero;
            _threadId = 0;
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
            lock (_gate) { _synchronizing?.TrySetResult(false); _synchronizing = null; }
            _ready.TrySetResult();
            _stopped.TrySetResult();
        }
    }

    private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;
        try
        {
            if (message == 0xfe && wParam.ToInt64() == 2) _removed(lParam);
            if (message == 0xff) ReadPacket(lParam);
        }
        catch (Exception exception) { _failure(exception); }
        // 保留 DefWindowProc 对前台 WM_INPUT 的清理；不使用 NOLEGACY，不改变普通窗口输入。
        return IntPtr.Zero;
    }

    private void ReadPacket(IntPtr input)
    {
        var began = Stopwatch.GetTimestamp();
        var size = 64u;
        var headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        if (NativeMethods.GetRawInputData(input, 0x10000003, _buffer, ref size, headerSize) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(_buffer);
        if (size < headerSize) return;
        var offset = (int)headerSize;
        if (header.Type == 0 && size >= headerSize + 24)
        {
            var buttons = unchecked((ushort)Marshal.ReadInt16(_buffer, offset + 4));
            if (buttons != 0)
            {
                var position = NativeMethods.GetMessagePos();
                _mouse(new(header.Device, buttons, Marshal.ReadInt16(_buffer, offset + 6),
                    unchecked((uint)NativeMethods.GetMessageTime()), unchecked((short)position), unchecked((short)(position >> 16))));
            }
        }
        Maximum(ref _packetMaximum, Stopwatch.GetTimestamp() - began);
    }

    private void DrainBeforeClick()
    {
        // PostThreadMessage 可能先于待取的输入消息；显式排空当前批次，再完成异步点击屏障。
        var count = 0;
        while (!_disposed && count++ < 512 && NativeMethods.PeekMessage(out var input, _window, 0xff, 0xff, NativeMethods.PM_REMOVE))
        { NativeMethods.TranslateMessage(ref input); NativeMethods.DispatchMessage(ref input); }
        if (!_disposed && NativeMethods.PeekMessage(out _, _window, 0xff, 0xff, NativeMethods.PM_NOREMOVE))
        {
            if (NativeMethods.PostThreadMessage(_threadId, SynchronizeMessage, UIntPtr.Zero, IntPtr.Zero)) return;
        }
        lock (_gate) { _synchronizing?.TrySetResult(!_disposed); _synchronizing = null; }
    }

    private static NativeMethods.RAWINPUTDEVICE[] Devices(IntPtr target, uint flags) =>
        [new() { UsagePage = 1, Usage = 2, Flags = flags, Target = target }];

    private static NativeMethods.RAWINPUTDEVICE[] Registrations()
    {
        var count = 0u;
        var size = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>();
        if (NativeMethods.GetRegisteredRawInputDevices(null, ref count, size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var devices = new NativeMethods.RAWINPUTDEVICE[count];
        if (count != 0 && NativeMethods.GetRegisteredRawInputDevices(devices, ref count, size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return devices;
    }

    private static void EnsureRegistrationAvailable()
    {
        if (Registrations().Any(item => item.UsagePage == 1 && item.Usage == 2))
            throw new InvalidOperationException("当前进程已有鼠标 Raw Input 接收者，未覆盖现有注册。");
    }

    private void Unregister()
    {
        if (!_registered) return;
        try
        {
            // 其他组件若替换了接收窗口，不注销其注册。
            var owned = Registrations().Where(item => item.Target == _window && item.UsagePage == 1 && item.Usage == 2)
                .Select(item => new NativeMethods.RAWINPUTDEVICE { UsagePage = item.UsagePage, Usage = item.Usage, Flags = 1 }).ToArray();
            if (owned.Length != 0 && !NativeMethods.RegisterRawInputDevices(owned, (uint)owned.Length, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { _registered = false; }
    }

    internal void RequestHeartbeat()
    {
        if (!Registered) return;
        var now = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(ref _heartbeat, now, 0) == 0 &&
            !NativeMethods.PostThreadMessage(_threadId, HeartbeatMessage, UIntPtr.Zero, IntPtr.Zero))
            Interlocked.CompareExchange(ref _heartbeat, 0, now);
    }

    private void CompleteHeartbeat()
    {
        var sent = Interlocked.Exchange(ref _heartbeat, 0);
        if (sent != 0) Maximum(ref _heartbeatMaximum, Stopwatch.GetTimestamp() - sent);
    }

    internal (bool Registered, double PendingMs, double HeartbeatMaxMs, double PacketMaxMs) ReadDiagnostics()
    {
        var sent = Interlocked.Read(ref _heartbeat);
        return (Registered, sent == 0 ? 0 : Stopwatch.GetElapsedTime(sent).TotalMilliseconds,
            Stopwatch.GetElapsedTime(0, Interlocked.Exchange(ref _heartbeatMaximum, 0)).TotalMilliseconds,
            Stopwatch.GetElapsedTime(0, Interlocked.Exchange(ref _packetMaximum, 0)).TotalMilliseconds);
    }

    private static void Maximum(ref long target, long value)
    {
        var previous = Interlocked.Read(ref target);
        while (value > previous)
        {
            var observed = Interlocked.CompareExchange(ref target, value, previous);
            if (observed == previous) break;
            previous = observed;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_thread is null) { _ready.TrySetResult(); _stopped.TrySetResult(); }
            _synchronizing?.TrySetResult(false);
            _synchronizing = null;
        }
        var threadId = _threadId;
        if (threadId != 0) NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
    }
}
