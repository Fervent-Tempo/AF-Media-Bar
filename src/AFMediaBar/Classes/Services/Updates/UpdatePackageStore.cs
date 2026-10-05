using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AFMediaBar.Classes.Models.Updates;

namespace AFMediaBar.Classes.Services.Updates;

/// <summary>
/// 更新目录（<c>%LOCALAPPDATA%\AFMediaBar\updates</c>）的文件所有权。
///
/// 它拥有三样东西：已下载的安装包、记录其身份的 <c>pending.json</c>、以及安装日志。把"文件在哪、还算不算数"
/// 集中在这里，下载服务与更新协调器就都不必自己拼路径，也不会出现两边对同一个文件给出不同判断。
/// File ownership for the update directory (<c>%LOCALAPPDATA%\AFMediaBar\updates</c>).
///
/// It owns three things: the downloaded installer, the <c>pending.json</c> that records its identity, and the
/// install log. Keeping "where the file is and whether it still counts" in one place means neither the downloader
/// nor the coordinator has to build paths, and the two can never disagree about the same file.
/// </summary>
public sealed class UpdatePackageStore
{
    /// <summary>记录已下载安装包身份的文件名。/ Name of the file recording the downloaded installer's identity.</summary>
    public const string PendingRecordFileName = "pending.json";

    /// <summary>安装日志的保留天数。/ Retention in days for install logs.</summary>
    public const int LogRetentionDays = 30;

    private const string PartSuffix = ".part";
    private const string InstallerPattern = "AFMediaBar-Setup-*.exe";
    private const string LogPattern = "install-*.log";
    private const string TemporarySuffix = ".tmp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _directoryPath;

