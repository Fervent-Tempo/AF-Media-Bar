// 应用音量属于运行记录，独立于设置与默认快照；调用者在后台线程加载和原子保存。
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>保存 AF 成功调整过的应用音量；文件不可读时尝试上一份完整记录。</summary>
public sealed class ApplicationVolumeMemoryStore
{
    private readonly string _path;
    private bool _loadedFromBackup;

    public ApplicationVolumeMemoryStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMediaBar"),
            "application-volumes.json");
    }

    public Dictionary<string, int> Load()
    {
        foreach (var path in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
                if (values is not null)
                {
                    _loadedFromBackup = path != _path;
                    return Normalize(values);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { Debug.WriteLine($"[VolumeMemory] Read: {error.Message}"); }
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    public void Save(IReadOnlyDictionary<string, int> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(file, Normalize(values));
                file.Flush(flushToDisk: true);
            }
            if (_loadedFromBackup) File.Move(temporary, _path, overwrite: true);
            else if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, _path);
            _loadedFromBackup = false;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = path.Trim();
            return Path.IsPathFullyQualified(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(path) : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    private static Dictionary<string, int> Normalize(IEnumerable<KeyValuePair<string, int>> values)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
            if (NormalizePath(pair.Key) is { } path) result[path] = Math.Clamp(pair.Value, 0, 100);
        return result;
    }
}
