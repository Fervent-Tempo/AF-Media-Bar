using System.Diagnostics;
using System.IO;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 将媒体来源标识解析为可激活的窗口、应用或可执行文件。
/// Resolves a media source identifier to an activatable window, app, or executable.
/// </summary>
public sealed class MediaSourceActivationService
{
    private readonly MediaSourceProcessResolver _processResolver;

    /// <summary>
    /// 创建来源激活器，并复用音频来源解析规则定位候选进程。
    /// Creates the source activator and reuses audio-source resolution rules to locate candidate processes.
    /// </summary>
    public MediaSourceActivationService(MediaSourceProcessResolver processResolver)
    {
        _processResolver = processResolver;
    }

    /// <summary>
    /// 在后台解析来源窗口并请求前置；没有有效窗口时沿用应用或可执行文件唤醒入口。
    /// </summary>
    public Task ActivateAsync(string sourceId) => Task.Run(() => ActivateCoreAsync(sourceId));

    private async Task ActivateCoreAsync(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var result = await TryActivateRunningAsync(sourceId, executablePaths).ConfigureAwait(false);
            if (result != WindowActivationResult.NotFound) return;

            if (sourceId.Contains('!'))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{sourceId}",
                    UseShellExecute = true
                });
                return;
            }

            var executablePath = executablePaths.FirstOrDefault(File.Exists);
            if (executablePath is not null)
            {
                Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[MediaSourceActivationService] Activate source failed: {ex}");
        }
    }

    /// <summary>解析当前已发现来源的安全启动描述。 / Resolves a safe launch descriptor for a currently discovered source.</summary>
    public MediaSourceDescriptor Describe(string sourceId)
    {
        var displayName = MediaSourceNameFormatter.GetDisplayName(sourceId, Translations.Get("Service.MediaSource.Unknown"));
        if (string.IsNullOrWhiteSpace(sourceId))
            return new MediaSourceDescriptor(string.Empty, displayName, null, null);
        if (sourceId.Contains('!'))
            return new MediaSourceDescriptor(sourceId, displayName, QuickLaunchTargetKind.AppUserModelId, sourceId);

        foreach (var processName in ResolveActivationProcessNames(sourceId))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            return new MediaSourceDescriptor(sourceId, displayName, QuickLaunchTargetKind.Executable, Path.GetFullPath(path));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        Debug.WriteLine($"[MediaSourceActivationService] Could not resolve launch path: {ex.Message}");
                    }
                }
            }
        }

        return new MediaSourceDescriptor(sourceId, displayName, null, null);
    }

    /// <summary>激活或启动一个经过设置归一化的快速启动项。 / Activates or starts a normalized quick-launch entry.</summary>
    public Task<QuickLaunchResult> LaunchAsync(QuickLaunchEntry entry, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LaunchCoreAsync(entry);
        }, cancellationToken);

    private async Task<QuickLaunchResult> LaunchCoreAsync(QuickLaunchEntry entry)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(entry.SourceId))
            {
                var result = await TryActivateRunningAsync(entry.SourceId).ConfigureAwait(false);
                if (result == WindowActivationResult.Activated) return QuickLaunchResult.Success;
                // 已找到界面但系统拒绝前置时，不另起一个应用实例。
                if (result == WindowActivationResult.Failed) return QuickLaunchResult.Failed;
            }
            var target = entry.Target.Trim();
            switch (entry.Kind)
            {
                case QuickLaunchTargetKind.AppUserModelId when target.Contains('!'):
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"shell:AppsFolder\\{target}",
                        UseShellExecute = true
                    });
                    return QuickLaunchResult.Success;
                case QuickLaunchTargetKind.Executable
                    when File.Exists(target) && string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase):
                case QuickLaunchTargetKind.Shortcut
                    when File.Exists(target) && string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase):
                    Process.Start(new ProcessStartInfo(Path.GetFullPath(target)) { UseShellExecute = true });
                    return QuickLaunchResult.Success;
                default:
                    return QuickLaunchResult.InvalidTarget;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[MediaSourceActivationService] Quick launch failed: {ex}");
            return QuickLaunchResult.Failed;
        }
    }

    private enum WindowActivationResult { NotFound, Activated, Failed }

    private async Task<WindowActivationResult> TryActivateRunningAsync(
        string sourceId, HashSet<string>? executablePaths = null)
    {
        foreach (var processName in ResolveActivationProcessNames(sourceId))
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (executablePaths is not null)
                        {
                            try
                            {
                                if (process.MainModule?.FileName is { Length: > 0 } path)
                                    executablePaths.Add(path);
                            }
                            catch (System.ComponentModel.Win32Exception) { }
                        }

                        // MainWindowHandle 可能指向通信窗口；枚举并验证实际界面，也保留隐藏到托盘的主窗口。
                        foreach (var handle in FindActivationWindows(process.Id))
                        {
                            if (await RestoreWindowAsync(handle, process.Id).ConfigureAwait(false))
                                return WindowActivationResult.Activated;
                            // 不因焦点限制依次展开同一播放器的其他窗口。
                            Debug.WriteLine($"[MediaSourceActivationService] Window activation refused: {sourceId}");
                            return WindowActivationResult.Failed;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        Debug.WriteLine($"[MediaSourceActivationService] Process exited while activating: {ex.Message}");
                    }
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        return WindowActivationResult.NotFound;
    }

    internal static IReadOnlyList<IntPtr> FindActivationWindows(int processId)
    {
        var windows = new List<IntPtr>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (IsActivationWindow(handle, processId)) windows.Add(handle);
            return true;
        }, IntPtr.Zero);
        return windows.OrderByDescending(NativeMethods.IsWindowVisible).ToArray();
    }

    private static bool IsActivationWindow(IntPtr handle, int processId)
    {
        const int ToolWindow = 0x00000080;
        const int AppWindow = 0x00040000;
        const int Caption = 0x00C00000;
        const uint Owner = 4;
        NativeMethods.GetWindowThreadProcessId(handle, out var ownerProcess);
        if (ownerProcess != processId || NativeMethods.GetWindow(handle, Owner) != IntPtr.Zero) return false;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_STYLE);
        var extendedStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        if ((style & NativeMethods.WS_CHILD) != 0 ||
            (extendedStyle & NativeMethods.WS_EX_NOACTIVATE) != 0 ||
            ((extendedStyle & ToolWindow) != 0 && (extendedStyle & AppWindow) == 0)) return false;
        if (!NativeMethods.GetWindowRect(handle, out var bounds) ||
            bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return false;
        return NativeMethods.IsWindowVisible(handle) || (extendedStyle & AppWindow) != 0 || (style & Caption) != 0;
    }

    private static async Task<bool> RestoreWindowAsync(IntPtr handle, int processId)
    {
        if (!IsActivationWindow(handle, processId) ||
            !NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_RESTORE)) return false;
        // 异步恢复避免等待目标 UI 线程；窗口显示后只请求一次前置，不绕过系统焦点限制。
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!IsActivationWindow(handle, processId)) return false;
            if (NativeMethods.IsWindowVisible(handle) && !NativeMethods.IsIconic(handle))
                return NativeMethods.SetForegroundWindow(handle) || NativeMethods.GetForegroundWindow() == handle;
            await Task.Delay(50).ConfigureAwait(false);
        }
        return false;
    }

    private IEnumerable<string> ResolveActivationProcessNames(string sourceId)
    {
        var names = _processResolver.ResolveProcessNames(sourceId);
        // 桌面应用也可用不带 .exe 的进程名注册 SMTC；只补充精确名称，不猜测相似应用。
        if (!string.IsNullOrWhiteSpace(sourceId) && !sourceId.Contains('!') &&
            sourceId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            !sourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return names.Append(sourceId).Distinct(StringComparer.OrdinalIgnoreCase);
        return names;
    }

}
