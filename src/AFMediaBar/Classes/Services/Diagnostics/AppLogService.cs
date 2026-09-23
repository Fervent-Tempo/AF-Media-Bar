using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace AFMediaBar.Classes.Services;

/// <summary>日志级别。/ Log level.</summary>
public enum AppLogLevel
{
    /// <summary>细节：只进调试输出，不写文件（例如每次悬停变化、播放进度心跳）。/ Detail: debug output only, never the file (per-hover changes, playback progress heartbeats).</summary>
    Verbose = 0,

    /// <summary>关键事件：启动、设置读写、媒体切换、取词结果、设备与音量应用、更新链路、退出。/ Key events: startup, settings I/O, media switches, lyric results, device and volume applies, the update chain, exit.</summary>
    Info = 1,

    /// <summary>可恢复的问题：某个来源失败、注册表写不进去、候选落空。/ Recoverable problems: a failing source, a refused registry write, a candidate that never applied.</summary>
    Warning = 2,

    /// <summary>异常：未处理异常与明确的失败路径，带堆栈。/ Exceptions: unhandled exceptions and clear failure paths, with a stack trace.</summary>
    Error = 3
}

/// <summary>
/// 程序日志：一个文件、只留最近若干条，出问题时把这一份发出来即可。
///
/// 设计取舍（自用场景）：
/// - **只有一个文件**：`%LOCALAPPDATA%\AFMediaBar\logs\app.log`。用户只会在"感觉哪里不对"时来翻日志，
///   因此不需要按天切分、保留多份或导出第二份文本——多出来的入口只会让人不知道该发哪个文件。
/// - **只留最近 <see cref="MaximumLines"/> 条**：日志在内存里维护一个环形窗口，落盘时整份重写。
///   这样文件永远有界（约 120 KB），而且**旧会话的尾部会保留**：崩溃发生在退出路径或上一次运行时也能看到现场。
/// - **不阻塞界面**：界面上只入队，写入在专用后台线程；普通行按 <see cref="RewriteInterval"/> 合并重写，
///   错误行立刻重写（崩溃现场不能被合并窗口拖掉）。
/// - **细节不落盘**：<see cref="AppLogLevel.Verbose"/> 只进调试输出（`Debug.WriteLine`，Release 下编译期移除），
///   文件里留的都是能直接读的关键信息。
/// Application log: one file, only the newest entries, which is the whole bug report.
///
/// Design trade-offs for a single-user tool:
/// - **Exactly one file**: `%LOCALAPPDATA%\AFMediaBar\logs\app.log`. A user opens the log only when something feels wrong, so daily files,
///   several retained copies, and a second exported text file would only raise the question of which one to send.
/// - **Only the newest <see cref="MaximumLines"/> entries**: the log keeps a ring window in memory and rewrites the whole file, which stays
///   bounded (about 120 KB) and *keeps the tail of earlier sessions*, so a crash on the exit path or in a previous run is still visible.
/// - **It never blocks the interface**: the UI thread only enqueues while a dedicated background thread writes; ordinary lines are coalesced
///   into one rewrite per <see cref="RewriteInterval"/>, and an error line rewrites immediately so a crash site is never stuck behind that window.
/// - **Detail stays out of the file**: <see cref="AppLogLevel.Verbose"/> goes to the debug output only (`Debug.WriteLine`, compiled out in
///   Release), leaving the file readable.
/// </summary>
public sealed class AppLogService : IDisposable
{
    /// <summary>
    /// 进程内的日志入口。控件与静态入口（例如由 XAML 构造的 `TaskBarMediaControl`）拿不到注入，因此与 `SettingsManager` 一样保留
    /// 这一处静态门面；它只用于写日志，不解析服务。
    /// In-process log entry point. Controls and static entry points — for example `TaskBarMediaControl`, which XAML constructs — cannot be
    /// injected, so this one static facade stays, exactly like `SettingsManager`. It is used to write log lines and resolves no services.
    /// </summary>
    public static AppLogService? Current { get; private set; }

