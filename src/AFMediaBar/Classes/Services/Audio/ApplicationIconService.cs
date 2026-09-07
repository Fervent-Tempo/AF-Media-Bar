using System.Diagnostics;
using System.IO;
using DrawingIcon = System.Drawing.Icon;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 从音频会话或进程可执行文件读取应用图标，并按来源路径缓存原始图像字节。
/// Reads application icons from audio sessions or process executables and caches raw image bytes by source path.
/// </summary>
public sealed class ApplicationIconService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>按会话图标路径和进程路径依次读取图标。 / Reads an icon from the session path and then the process path.</summary>
    public byte[]? GetIconData(uint processId, string? sessionIconPath)
    {
        var candidates = new[] { NormalizeIconPath(sessionIconPath), GetProcessPath(processId) }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            lock (_gate)
            {
                if (_cache.TryGetValue(candidate!, out var cached))
                {
                    return cached;
                }
            }

            var icon = ReadIcon(candidate!);
            if (icon is null)
            {
                continue;
            }

            lock (_gate)
            {
                _cache[candidate!] = icon;
            }
            return icon;
        }

        return null;
    }

    private static byte[]? ReadIcon(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var extension = Path.GetExtension(path);
            if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return File.ReadAllBytes(path);
            }

            using var icon = DrawingIcon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            icon.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetProcessPath(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeIconPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = Environment.ExpandEnvironmentVariables(value.Trim().TrimStart('@').Trim('"'));
        var resourceSeparator = path.LastIndexOf(',');
        if (resourceSeparator > 0 && int.TryParse(path[(resourceSeparator + 1)..], out _))
        {
            path = path[..resourceSeparator];
        }

        return path.Trim('"');
    }
}
