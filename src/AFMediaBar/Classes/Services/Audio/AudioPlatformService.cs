// 拥有音频 COM 专用线程和端点通知；原生对象只在该线程创建、使用和释放。
using System.Diagnostics;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>区分用户策略变化与原生设备通知，避免默认输出变化误取消输入请求。</summary>
public sealed class AudioDeviceInvalidationEventArgs(bool cancelPending) : EventArgs
{
    public bool CancelPending { get; } = cancelPending;
}

/// <summary>串行执行音频平台操作，发布设备代际，不在 UI 线程等待原生调用。</summary>
public sealed class AudioPlatformService : IDisposable
{
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private readonly List<Action> _cleanup = [];
    private int _disposed;
    private int _version;
    private DispatcherTimer? _propertyCoalesceTimer;
    private bool _propertyNotificationPending;
    public int Version => Volatile.Read(ref _version);
    public event EventHandler? Changed;

    public AudioPlatformService()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "AF Audio COM" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void RegisterCleanup(Action cleanup) => _cleanup.Add(cleanup);

    private void Run()
    {
        MMDeviceEnumerator? enumerator = null;
        MMDeviceNotificationClient? notifications = null;
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            // 属性通知一次采集启停会来几十条；固定窗口合并成一次，设备增删/状态/默认变化仍立即发布。
            _propertyCoalesceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _propertyCoalesceTimer.Tick += OnPropertyCoalesceTick;
            enumerator = new MMDeviceEnumerator();
            notifications = enumerator.CreateNotificationClient(false);
            notifications.DeviceAdded += (_, _) => Invalidate();
            notifications.DeviceRemoved += (_, _) => Invalidate();
            notifications.DeviceStateChanged += (_, _) => Invalidate();
            notifications.DefaultDeviceChanged += (_, _) => Invalidate();
            notifications.PropertyValueChanged += (_, args) =>
            {
                // 环回采集启停本身会通知所有端点；把它转发回去会形成“重建采集 → 通知 → 再重建”的自激循环。
                // 已知生命周期键先过滤，其余属性只作为“需要重新解析”的脏标记，并合并同一突发。
                if (AudioEndpointNotificationPolicy.ShouldInvalidate(args.PropertyKey)) InvalidateCoalesced();
            };
            _ready.TrySetResult(dispatcher);
            if (Volatile.Read(ref _disposed) == 0) Dispatcher.Run();
        }
        catch (Exception exception) { _ready.TrySetException(exception); Debug.WriteLine(exception); }
        finally
        {
            if (_propertyCoalesceTimer is { } timer)
            {
                timer.Stop();
                timer.Tick -= OnPropertyCoalesceTick;
            }
            _propertyCoalesceTimer = null;
            foreach (var cleanup in _cleanup)
                try { cleanup(); } catch (Exception exception) { Debug.WriteLine(exception); }
            if (enumerator is not null)
            {
                try { notifications?.Dispose(); }
                catch (Exception exception) { Debug.WriteLine(exception); }
                enumerator.Dispose();
            }
        }
    }

    public async Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var dispatcher = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return operation();
        }, DispatcherPriority.Normal, cancellationToken).Task.ConfigureAwait(false);
    }

    public void Invalidate(bool cancelPending = false)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Increment(ref _version);
        // 显式失效（设备增删/状态/默认变化、用户切换）不合并；只排队，业务刷新由订阅者异步处理。
        if (_ready.Task.IsCompletedSuccessfully)
            _ready.Task.Result.BeginInvoke(() =>
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                _propertyNotificationPending = false;
                _propertyCoalesceTimer?.Stop();
                PublishChanged(cancelPending);
            });
    }

    /// <summary>合并同一轮属性通知突发；版本仍逐条递增，只有 Changed 的发布被合并。</summary>
    private void InvalidateCoalesced()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Increment(ref _version);
        if (!_ready.Task.IsCompletedSuccessfully) return;
        _ready.Task.Result.BeginInvoke(() =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _propertyNotificationPending = true;
            if (_propertyCoalesceTimer is { IsEnabled: false } timer) timer.Start();
        });
    }

    private void OnPropertyCoalesceTick(object? sender, EventArgs args)
    {
        _propertyCoalesceTimer?.Stop();
        if (!_propertyNotificationPending || Volatile.Read(ref _disposed) != 0) return;
        _propertyNotificationPending = false;
        PublishChanged(cancelPending: false);
    }

    private void PublishChanged(bool cancelPending)
    {
        if (Volatile.Read(ref _disposed) != 0 || Changed is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
            try { handler(this, new AudioDeviceInvalidationEventArgs(cancelPending)); } catch (Exception exception) { Debug.WriteLine(exception); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _ready.TrySetCanceled();
        if (_ready.Task.IsCompletedSuccessfully)
            _ready.Task.Result.BeginInvokeShutdown(DispatcherPriority.Send);
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
    }


}
