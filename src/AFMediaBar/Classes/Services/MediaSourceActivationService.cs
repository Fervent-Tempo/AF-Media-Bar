using System.Diagnostics;
using System.IO;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services.Audio;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 将媒体来源标识解析为可激活的窗口、应用或可执行文件。
/// Resolves a media source identifier to an activatable window, app, or executable.
/// </summary>
public sealed class MediaSourceActivationService
{
    private readonly MediaSourceProcessResolver _processResolver;

    /// <summary>
    /// 调用 MediaSourceActivationService，提供 API。
    /// Provides the public MediaSourceActivationService entry point required by this component.
    /// </summary>
    public MediaSourceActivationService(MediaSourceProcessResolver processResolver)
    {
        _processResolver = processResolver;
    }

    /// <summary>
    /// 调用 Activate，提供 API。
    /// Provides the public Activate entry point required by this component.
    /// </summary>
    public void Activate(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processName in _processResolver.ResolveProcessNames(sourceId))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                        {
                            var executablePath = process.MainModule?.FileName;
                            if (!string.IsNullOrWhiteSpace(executablePath))
                            {
                                executablePaths.Add(executablePath);
                            }

                            continue;
                        }

                        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(handle);
                        return;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or
                                                   System.ComponentModel.Win32Exception or
                                                   NotSupportedException)
                    {
                        Debug.WriteLine($"[MediaSourceActivationService] Process exited while activating: {ex.Message}");
                    }
                }
            }
        }

        try
        {
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

}
