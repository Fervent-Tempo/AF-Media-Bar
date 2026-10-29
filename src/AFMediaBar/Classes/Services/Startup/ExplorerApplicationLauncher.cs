// 每次启动独占一个后台 STA 线程；桌面 Shell 的 COM 引用在该线程内创建和释放，不缓存 Explorer 对象。
using System.IO;
using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Services.Startup;

/// <summary>委托桌面 Explorer 启动外部程序，避免将播放器纳入本应用的子进程树。</summary>
public sealed class ExplorerApplicationLauncher
{
    private static readonly Guid _shellWindowsClass = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    public Task LaunchAsync(string target, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                LaunchOnSta(Path.GetFullPath(target), cancellationToken);
                completion.TrySetResult();
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception exception)
            {
                completion.TrySetException(new InvalidOperationException("无法通过桌面 Explorer 启动应用。", exception));
            }
        }) { IsBackground = true, Name = "AF Explorer launch" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void LaunchOnSta(string target, CancellationToken cancellationToken)
    {
        object? windows = null;
        object? desktop = null;
        object? view = null;
        object? shell = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(_shellWindowsClass, throwOnError: true)!);
            if (windows is null) throw new InvalidOperationException("无法取得 Shell 窗口集合。");
            object location = 0;
            object root = 0;
            // SWC_DESKTOP / SWFO_NEEDDISPATCH：取得实际桌面对象；直接创建 Shell.Application 不能保证由 Explorer 启动。
            desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, out int desktopHandle, 1);
            if (desktop is null) throw new InvalidOperationException("桌面 Explorer 不可用。");
            view = ((dynamic)desktop).Document;
            if (view is null) throw new InvalidOperationException("桌面 Shell 视图不可用。");
            shell = ((dynamic)view).Application;
            if (shell is null) throw new InvalidOperationException("桌面 Shell 启动接口不可用。");
            cancellationToken.ThrowIfCancellationRequested();
            // 快捷方式保留自身的参数、工作目录与默认操作，不拼接命令行。
            ((dynamic)shell).ShellExecute(target, Type.Missing, Type.Missing, Type.Missing, 1);
        }
        finally
        {
            Release(shell);
            Release(view);
            Release(desktop);
            Release(windows);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
