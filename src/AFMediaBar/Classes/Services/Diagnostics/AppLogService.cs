using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using AFMediaBar.Classes.Services.Diagnostics;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

/// <summary>日志级别。</summary>
public enum AppLogLevel { Verbose, Info, Warning, Error }

/// <summary>后台追加关键事件，按大小轮转；Verbose 仍只进入调试输出。</summary>
public sealed class AppLogService : IDisposable
{
    /// <summary>仅用于日志的静态门面，不解析业务服务。</summary>
    public static AppLogService? Current { get; private set; }
    public const long MaximumFileBytes = 5 * 1024 * 1024;
    public const int MaximumFiles = 5;
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(3);
    private readonly BlockingCollection<LogEntry> _pending = new(new ConcurrentQueue<LogEntry>(), 4096);
    private readonly object _lifecycleGate = new();
    private readonly object _ringGate = new();
    private readonly AutoResetEvent _retryWake = new(false);
    private readonly Task _writer;
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _dropped;
    private long _reportedDropped;
    private long _writeFailures;
    private long _reportedWriteFailures;
    private string? _lastWriteError;
    private volatile bool _disposed;

    public AppLogService(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMediaBar", "logs");
        FilePath = Path.Combine(DirectoryPath, "app.log");
        Current = this;
        _writer = Task.Factory.StartNew(WriteLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public string DirectoryPath { get; }
    public string FilePath { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public long DroppedEntries => Interlocked.Read(ref _dropped);
    public long WriteFailures => Interlocked.Read(ref _writeFailures);
    internal bool IsDisposed => _disposed;
#if DEBUG
    internal event Action<PerformanceDiagnosticEvent>? DiagnosticRecorded;
#endif

    [Conditional("DEBUG")]
    internal void Diagnostic(string category, string name, double? durationMs = null, int? browserProcessId = null, string? instance = null, int? generation = null)
    {
#if DEBUG
        try { DiagnosticRecorded?.Invoke(new(category, name, DateTimeOffset.Now, Stopwatch.GetTimestamp(), durationMs, browserProcessId, instance, generation)); }
        catch (Exception exception) { Debug.WriteLine(exception); }
#endif
    }
    public void Info(string category, string message) => Write(AppLogLevel.Info, category, message, null);
    public void Warn(string category, string message) => Write(AppLogLevel.Warning, category, message, null);
    public void Warn(string category, string message, Exception exception) => Write(AppLogLevel.Warning, category, message, exception);
    public void Error(string category, string message, Exception? exception = null) => Write(AppLogLevel.Error, category, message, exception);

    [Conditional("DEBUG")]
    public void Verbose(string category, string message) => Debug.WriteLine(Format(AppLogLevel.Verbose, category, message));

    public void LogSessionStart()
    {
        var assembly = Assembly.GetEntryAssembly();
        var build = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var configuration = "Release";
#if DEBUG
        configuration = "Debug";
#endif
        Info("App", $"===== 启动: AF Media Bar {assembly?.GetName().Version} ({configuration}) =====");
        Info("App", $"build={build} 系统={Environment.OSVersion.VersionString} dotnet={Environment.Version} 64-bit={Environment.Is64BitProcess}");
        Info("App", $"日志文件={FilePath} maximumFileBytes={MaximumFileBytes} maximumFiles={MaximumFiles}");
    }

    public void CaptureUnhandled(string source, Exception exception) => Error("Crash", $"未处理异常: {source}", exception);

    public void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{DirectoryPath}\"", UseShellExecute = true });
        }
        catch (Exception exception) { Warn("App", $"打开日志目录失败: {exception.Message}"); }
    }

    /// <summary>崩溃边界的有界等待；所有文件操作仍由写入线程完成。</summary>
    public void Flush(TimeSpan timeout)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Enqueue(new("", true, completion))) return;
        try { completion.Task.Wait(timeout); }
        catch (AggregateException exception) { Debug.WriteLine(exception); }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(Current, this)) Current = null;
            _pending.CompleteAdding();
            _retryWake.Set();
        }
        try { _writer.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException exception) { Debug.WriteLine(exception); }
        // 队列由写入线程最终释放，超时后不与仍在收尾的消费者竞争。
    }

    private void Write(AppLogLevel level, string category, string message, Exception? exception)
    {
        if (_disposed) return;
        var line = Format(level, category, message);
        if (exception is not null)
        {
            var builder = new StringBuilder(line);
            foreach (var stackLine in exception.ToString().Replace("\r\n", "\n").Split('\n'))
                builder.AppendLine().Append("      ").Append(stackLine);
            line = builder.ToString();
        }
        Debug.WriteLine(line);
        Enqueue(new(line, level == AppLogLevel.Error));
    }

    private bool Enqueue(LogEntry entry)
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return false;
            if (!_pending.TryAdd(entry))
            {
                if (_pending.TryTake(out var removed))
                {
                    removed.Completion?.TrySetCanceled();
                    Interlocked.Increment(ref _dropped);
                }
                _pending.TryAdd(entry);
            }
            if (entry.ForceFlush) _retryWake.Set();
            return true;
        }
    }

    private string Format(AppLogLevel level, string category, string message) =>
        $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level(level)}] [{category}] session={SessionId} pid={Environment.ProcessId} tid={NativeMethods.GetCurrentThreadId()} managedTid={Environment.CurrentManagedThreadId} elapsedMs={Stopwatch.GetElapsedTime(_started).TotalMilliseconds:F1} {message}";

    private static string Level(AppLogLevel level) => level switch
    {
        AppLogLevel.Verbose => "VRB", AppLogLevel.Warning => "WRN", AppLogLevel.Error => "ERR", _ => "INF"
    };

    private void WriteLoop()
    {
        using var file = new RollingLogFile(FilePath, MaximumFileBytes, MaximumFiles);
        var sinceFlush = Stopwatch.StartNew();
        LogEntry? retryEntry = null;
        try
        {
            while (retryEntry.HasValue || !_pending.IsCompleted)
            {
                LogEntry entry;
                if (retryEntry is { } retained)
                {
                    // 失败项留在消费者手中，其余项继续受原队列上限约束；Error/Flush 可提前唤醒重试。
                    if (!_disposed) _retryWake.WaitOne(TimeSpan.FromMilliseconds(250));
                    entry = retained;
                }
                else
                {
                    var remaining = FlushInterval - sinceFlush.Elapsed;
                    _pending.TryTake(out entry, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                }
                var completed = false;
                lock (_ringGate)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(entry.Line)) file.Append(entry.Line);
                        if (entry.ForceFlush || sinceFlush.Elapsed >= FlushInterval || _pending.IsCompleted)
                        {
                            var dropped = DroppedEntries;
                            if (dropped != _reportedDropped)
                            {
                                file.Append(Format(AppLogLevel.Warning, "Log", $"queueDropped={dropped} delta={dropped - _reportedDropped}"));
                            }
                            if (_writeFailures != _reportedWriteFailures)
                            {
                                file.Append(Format(AppLogLevel.Warning, "Log", $"writeFailures={_writeFailures} lastWriteError={_lastWriteError}"));
                            }
                            file.Flush();
                            _reportedDropped = dropped;
                            _reportedWriteFailures = _writeFailures;
                            sinceFlush.Restart();
                        }
                        retryEntry = null;
                        completed = true;
                    }
                    catch (Exception exception)
                    {
                        Interlocked.Increment(ref _writeFailures);
                        _lastWriteError = exception.GetType().Name;
                        retryEntry = entry;
                        Debug.WriteLine($"[Log] 写入失败: {exception.Message}");
                        try { file.Dispose(); }
                        catch (Exception closeException) { Debug.WriteLine($"[Log] 关闭失败: {closeException.Message}"); }
                        if (_disposed) break;
                    }
                    finally { if (completed) entry.Completion?.TrySetResult(); }
                }
            }
        }
        catch (Exception exception) { Debug.WriteLine($"[Log] 写入线程失败: {exception}"); }
        finally
        {
            try { file.Flush(); }
            catch (Exception exception) { Debug.WriteLine(exception); }
            lock (_lifecycleGate)
            {
                _disposed = true;
                if (ReferenceEquals(Current, this)) Current = null;
                retryEntry?.Completion?.TrySetCanceled();
                while (_pending.TryTake(out var abandoned)) abandoned.Completion?.TrySetCanceled();
                _pending.Dispose();
                _retryWake.Dispose();
            }
        }
    }

    private readonly record struct LogEntry(string Line, bool ForceFlush, TaskCompletionSource? Completion = null);
}