    /// <summary>
    /// 文件里保留的最大行数。1000 行按正常使用的频率（切歌、取词、设置变更各一两行）大约覆盖几小时到一天，
    /// 足以包含"刚发现问题"之前的上下文；而文件本身约 120 KB，任何聊天窗口都能直接发出去。
    /// Maximum lines kept in the file. At normal activity — a couple of lines per track change, lyric lookup, or settings change — a thousand
    /// lines covers several hours to a day, which is more than the context around "I just noticed something wrong", while the file itself stays
    /// around 120 KB and can be sent through any chat window.
    /// </summary>
    public const int MaximumLines = 1000;

    /// <summary>普通行合并重写文件的间隔；错误行不受它影响，会立刻重写。/ Interval that coalesces ordinary lines into one file rewrite; error lines ignore it and rewrite immediately.</summary>
    public static readonly TimeSpan RewriteInterval = TimeSpan.FromSeconds(3);

    private readonly BlockingCollection<LogEntry> _pending = new(new ConcurrentQueue<LogEntry>(), 4096);
    private readonly Queue<string> _lines = new(MaximumLines + 1);
    private readonly object _lifecycleGate = new();
    private readonly object _ringGate = new();
    private readonly Task _writer;
    private bool _dirty;
    private volatile bool _disposed;

