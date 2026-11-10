// 共享宠物的指针采样与点击通知；所有租约在 UI 线程使用，最后租约释放时停止采样。
// 借用现有鼠标监听器，不启动、停止或释放它。
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace AFMediaBar.Classes.Services;

/// <summary>宠物的一次输入：物理屏幕坐标，以及是否需要眨眼。</summary>
public readonly record struct PetInputSnapshot(Point ScreenPosition, bool Blink, long ObservedAtTicks = 0);

/// <summary>按可见宠物的租约共享全局输入，不拦截系统鼠标操作。</summary>
public sealed class PetInputService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Func<Point?> _readPointer;
    private readonly NativeMouseInputMonitor? _mouse;
    private readonly List<Lease> _leases = [];
    private Point? _lastPosition;
    private bool _disposed;

    public PetInputService(NativeMouseInputMonitor mouse)
        : this(Application.Current.Dispatcher, () => mouse.TryGetPointerPosition(out var position) ? position : null)
    {
        _mouse = mouse;
    }

    internal PetInputService(Dispatcher dispatcher, Func<Point?> readPointer)
    {
        _dispatcher = dispatcher;
        _readPointer = readPointer;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1d / 30)
        };
        _timer.Tick += OnTick;
    }

    internal bool IsSampling => _timer.IsEnabled;
    internal long LastButtonPressTicks => _mouse?.LastButtonPressTicks ?? 0;

    /// <summary>取得 UI 线程租约；首次通知为当前指针，不重放此前的点击。</summary>
    public IDisposable Subscribe(Action<PetInputSnapshot> receive)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lease = new Lease(this, receive);
        _leases.Add(lease);
        if (_leases.Count == 1)
        {
            _lastPosition = null;
            if (_mouse is not null)
            {
                _mouse.LeftButtonPressed += OnPressed;
                _mouse.RightButtonPressed += OnPressed;
            }
            _timer.Start();
        }
        if (_readPointer() is { } position)
        {
            if (_leases.Count == 1) _lastPosition = position;
            lease.Publish(new PetInputSnapshot(position, false));
        }
        return lease;
    }

    private void OnTick(object? sender, EventArgs args) => SamplePointer();

    internal void SamplePointer()
    {
        _dispatcher.VerifyAccess();
        if (_disposed || _leases.Count == 0 || _readPointer() is not { } position || _lastPosition == position)
            return;
        _lastPosition = position;
        foreach (var lease in _leases.ToArray())
            lease.Publish(new PetInputSnapshot(position, false));
    }

    private void OnPressed(object? sender, NativeMouseButtonEventArgs args) =>
        PublishPress(new Point(args.ScreenX, args.ScreenY), args.ObservedAtTicks);

    internal void PublishPress(Point position, long observedAtTicks)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        foreach (var lease in _leases.ToArray())
        {
            // Hook 事件可能在 UI 队列里跨过隐藏、重建和重新订阅，不能给新租约补播旧点击。
            if (observedAtTicks >= lease.ActivatedAtTicks)
                lease.Publish(new PetInputSnapshot(position, true, observedAtTicks));
        }
    }

    private void Release(Lease lease)
    {
        _dispatcher.VerifyAccess();
        _leases.Remove(lease);
        if (_leases.Count == 0) StopSampling();
    }

    private void StopSampling()
    {
        _timer.Stop();
        _lastPosition = null;
        if (_mouse is not null)
        {
            _mouse.LeftButtonPressed -= OnPressed;
            _mouse.RightButtonPressed -= OnPressed;
        }
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        StopSampling();
        foreach (var lease in _leases.ToArray()) lease.Dispose();
        _timer.Tick -= OnTick;
    }

    private sealed class Lease(PetInputService owner, Action<PetInputSnapshot> receive) : IDisposable
    {
        private Action<PetInputSnapshot>? _receive = receive;
        internal long ActivatedAtTicks { get; } = Stopwatch.GetTimestamp();

        internal void Publish(PetInputSnapshot snapshot)
        {
            try { _receive?.Invoke(snapshot); }
            catch (Exception exception) { Debug.WriteLine($"[PetInputService] Presentation failed: {exception}"); }
        }

        public void Dispose()
        {
            owner._dispatcher.VerifyAccess();
            if (_receive is null) return;
            _receive = null;
            owner.Release(this);
        }
    }
}
