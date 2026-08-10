using System.Diagnostics;
using System.IO;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

/// <summary>
/// 激活当前媒体来源的窗口，必要时尝试启动对应应用。
/// Activates the current media source window, or launches the matching app when needed.
/// </summary>
internal static class MediaSourceLauncherService
{
    internal static bool ShowOrLaunch(string sourceId, string sourceName)
    {
        var candidates = GetProcessNames(sourceId, sourceName).ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == nint.Zero ||
                        !candidates.Contains(process.ProcessName))
                    {
                        continue;
                    }

                    NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SwRestore);
                    NativeMethods.SetForegroundWindow(process.MainWindowHandle);
                    return true;
                }
                catch
                {
                    // 枚举后进程可能立即退出；单个进程失效不应中断后续查找。
                    // A process can exit after enumeration; one stale entry must not stop the search.
                }
            }
        }

        return TryLaunch(sourceId, sourceName);
    }

    private static IEnumerable<string> GetProcessNames(string sourceId, string sourceName)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourceId);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            yield return fileName;
        }

        var mappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["网易云音乐"] = ["cloudmusic"],
            ["QQ音乐"] = ["qqmusic"],
            ["酷狗音乐"] = ["kugou", "KuGou"],
            ["Spotify"] = ["spotify"],
            ["Google Chrome"] = ["chrome"],
            ["Microsoft Edge"] = ["msedge"],
            ["Firefox"] = ["firefox"],
            ["VLC"] = ["vlc"],
            ["PotPlayer"] = ["PotPlayerMini64", "PotPlayerMini"],
            ["Windows Media Player"] = ["Microsoft.Media.Player", "wmplayer"],
            ["mpv"] = ["mpv"],
            ["foobar2000"] = ["foobar2000"]
        };

        if (mappings.TryGetValue(sourceName, out var names))
        {
            foreach (var name in names)
            {
                yield return name;
            }
        }
    }

    private static bool TryLaunch(string sourceId, string sourceName)
    {
        try
        {
            if (sourceName == "网易云音乐")
            {
                var knownPaths = new[]
                {
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "NetEase",
                        "CloudMusic",
                        "cloudmusic.exe"),
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "NetEase",
                        "CloudMusic",
                        "cloudmusic.exe")
                };
                var executable = knownPaths.FirstOrDefault(File.Exists);
                if (executable is not null)
                {
                    Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
                    return true;
                }
            }

            if (sourceId.Contains('!'))
            {
                Process.Start(new ProcessStartInfo($"shell:AppsFolder\\{sourceId}")
                {
                    UseShellExecute = true
                });
                return true;
            }
        }
        catch
        {
            // 打开来源只是便捷操作；失败时保留已有媒体控制功能。
            // Opening the source is optional; existing media controls remain usable on failure.
        }

        return false;
    }
}