    /// <summary>
    /// 创建日志服务、打开（或在同一文件里接续）日志文件，并启动后台写入线程。
    /// Creates the log service, opens the log file — or continues it — and starts the background writer.
    /// </summary>
    /// <param name="directoryPath">日志目录；省略时取 `%LOCALAPPDATA%\AFMediaBar\logs`。/ Log directory; defaults to `%LOCALAPPDATA%\AFMediaBar\logs`.</param>
    public AppLogService(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AFMediaBar",
            "logs");
        FilePath = Path.Combine(DirectoryPath, "app.log");
        Current = this;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            LoadExistingTail();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Log] 无法准备日志目录 / cannot prepare the log directory: {ex.Message}");
        }

        _writer = Task.Factory.StartNew(
            WriteLoop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <summary>日志目录。/ The log directory.</summary>
    public string DirectoryPath { get; }

    /// <summary>日志文件（就是需要发出来的那一份）。/ The log file, which is the one to send.</summary>
    public string FilePath { get; }

    /// <summary>写一条关键事件。/ Writes one key event.</summary>
    /// <param name="category">分类，例如 Media、Lyrics、Taskbar。/ Category such as Media, Lyrics, or Taskbar.</param>
    /// <param name="message">内容。/ Message.</param>
    public void Info(string category, string message) => Write(AppLogLevel.Info, category, message, null);

    /// <summary>写一条可恢复的问题。/ Writes one recoverable problem.</summary>
    /// <param name="category">分类。/ Category.</param>
    /// <param name="message">内容。/ Message.</param>
    public void Warn(string category, string message) => Write(AppLogLevel.Warning, category, message, null);

    /// <summary>写一条异常，带完整堆栈；它会立刻落盘，不参与合并重写。/ Writes one exception with its full stack trace; it lands on disk immediately instead of joining the coalesced rewrite.</summary>
    /// <param name="category">分类。/ Category.</param>
    /// <param name="message">内容。/ Message.</param>
    /// <param name="exception">异常；可以为 null（只记消息）。/ The exception, or null to log the message alone.</param>
    public void Error(string category, string message, Exception? exception = null) =>
        Write(AppLogLevel.Error, category, message, exception);

    /// <summary>写一条只进调试输出的细节。/ Writes one detail line that only reaches the debug output.</summary>
    /// <param name="category">分类。/ Category.</param>
    /// <param name="message">内容。/ Message.</param>
    [Conditional("DEBUG")]
    public void Verbose(string category, string message) =>
        Debug.WriteLine(Format(AppLogLevel.Verbose, category, message));

    /// <summary>
    /// 记录一次启动：写会话分隔行与运行环境（版本、构建类型、系统、日志文件），报告里一眼能看到运行条件。
    /// Records one start: a session separator plus the runtime environment — version, build type, OS, log file — so a report shows the conditions
    /// at a glance.
    /// </summary>
    public void LogSessionStart()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var isDebug = false;
#if DEBUG
        isDebug = true;
#endif
        Info("App", $"===== 启动 / session start: AF Media Bar {version} ({(isDebug ? "Debug" : "Release")}) =====");
        Info("App", $"系统 / OS: {Environment.OSVersion.VersionString} (.NET {Environment.Version}), 64-bit={Environment.Is64BitProcess}");
        Info("App", $"日志文件 / log file: {FilePath}（只保留最近 {MaximumLines} 条 / newest {MaximumLines} lines only）");
    }

    /// <summary>
    /// 记录未处理异常（Dispatcher、AppDomain、未观察的任务异常都走这里）。
    /// Records an unhandled exception; the dispatcher, the app domain, and unobserved task exceptions all come through here.
    /// </summary>
    /// <param name="source">来源标识，例如 `Dispatcher`。/ Source tag such as `Dispatcher`.</param>
    /// <param name="exception">异常。/ The exception.</param>
    public void CaptureUnhandled(string source, Exception exception) =>
        Error("Crash", $"未处理异常 / unhandled exception from {source}", exception);

    /// <summary>打开日志文件夹；失败时记一条警告而不是抛给界面。/ Opens the log folder, logging a warning instead of throwing at the interface.</summary>
    public void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{DirectoryPath}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Warn("App", $"打开日志目录失败 / cannot open the log directory: {ex.Message}");
        }
    }

    /// <summary>等待队列写完并落盘（崩溃与退出路径使用，最多等待给定时间）。/ Waits for the queue to drain and the file to be written, used on the crash and exit paths with a bounded wait.</summary>
    /// <param name="timeout">最长等待时间。/ Maximum wait.</param>
    public void Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_pending.Count > 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        lock (_ringGate)
        {
            if (_dirty)
            {
                RewriteFile();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(Current, this))
            {
                // 先撤掉静态入口，再关闭队列；退出期间仍在收尾的后台回调会因此直接跳过日志。
                // Remove the static entry point before closing the queue so late shutdown callbacks simply skip logging.
                Current = null;
            }

            // 与 Write 的入队共用一把生命周期锁，因此 TryAdd 不会与 CompleteAdding/Dispose 交叉，也就不会先抛出再由我们吞掉。
            // This shares the lifecycle gate with Write, so TryAdd cannot cross CompleteAdding/Dispose and need not throw before being swallowed.
            _pending.CompleteAdding();
        }

        try
        {
            _writer.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Log] 写入线程未能收尾 / the writer thread did not finish: {ex.Message}");
        }

        lock (_ringGate)
        {
            if (_dirty)
            {
                RewriteFile();
            }
        }

        _pending.Dispose();
    }

    private void Write(AppLogLevel level, string category, string message, Exception? exception)
    {
        // 日志是诊断辅助设施；退出边界到达后，迟到的后台任务写入必须成为无操作，不能反过来把正常退出变成崩溃。
        // Logging is diagnostic infrastructure. Once shutdown reaches this boundary, a late background write must become a no-op instead of
        // turning a normal exit into a crash.
        if (_disposed)
        {
            return;
        }

        var line = Format(level, category, message);
        Mirror(line);
        if (exception is not null)
        {
            // 堆栈逐行加缩进：报告里能一眼看出异常落在哪一层，而不是一整段没有缩进的文本。
            // The stack is indented line by line so a report shows the failing frame at a glance instead of one unindented blob.
            var builder = new StringBuilder(line);
            foreach (var stackLine in exception.ToString().Replace("\r\n", "\n").Split('\n'))
            {
                builder.AppendLine();
                builder.Append("      ").Append(stackLine);
            }

            line = builder.ToString();
            Mirror(line);
        }

        // 错误行立刻落盘：崩溃现场不能等合并窗口。其余行只入队，由写入线程按间隔合并重写。
        // An error line lands on disk immediately, because a crash site must not wait for the coalescing window; other lines are queued and
        // coalesced by the writer thread.
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _pending.TryAdd(new LogEntry(line, ForceRewrite: level == AppLogLevel.Error));
        }
    }

    private static string Format(AppLogLevel level, string category, string message) =>
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Level(level)}] [{category}] {message}";

    private static string Level(AppLogLevel level) => level switch
    {
        AppLogLevel.Verbose => "VRB",
        AppLogLevel.Warning => "WRN",
        AppLogLevel.Error => "ERR",
        _ => "INF"
    };

    private static void Mirror(string line)
    {
#if DEBUG
        Debug.WriteLine(line);
#else
        _ = line;
#endif
    }

    private void WriteLoop()
    {
        while (true)
        {
            LogEntry entry;
            try
            {
                if (_pending.TryTake(out entry!, RewriteInterval))
                {
                    lock (_ringGate)
                    {
                        AppendLine(entry.Line);
                        if (entry.ForceRewrite)
                        {
                            RewriteFile();
                        }
                    }

                    continue;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding 之后没有新行：把剩余内容收尾后退出。 / After CompleteAdding there is nothing new, so finish and leave.
                break;
            }

            lock (_ringGate)
            {
                if (_dirty)
                {
                    RewriteFile();
                }
            }

            if (_pending.IsAddingCompleted && _pending.Count == 0)
            {
                break;
            }
        }

        lock (_ringGate)
        {
            if (_dirty)
            {
                RewriteFile();
            }
        }
    }

    /// <summary>把一行放进内存里的环形窗口；超出上限时丢掉最旧的一行。/ Puts one line into the in-memory ring window, dropping the oldest line past the cap.</summary>
    /// <param name="line">日志行。/ The log line.</param>
    private void AppendLine(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > MaximumLines)
        {
            _lines.Dequeue();
        }

        _dirty = true;
    }

    /// <summary>把整个环形窗口写成文件：先写临时文件再替换，读取方不会看到写了一半的日志。/ Writes the whole ring window: a temporary file is written first and then replaces the log, so a reader never sees a half-written file.</summary>
    private void RewriteFile()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var temp = FilePath + ".tmp";
            File.WriteAllLines(temp, _lines, Encoding.UTF8);
            File.Move(temp, FilePath, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Log] 写日志失败 / writing the log failed: {ex.Message}");
        }
    }

    /// <summary>启动时读回上一次运行的尾部，让它留在新的窗口里：崩溃可能发生在退出路径或上一次运行，那正是最需要的上下文。/ Reads the previous run's tail at startup so it stays in the new window: a crash on the exit path or in the previous run is exactly the context that matters most.</summary>
    private void LoadExistingTail()
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        // 只接续一半窗口：另一半留给本次会话，避免一次很长的会话把这次的现场挤掉。
        // Only half the window is carried over, leaving the other half for this session so one very long run cannot squeeze out the current one.
        var previous = File.ReadAllLines(FilePath);
        var carryOver = Math.Min(previous.Length, MaximumLines / 2);
        foreach (var line in previous[^carryOver..])
        {
            AppendLine(line);
        }

        // 接续进来的内容与原文件一致，因此不需要立刻重写。
        // What was carried over matches the file already, so no rewrite is needed yet.
        _dirty = false;
    }

    /// <summary>一条待写入的日志行及其是否立刻落盘。/ One pending log line and whether it has to land on disk immediately.</summary>
    /// <param name="Line">日志行文本。/ The log line text.</param>
    /// <param name="ForceRewrite">是否立刻重写文件。/ Whether the file has to be rewritten right away.</param>
    private readonly record struct LogEntry(string Line, bool ForceRewrite);
}