    /// <summary>
    /// 创建更新目录的所有者；目录可以覆盖，因此单元测试不必触碰用户真实目录。
    /// Creates the owner of the update directory; the directory can be overridden so unit tests never touch the
    /// user's real one.
    /// </summary>
    /// <param name="directoryPath">更新目录；为 null 时使用 <c>%LOCALAPPDATA%\AFMediaBar\updates</c>。/ Update directory, or <c>%LOCALAPPDATA%\AFMediaBar\updates</c> when null.</param>
    public UpdatePackageStore(string? directoryPath = null)
    {
        _directoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AFMediaBar",
            "updates");
    }

    /// <summary>更新目录的完整路径。/ Full path of the update directory.</summary>
    public string DirectoryPath => _directoryPath;

    /// <summary>待安装记录的完整路径。/ Full path of the pending record.</summary>
    public string PendingRecordPath => Path.Combine(_directoryPath, PendingRecordFileName);

    /// <summary>
    /// 解析安装包的落地路径。文件名优先取直链自身的文件名，因此发布侧换名字不需要改代码；直链文件名不可用时
    /// 才退回按版本号生成的名称。
    /// Resolves where an installer lands. The file name comes from the direct link itself, so renaming an asset
    /// needs no code change, and only an unusable name falls back to one built from the version.
    /// </summary>
    /// <param name="sourceUrl">安装包直链。/ Installer direct link.</param>
    /// <param name="version">安装包版本，用于回退命名。/ Version used for the fallback name.</param>
    public string ResolveInstallerPath(string sourceUrl, string version)
    {
        var name = string.Empty;
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            name = Path.GetFileName(uri.LocalPath);
        }

        name = SanitizeFileName(name);
        if (name.Length == 0 || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = $"AFMediaBar-Setup-v{version}-win-x64.exe";
        }

        return Path.Combine(_directoryPath, name);
    }

    /// <summary>解析某版本的安装日志路径，供静默安装的 <c>/LOG</c> 使用。/ Resolves the install log path for a version, used by the installer's <c>/LOG</c>.</summary>
    /// <param name="version">版本号。/ Version.</param>
    public string ResolveLogPath(string version) => Path.Combine(_directoryPath, $"install-{SanitizeFileName(version)}.log");

    /// <summary>读取待安装记录；不存在或已损坏时返回 null。/ Reads the pending record, returning null when it is missing or damaged.</summary>
    public UpdatePendingFileRecord? ReadPendingRecord()
    {
        try
        {
            if (!File.Exists(PendingRecordPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<UpdatePendingFileRecord>(File.ReadAllText(PendingRecordPath), JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Update] Pending record could not be read: {exception.Message}");
            return null;
        }
    }

    /// <summary>写入待安装记录。/ Writes the pending record.</summary>
    /// <param name="record">已通过校验的安装包记录。/ Record of an installer that passed verification.</param>
    public void WritePendingRecord(UpdatePendingFileRecord record)
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            var payload = JsonSerializer.Serialize(record, JsonOptions);

            // 先写临时文件再整体替换：直接覆盖会在写入被中断时留下半截 JSON，下次启动读不出来，更新也就丢了。
            // Write to a temporary file first and replace in one step: overwriting in place can leave half-written
            // JSON behind when the write is interrupted, which the next start cannot read, losing the update.
            var temporaryPath = PendingRecordPath + TemporarySuffix;
            File.WriteAllText(temporaryPath, payload);
            File.Move(temporaryPath, PendingRecordPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 记录写不进去不影响本次更新：文件本身已经通过校验，只是下次启动会重新校验一遍。
            // A failed write does not break this update: the file itself passed verification and will simply be
            // verified again on the next start.
            Debug.WriteLine($"[Update] Pending record could not be written: {exception.Message}");
        }
    }

    /// <summary>删除待安装记录。/ Deletes the pending record.</summary>
    public void ClearPendingRecord()
    {
        try
        {
            if (File.Exists(PendingRecordPath))
            {
                File.Delete(PendingRecordPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Update] Pending record could not be deleted: {exception.Message}");
        }
    }

    /// <summary>
    /// 判断清单里的安装包在本机是否已有可用副本。
    /// Decides whether a usable local copy of the manifest's installer already exists.
    /// </summary>
    /// <param name="asset">清单里的安装包。/ Package from the manifest.</param>
    public UpdatePendingFileAction Evaluate(UpdatePackageAsset asset)
    {
        var record = ReadPendingRecord();
        if (record is null)
        {
            return UpdatePendingFileAction.Download;
        }

        // 路径不可信时一律丢弃：记录是用户可写的，其中的路径可能被指向更新目录之外的任意位置。
        // An untrusted path is discarded outright: the record is user-writable, so the path inside it may have been
        // pointed anywhere outside this directory.
        if (!IsTrustedInstallerPath(record.Path))
        {
            Debug.WriteLine("[Update] Pending installer path is outside the update directory; discarding the record.");
            return UpdatePendingFileAction.Discard;
        }

        var exists = false;
        long size = 0;
        var modified = DateTimeOffset.MinValue;
        try
        {
            var info = new FileInfo(record.Path);
            exists = info.Exists;
            if (exists)
            {
                size = info.Length;
                modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Debug.WriteLine($"[Update] Pending installer could not be inspected: {exception.Message}");
        }

        return UpdatePendingFilePolicy.Decide(record, asset, exists, size, modified);
    }

    /// <summary>删除一个安装包文件，不抛出。/ Deletes one installer file without throwing.</summary>
    /// <param name="path">要删除的路径。/ Path to delete.</param>
    public void RemoveInstaller(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 文件可能正被安装程序使用；留下它比让启动路径抛异常好。
            // The file may be in use by the running installer; leaving it beats throwing on a startup path.
            Debug.WriteLine($"[Update] Installer could not be deleted ({path}): {exception.Message}");
        }
    }

    /// <summary>
    /// 清理残留：除待安装的那一个以外的安装包、所有未完成的 <c>.part</c> 文件、以及超过保留期的日志。
    ///
    /// 只删除已知模式的文件，绝不递归删除目录：这个目录与用户的设置文件同级，误删的代价无法接受。
    /// Cleans up leftovers: every installer except the pending one, all unfinished <c>.part</c> files, and logs past
    /// their retention window.
    ///
    /// Only known file patterns are deleted and the directory is never removed recursively: it sits next to the
    /// user's settings file, where an over-eager delete would be unacceptable.
    /// </summary>
    /// <param name="keepPath">必须保留的安装包路径（待安装的那一个）；没有时传 null。/ Installer path to keep (the pending one), or null.</param>
    public void CleanupObsolete(string? keepPath)
    {
        try
        {
            if (!Directory.Exists(_directoryPath))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_directoryPath, InstallerPattern))
            {
                if (keepPath is not null && string.Equals(file, keepPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                RemoveInstaller(file);
            }

            foreach (var part in Directory.EnumerateFiles(_directoryPath, "*" + PartSuffix))
            {
                RemoveInstaller(part);
            }

            var cutoff = DateTime.UtcNow.AddDays(-LogRetentionDays);
            foreach (var log in Directory.EnumerateFiles(_directoryPath, LogPattern))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(log) < cutoff)
                    {
                        File.Delete(log);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[Update] Install log could not be deleted ({log}): {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Update] Update directory cleanup failed: {exception.Message}");
        }
    }

    /// <summary>
    /// 判断一个安装包路径是否可以信任：它必须位于本更新目录之内，并且是一个 <c>.exe</c>。
    ///
    /// <c>pending.json</c> 落在用户自己的目录里，因此记录中的路径只能当作"指向本目录下某个文件"的提示，
    /// 绝不能当作可以照此执行的地址：少了这层约束，改写这一个文件就足以让下一次启动去运行别处的任意文件。
    /// Decides whether an installer path may be trusted: it has to sit inside this update directory and be an
    /// <c>.exe</c>.
    ///
    /// <c>pending.json</c> lives in the user's own directory, so a path read from the record is only a hint pointing
    /// at a file inside this directory, never an address that may be executed as-is: without this constraint,
    /// rewriting that single file would be enough to make the next start run an arbitrary file from elsewhere.
    /// </summary>
    /// <param name="path">待判断的路径。/ Path to judge.</param>
    public bool IsTrustedInstallerPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return false;
        }

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_directoryPath));
            var candidate = Path.GetFullPath(path);

            return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }
}
