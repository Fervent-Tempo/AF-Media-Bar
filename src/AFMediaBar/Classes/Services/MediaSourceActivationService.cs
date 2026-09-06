using System.Diagnostics;
using System.IO;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 将媒体来源标识解析为可激活的窗口、应用或可执行文件。
/// Resolves a media source identifier to an activatable window, app, or executable.
/// </summary>
public sealed class MediaSourceActivationService
{
    private static readonly (string[] Tokens, string[] ProcessNames)[] SourceProcesses =
    [
        (["cloudmusic", "netease", "163music"], ["cloudmusic"]),
        (["qqmusic"], ["QQMusic"]),
        (["kugou", "kgmusic"], ["KuGou", "KuGouMusic"]),
        (["spotify"], ["Spotify"]),
        (["chrome"], ["chrome"]),
        (["msedge", "microsoftedge"], ["msedge"]),
        (["firefox"], ["firefox"]),
        (["vlc"], ["vlc"]),
        (["potplayer", "daum"], ["PotPlayerMini64", "PotPlayerMini"]),
        (["zunemusic", "media.player", "wmplayer"], ["Microsoft.Media.Player", "Music.UI", "wmplayer"]),
        (["mpv"], ["mpv"]),
        (["foobar"], ["foobar2000"])
    ];

    public void Activate(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processName in ResolveProcessNames(sourceId))
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

    private static IEnumerable<string> ResolveProcessNames(string sourceId)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in SourceProcesses)
        {
            if (mapping.Tokens.Any(token => sourceId.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var processName in mapping.ProcessNames)
                {
                    names.Add(processName);
                }
            }
        }

        if (sourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            names.Add(Path.GetFileNameWithoutExtension(sourceId));
        }

        return names;
    }
}
