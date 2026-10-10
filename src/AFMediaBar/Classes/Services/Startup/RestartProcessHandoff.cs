// 普通重启的命令构造与握手；仅持有本次创建的子进程和一次性事件句柄。
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace AFMediaBar.Classes.Services.Startup;

internal sealed record RestartHandoff(int ParentId, long ParentStartTicks, string EventName);

/// <summary>交接未提交时负责终止等待实例，提交后只释放本地句柄。</summary>
internal sealed class RestartPreparation(Action release, Func<Task> abort, Action? commit = null) : IAsyncDisposable
{
    private bool _committed;
    private int _disposed;
    public void Commit()
    {
        commit?.Invoke();
        _committed = true;
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { if (!_committed) await abort(); }
        finally { release(); }
    }
}

/// <summary>在启动副作用前等待旧进程退出，避免单实例门禁和设置写入竞争。</summary>
internal static class RestartProcessHandoff
{
    internal const string ArgumentPrefix = "--afmb-restart=";
    private const string EventPrefix = @"Local\AFMediaBar.Restart.";
    internal static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(30);

    internal static ProcessStartInfo BuildStartInfo(string executable, string assemblyPath, string workingDirectory,
        IEnumerable<string> arguments, RestartHandoff handoff)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("Restart executable must be absolute.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (!Path.IsPathFullyQualified(assemblyPath)) throw new ArgumentException("Restart assembly must be absolute.");
            start.ArgumentList.Add(assemblyPath);
        }
        foreach (var argument in arguments.Where(value => !value.StartsWith(ArgumentPrefix, StringComparison.Ordinal)))
            start.ArgumentList.Add(argument);
        start.ArgumentList.Add(ArgumentPrefix + handoff.ParentId.ToString(CultureInfo.InvariantCulture) + ":" +
            handoff.ParentStartTicks.ToString(CultureInfo.InvariantCulture) + ":" + handoff.EventName);
        return start;
    }

    internal static bool TryParse(string[] arguments, out RestartHandoff? handoff)
    {
        handoff = null;
        var restartArguments = arguments.Where(value => value.StartsWith(ArgumentPrefix, StringComparison.Ordinal)).ToArray();
        if (restartArguments.Length != 1) return false;
        var parts = restartArguments[0][ArgumentPrefix.Length..].Split(':', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks <= 0 ||
            !parts[2].StartsWith(EventPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(parts[2][EventPrefix.Length..], "N", out _)) return false;
        handoff = new(id, ticks, parts[2]);
        return true;
    }

    internal static Task<RestartPreparation?> PrepareCurrentProcessAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        using var parent = Process.GetCurrentProcess();
        var handoff = new RestartHandoff(parent.Id, parent.StartTime.ToUniversalTime().Ticks, EventPrefix + Guid.NewGuid().ToString("N"));
        var start = BuildStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path."),
            Assembly.GetEntryAssembly()?.Location ?? string.Empty, Environment.CurrentDirectory, Environment.GetCommandLineArgs().Skip(1), handoff);
        return await PrepareAsync(start, handoff.EventName, ReadyTimeout, cancellationToken);
    }, cancellationToken);

    internal static async Task<RestartPreparation?> PrepareAsync(ProcessStartInfo start, string eventName, TimeSpan timeout,
        CancellationToken cancellationToken, Func<ProcessStartInfo, Process?>? launch = null)
    {
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName, out var created);
        if (!created) return null;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        request.CancelAfter(timeout);
        var committed = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".Commit", out var commitCreated);
        if (!commitCreated) { committed.Dispose(); return null; }
        Process? child = null;
        var transferred = false;
        try
        {
            child = await Task.Run(() => (launch ?? Process.Start)(start), request.Token);
            if (child is null) return null;
            await WaitForSignalAsync(ready, request.Token);
            cancellationToken.ThrowIfCancellationRequested();
            var preparation = new RestartPreparation(() => { child.Dispose(); committed.Dispose(); },
                () => StopWaitingChildAsync(child), () => committed.Set());
            transferred = true;
            return preparation;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        finally
        {
            if (!transferred && child is not null)
            {
                await StopWaitingChildAsync(child);
                child.Dispose();
            }
            if (!transferred) committed.Dispose();
        }
    }

    private static Task StopWaitingChildAsync(Process child) => Task.Run(async () =>
    {
        try
        {
            if (!child.HasExited) child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            AppLogService.Current?.Warn("Restart", $"等待实例清理失败: {ex.Message}");
        }
    });

    internal static Task<bool> WaitForParentAsync(RestartHandoff handoff, TimeSpan timeout, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        try
        {
            using var parent = Process.GetProcessById(handoff.ParentId);
            // 先取得进程句柄，之后即使 PID 被复用也只等待这一个进程。
            _ = parent.SafeHandle;
            if (parent.StartTime.ToUniversalTime().Ticks != handoff.ParentStartTicks) return false;
            using var ready = EventWaitHandle.OpenExisting(handoff.EventName);
            using var committed = EventWaitHandle.OpenExisting(handoff.EventName + ".Commit");
            ready.Set();
            await parent.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            return committed.WaitOne(0);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or
                                  WaitHandleCannotBeOpenedException or UnauthorizedAccessException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    });

    private static async Task<bool> WaitForSignalAsync(WaitHandle signal, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registered = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => completion.TrySetResult(true), null, Timeout.Infinite, true);
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try { return await completion.Task; }
        finally { registered.Unregister(null); }
    }
}
