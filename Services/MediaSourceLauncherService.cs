using System.Diagnostics;
using System.IO;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

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
                    // A process can exit while its window is being inspected.
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

            if (sourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(sourceId) { UseShellExecute = true });
                return true;
            }
        }
        catch
        {
            // Showing the source is a convenience action; media control remains available.
        }

        return false;
    }
}
