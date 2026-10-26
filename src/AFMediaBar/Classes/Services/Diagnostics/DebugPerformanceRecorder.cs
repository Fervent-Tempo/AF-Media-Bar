#if DEBUG
// App 拥有记录服务；采样回调和 Hook 只入队或更新计数，后台任务独占资源读取及文件。
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using Microsoft.Extensions.Hosting;

namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>Debug 默认启动的有界本地性能记录器，不依赖工具窗口或开发者模式。</summary>
public sealed class DebugPerformanceRecorder(SystemMetricsMonitorService metrics, SystemMetricsService sampler,
    NativeMouseInputMonitor mouse, PowerStateMonitor power, AppLogService log) : IHostedService, IDisposable
{
    private const long MaximumFileBytes = 10 * 1024 * 1024;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly object _gate = new();
    private Run? _run;
    private bool _disposed;
    private string _state = "Stopped";
    private long _lastDroppedSamples;
    private long _lastDroppedEvents;
    private long _lastSampleUtcTicks;

    internal string DirectoryPath => Path.Combine(log.DirectoryPath, "performance");

    internal (string State, long DroppedSamples, long DroppedEvents, DateTimeOffset? LastSample) GetStatus()
    {
        lock (_gate)
        {
            var ticks = _run?.LastSampleUtcTicks ?? _lastSampleUtcTicks;
            return (_state, _run?.DroppedSamples ?? _lastDroppedSamples, _run?.DroppedEvents ?? _lastDroppedEvents,
                ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero));
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested) StartRecording();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken) => await StopRecordingAsync(cancellationToken).ConfigureAwait(false);

    internal bool StartRecording()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            if (_run is { Accepting: true }) return true;
            if (_run is not null) return false;
            var run = new Run();
            _run = run;
            _state = "Running";
            log.DiagnosticRecorded += OnDiagnostic;
            power.StateChanged += OnPowerChanged;
            mouse.SetDiagnosticEnabled(!IsPaused);
            run.Writer = Task.Run(() => WriteAsync(run));
            run.Heartbeat = Task.Run(() => HeartbeatAsync(run));
            run.Completion = CompleteAsync(run);
            try { run.Lease = metrics.SubscribeDiagnostics(TimeSpan.FromSeconds(1), snapshot => Capture(run, snapshot)); }
            catch (Exception exception)
            {
                run.Failed = true;
                RequestStop(run);
                log.Warn("Performance", "无法订阅性能采样，已停止记录", exception);
                return false;
            }
        }
        log.Info("Performance", "Debug 持续性能记录已开启；每秒采样，空闲降频，熄屏或睡眠暂停。");
        return true;
    }

    internal async Task<bool> StopRecordingAsync(CancellationToken token = default)
    {
        Run? run;
        lock (_gate) { run = _run; if (run is not null) RequestStop(run); }
        if (run is null) return true;
        try { await run.Completion.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false); return true; }
        catch (TimeoutException) { log.Warn("Performance", "性能记录仍在后台收尾，未等待界面线程。"); return false; }
    }

    internal void Mark()
    {
        lock (_gate)
            if (_run is { Accepting: true } run)
            {
                AddEvent(run, new("Manual", "just-happened", DateTimeOffset.Now, Stopwatch.GetTimestamp()));
                Interlocked.Exchange(ref run.FlushRequested, 1);
                Wake(run);
            }
    }

    internal void OpenFolder()
    {
        Directory.CreateDirectory(DirectoryPath);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DirectoryPath}\"") { UseShellExecute = true });
    }

    private bool IsPaused => power.IsDisplayOff || power.IsSuspended || power.IsSessionLocked;

    private void Capture(Run run, SystemMetricsSnapshot snapshot)
    {
        var gpu = sampler.GetDiagnosticGpuUsage();
        lock (_gate)
        {
            if (!ReferenceEquals(run, _run) || !run.Accepting) return;
            if (IsPaused) return;
            var pending = Interlocked.Read(ref run.UiPending);
            var record = new RawSample(DateTimeOffset.Now, Stopwatch.GetTimestamp(), snapshot, gpu, sampler.GetDiagnosticBusiestEngine(), metrics.DiagnosticInterval.TotalMilliseconds,
                GC.GetTotalPauseDuration().TotalMilliseconds, GC.GetTotalMemory(false), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                pending == 0 ? 0 : Stopwatch.GetElapsedTime(pending).TotalMilliseconds,
                Stopwatch.GetElapsedTime(0, Interlocked.Exchange(ref run.UiMaximum, 0)).TotalMilliseconds,
                mouse.ReadDiagnosticState(), Interlocked.Exchange(ref run.ResetBaseline, 0) != 0);
            if (!run.Samples.TryAdd(record))
            {
                if (run.Samples.TryTake(out _)) Interlocked.Increment(ref run.DroppedSamples);
                run.Samples.TryAdd(record);
            }
            run.LastSampleUtcTicks = record.Time.UtcTicks;
            Wake(run);
        }
    }

    private void OnDiagnostic(PerformanceDiagnosticEvent value)
    {
        lock (_gate)
        {
            if (_run is not { Accepting: true } run) return;
            AddEvent(run, value);
            if (value.Name.Contains("failed", StringComparison.Ordinal) || value.DurationMs >= 500)
                Interlocked.Exchange(ref run.FlushRequested, 1);
        }
    }

    private void OnPowerChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_run is not { Accepting: true } run) return;
            Interlocked.Exchange(ref run.UiPending, 0);
            Interlocked.Exchange(ref run.ResetBaseline, 1);
            mouse.SetDiagnosticEnabled(!IsPaused);
            AddEvent(run, new("Power", IsPaused ? "paused" : "resumed", DateTimeOffset.Now, Stopwatch.GetTimestamp()));
        }
    }

    private static void AddEvent(Run run, PerformanceDiagnosticEvent value)
    {
        if (!run.Events.TryAdd(value))
        {
            if (run.Events.TryTake(out _)) Interlocked.Increment(ref run.DroppedEvents);
            run.Events.TryAdd(value);
        }
        Wake(run);
    }

    private static void Wake(Run run)
    {
        try { if (run.Signal.CurrentCount == 0) run.Signal.Release(); }
        catch (SemaphoreFullException) { }
    }

    private async Task HeartbeatAsync(Run run)
    {
        try
        {
            while (!run.Cancellation.IsCancellationRequested)
            {
                if (!IsPaused && run.Accepting && !_dispatcher.HasShutdownStarted)
                {
                    var sent = Stopwatch.GetTimestamp();
                    if (Interlocked.CompareExchange(ref run.UiPending, sent, 0) == 0)
                        _ = _dispatcher.BeginInvoke(() =>
                        {
                            if (!run.Accepting || Interlocked.CompareExchange(ref run.UiPending, 0, sent) != sent) return;
                            UpdateMaximum(ref run.UiMaximum, Stopwatch.GetTimestamp() - sent);
                        }, DispatcherPriority.Normal);
                    mouse.RequestDiagnosticHeartbeat();
                }
                await Task.Delay(IsPaused ? 1000 : 250, run.Cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            lock (_gate)
                if (run.Accepting) AddEvent(run, new("Heartbeat", "failed", DateTimeOffset.Now, Stopwatch.GetTimestamp()));
            log.Warn("Performance", "诊断心跳停止", exception);
        }
    }

    private async Task WriteAsync(Run run)
    {
        var environment = new
        {
            build = typeof(DebugPerformanceRecorder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            pid = Environment.ProcessId, os = Environment.OSVersion.VersionString, dotnet = Environment.Version.ToString(),
            sampleIntervalMs = 1000, maximumFileBytes = MaximumFileBytes, maximumFiles = 5, debuggerAttached = Debugger.IsAttached
        };
        using var file = new PerformanceLogWriter(Path.Combine(DirectoryPath, "performance.jsonl"), MaximumFileBytes, 5,
            log.SessionId, run.Id, run.Started, environment);
        using var processes = new DebugProcessResourceSampler();
        long? previousSample = null;
        double previousGcPause = 0;
        var flush = Stopwatch.StartNew();
        var markers = new Dictionary<string, (long Timestamp, int Suppressed)>();
        var gpuHighCount = 0;
        void Write(string kind, object payload, DateTimeOffset time, long timestamp) => file.Write(kind, payload, time, timestamp);
        void Mark(string reason, DateTimeOffset time, long timestamp, object detail)
        {
            markers.TryGetValue(reason, out var previous);
            if (previous.Timestamp != 0 && Stopwatch.GetElapsedTime(previous.Timestamp, timestamp).TotalSeconds < 60)
            {
                markers[reason] = (previous.Timestamp, previous.Suppressed + 1);
                return;
            }
            Write("marker", new { reason, previouslySuppressed = previous.Suppressed, detail }, time, timestamp);
            markers[reason] = (timestamp, 0);
            Interlocked.Exchange(ref run.FlushRequested, 1);
        }
        try
        {
            Write("start", new { }, DateTimeOffset.Now, Stopwatch.GetTimestamp());
            while (run.Accepting || run.Events.Count > 0 || run.Samples.Count > 0)
            {
                for (var count = 0; count < 128 && run.Events.TryTake(out var diagnostic); count++)
                {
                    Write("event", diagnostic, diagnostic.Time, diagnostic.Timestamp);
                    if (diagnostic.Name.Contains("failed", StringComparison.Ordinal)) Mark("webview-or-operation-failure", diagnostic.Time, diagnostic.Timestamp, new { diagnostic.Category, diagnostic.Name });
                    if (diagnostic.Category == "Prune" && diagnostic.DurationMs >= 500) Mark("slow-trim", diagnostic.Time, diagnostic.Timestamp, new { diagnostic.DurationMs, diagnostic.Instance });
                }
                for (var count = 0; count < 64 && run.Samples.TryTake(out var sample); count++)
                {
                    var interval = previousSample is long last ? Stopwatch.GetElapsedTime(last, sample.Timestamp).TotalMilliseconds : (double?)null;
                    var reset = sample.ResetBaseline || interval > Math.Max(2000, sample.ExpectedIntervalMs * 2);
                    var resources = processes.Sample(reset);
                    // 资源是在后台读取的；新进程不能承接早于自身启动时刻的 GPU 负载。
                    sampler.SetDiagnosticProcessIds(resources.Select(item => item.Pid).ToHashSet());
                    var hook = sample.Mouse;
                    var uiPendingMs = sample.UiPendingMs;
                    var uiMaximumMs = sample.UiMaximumMs;
                    var engines = GpuProcessEngineUsage.SummarizeOwned(sample.Gpu, resources, sample.Time, Environment.ProcessId);
                    file.WriteSample(new
                    {
                        sample.Metrics, sample.ExpectedIntervalMs, actualIntervalMs = interval, resourcesCapturedAt = DateTimeOffset.Now,
                        baselineReset = reset, gcPauseDeltaMs = sample.ResetBaseline ? (double?)null : sample.GcPauseTotalMs - previousGcPause,
                        sample.ManagedBytes, sample.Gen0, sample.Gen1, sample.Gen2,
                        ui = new { pendingMs = uiPendingMs, maximumMs = uiMaximumMs },
                        mouse = new { hook.Installed, hook.PendingMs, hook.HeartbeatMaxMs, hook.CallbackMaxMs, hook.ForwardMaxMs },
                        debuggerAttached = Debugger.IsAttached, run.DroppedSamples, run.DroppedEvents,
                        sampleAgeMs = Stopwatch.GetElapsedTime(sample.Timestamp).TotalMilliseconds
                    }, resources, sample.GlobalGpuEngine, engines, DescribeTopGpu(sample.Gpu, sample.Time), sample.Time, sample.Timestamp);
                    if (!sample.ResetBaseline && interval > Math.Max(2000, sample.ExpectedIntervalMs * 2)) Mark("sample-gap", sample.Time, sample.Timestamp, new { actualIntervalMs = interval, sample.ExpectedIntervalMs });
                    if (Math.Max(uiPendingMs, uiMaximumMs) > 500) Mark("ui-delay", sample.Time, sample.Timestamp, new { uiPendingMs, uiMaximumMs });
                    if (Math.Max(hook.PendingMs, hook.HeartbeatMaxMs) > 500) Mark("hook-thread-delay", sample.Time, sample.Timestamp, new { hook.PendingMs, hook.HeartbeatMaxMs });
                    if (hook.CallbackMaxMs > 50) Mark("hook-callback-delay", sample.Time, sample.Timestamp, new { hook.CallbackMaxMs, hook.ForwardMaxMs });
                    gpuHighCount = sample.Metrics.SystemGpuPercent >= 90 ? gpuHighCount + 1 : 0;
                    if (gpuHighCount == 3) Mark("gpu-high", sample.Time, sample.Timestamp, new { sample.Metrics.SystemGpuPercent });
                    previousSample = sample.Timestamp;
                    previousGcPause = sample.GcPauseTotalMs;
                }
                if (Interlocked.Exchange(ref run.FlushRequested, 0) != 0 || flush.Elapsed.TotalSeconds >= 2 || !run.Accepting)
                { file.Flush(); flush.Restart(); }
                if (run.Accepting && run.Events.Count == 0 && run.Samples.Count == 0)
                    await run.Signal.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            Write("stop", new { run.DroppedSamples, run.DroppedEvents, suppressedMarkers = markers.Select(item => new { reason = item.Key, item.Value.Timestamp, item.Value.Suppressed }).ToArray() }, DateTimeOffset.Now, Stopwatch.GetTimestamp());
            file.Flush();
        }
        catch (Exception exception)
        {
            run.Failed = true;
            log.Warn("Performance", "持续性能记录失败，已停止；可在开发者工具中重试。", exception);
        }
        finally
        {
            try { file.Dispose(); }
            catch (Exception exception) { run.Failed = true; log.Warn("Performance", "性能文件关闭失败", exception); }
            lock (_gate)
            {
                RequestStop(run);
                _lastDroppedSamples = run.DroppedSamples;
                _lastDroppedEvents = run.DroppedEvents;
                _lastSampleUtcTicks = run.LastSampleUtcTicks;
                if (ReferenceEquals(run, _run))
                {
                    _state = run.Failed ? "Failed" : "Stopped";
                    _run = null;
                }
            }
        }
    }

    private static GpuProcessIdentityUsage[] DescribeTopGpu(GpuProcessEngineUsage[] entries, DateTimeOffset capturedAt) =>
        entries.Where(item => item.Percent > 0).OrderByDescending(item => item.Percent).Take(5).Select(item =>
    {
        string? name = null;
        DateTimeOffset? startedAt = null;
        try
        {
            using var process = Process.GetProcessById(item.Pid);
            var candidateStart = new DateTimeOffset(process.StartTime);
            if (GpuProcessEngineUsage.CanAssociateProcess(candidateStart, capturedAt))
            {
                name = process.ProcessName;
                startedAt = candidateStart;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return new GpuProcessIdentityUsage(item.Pid, name, startedAt, item.Adapter, item.Engine, item.Percent);
    }).ToArray();

    private void RequestStop(Run run)
    {
        if (!run.Accepting) return;
        run.Accepting = false;
        _state = "Stopping";
        run.Lease?.Dispose();
        run.Cancellation.Cancel();
        log.DiagnosticRecorded -= OnDiagnostic;
        power.StateChanged -= OnPowerChanged;
        mouse.SetDiagnosticEnabled(false);
        Wake(run);
    }

    private async Task CompleteAsync(Run run)
    {
        try { await Task.WhenAll(run.Writer, run.Heartbeat).ConfigureAwait(false); }
        catch (Exception exception) { log.Warn("Performance", "性能记录后台任务未能正常收尾", exception); }
        finally
        {
            run.Cancellation.Dispose();
            run.Samples.Dispose();
            run.Events.Dispose();
            run.Signal.Dispose();
        }
    }

    private static void UpdateMaximum(ref long target, long value)
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
            if (_run is { } run) RequestStop(run);
        }
    }

    private sealed record RawSample(DateTimeOffset Time, long Timestamp, SystemMetricsSnapshot Metrics,
        GpuProcessEngineUsage[] Gpu, GpuEngineTotal? GlobalGpuEngine, double ExpectedIntervalMs, double GcPauseTotalMs, long ManagedBytes,
        int Gen0, int Gen1, int Gen2, double UiPendingMs, double UiMaximumMs,
        (bool Installed, double PendingMs, double HeartbeatMaxMs, double CallbackMaxMs, double ForwardMaxMs) Mouse, bool ResetBaseline);

    private sealed class Run
    {
        internal string Id { get; } = Guid.NewGuid().ToString("N");
        internal long Started { get; } = Stopwatch.GetTimestamp();
        internal BlockingCollection<RawSample> Samples { get; } = new(new ConcurrentQueue<RawSample>(), 1024);
        internal BlockingCollection<PerformanceDiagnosticEvent> Events { get; } = new(new ConcurrentQueue<PerformanceDiagnosticEvent>(), 128);
        internal SemaphoreSlim Signal { get; } = new(0, 1);
        internal CancellationTokenSource Cancellation { get; } = new();
        internal Task Writer { get; set; } = Task.CompletedTask;
        internal Task Heartbeat { get; set; } = Task.CompletedTask;
        internal Task Completion { get; set; } = Task.CompletedTask;
        internal IDisposable? Lease { get; set; }
        internal volatile bool Accepting = true;
        internal bool Failed;
        internal long DroppedSamples;
        internal long DroppedEvents;
        internal long LastSampleUtcTicks;
        internal long UiPending;
        internal long UiMaximum;
        internal int FlushRequested;
        internal int ResetBaseline = 1;
    }
}
#endif
