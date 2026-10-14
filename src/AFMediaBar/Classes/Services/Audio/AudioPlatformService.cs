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
            enumerator = new MMDeviceEnumerator();
            notifications = enumerator.CreateNotificationClient(false);
            notifications.DeviceAdded += (_, _) => Invalidate();
            notifications.DeviceRemoved += (_, _) => Invalidate();
            notifications.DeviceStateChanged += (_, _) => Invalidate();
            notifications.DefaultDeviceChanged += (_, _) => Invalidate();
            notifications.PropertyValueChanged += (_, _) => Invalidate();
            _ready.TrySetResult(dispatcher);
            if (Volatile.Read(ref _disposed) == 0) Dispatcher.Run();
        }
        catch (Exception exception) { _ready.TrySetException(exception); Debug.WriteLine(exception); }
        finally
        {
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
        // 通知可能来自原生回调；只排队，业务刷新由订阅者异步处理。
        if (_ready.Task.IsCompletedSuccessfully)
            _ready.Task.Result.BeginInvoke(() =>
            {
                if (Volatile.Read(ref _disposed) != 0 || Changed is not { } handlers) return;
                foreach (EventHandler handler in handlers.GetInvocationList())
                    try { handler(this, new AudioDeviceInvalidationEventArgs(cancelPending)); } catch (Exception exception) { Debug.WriteLine(exception); }
            });
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
