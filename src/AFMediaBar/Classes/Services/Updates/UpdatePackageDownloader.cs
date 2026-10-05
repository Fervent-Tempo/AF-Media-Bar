using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Updates;

/// <summary>
/// 一次下载或校验的结果。
/// Result of one download or verification.
/// </summary>
/// <param name="FilePath">通过校验的安装包路径。/ Path of the installer that passed verification.</param>
/// <param name="Record">写下的待安装记录。/ Pending record that was written.</param>
/// <param name="FailureReason">失败原因；可直接展示。/ Failure reason, ready to display.</param>
/// <param name="Canceled">是否由取消引起。/ Whether the operation was cancelled.</param>
/// <param name="SourceHost">实际使用的来源主机名。/ Host name of the source that was used.</param>
public sealed record UpdateDownloadOutcome(
    string? FilePath,
    UpdatePendingFileRecord? Record,
    string? FailureReason,
    bool Canceled,
    string? SourceHost)
{
    /// <summary>是否成功。/ Whether the operation succeeded.</summary>
    public bool Succeeded => Record is not null;

    /// <summary>构造成功结果。/ Creates a success result.</summary>
    /// <param name="record">已通过校验的记录。/ Record that passed verification.</param>
    /// <param name="host">来源主机名。/ Source host name.</param>
    public static UpdateDownloadOutcome Success(UpdatePendingFileRecord record, string? host) =>
        new(record.Path, record, null, false, host);

    /// <summary>构造失败结果。/ Creates a failure result.</summary>
    /// <param name="reason">失败原因。/ Failure reason.</param>
    /// <param name="host">来源主机名。/ Source host name.</param>
    public static UpdateDownloadOutcome Failure(string reason, string? host) => new(null, null, reason, false, host);

    /// <summary>构造取消结果。/ Creates a cancellation result.</summary>
    public static UpdateDownloadOutcome CanceledOutcome { get; } = new(null, null, null, true, null);
}

/// <summary>
/// 安装包的下载与校验。
///
/// 哈希在下载过程中流式计算，因此"校验"不是额外一遍 70 MB 的读取；只有复核一个早已存在的文件时才会重新读取。
/// 任何哈希不符都会删除文件并且**绝不允许安装**：那是唯一能挡住被代理换包或下载截断的东西。
/// Downloading and verifying installers.
///
/// The hash is computed while streaming, so verification is not a second 70 MB pass; only re-checking a file that
/// already existed reads it again. Any hash mismatch deletes the file and <b>never allows installation</b>: that is
/// the only thing standing between the user and a swapped or truncated download.
/// </summary>
public sealed class UpdatePackageDownloader
{
    private const int CopyBufferSize = 81920;

    /// <summary>
    /// 单次下载的保护上限。它不是对清单的校验，而是防止一个失控或恶意的地址把磁盘写满；
    /// 完整性只由 SHA-256 判定，因此这个上限与清单里是否声明长度无关。
    /// Runaway guard for one download. It is not manifest validation: it only stops a runaway or hostile address
    /// from filling the disk, and because integrity is decided by SHA-256 alone it has nothing to do with whether the
    /// manifest declares a length.
    /// </summary>
    public const long MaximumDownloadBytes = 400L * 1024 * 1024;

    // 上限按整个请求计：70 MB 的安装包在 100 KB/s 的链路上也能完成，而挂死的连接不会拖住整个更新流程。
    // The limit covers the whole request: a 70 MB installer still finishes on a 100 KB/s link, while a hung
    // connection cannot hold the update path forever.
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(15) };

    private readonly UpdatePackageStore _store;

    /// <summary>创建下载器。/ Creates the downloader.</summary>
    /// <param name="store">更新目录的所有者。/ Owner of the update directory.</param>
    public UpdatePackageDownloader(UpdatePackageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 从给定来源下载并校验安装包，成功时写下待安装记录。
    /// Downloads and verifies the installer from the given source, writing the pending record on success.
    /// </summary>
    /// <param name="source">要尝试的来源。/ Source to try.</param>
    /// <param name="asset">清单里的安装包（提供目标长度与哈希）。/ Package from the manifest, providing the expected size and hash.</param>
    /// <param name="version">安装包版本，仅用于日志与记录。/ Installer version, used for the record and the log.</param>
    /// <param name="progress">0–100 的进度回调。/ Progress callback reporting 0–100.</param>
    /// <param name="cancellationToken">取消标记。/ Cancellation token.</param>
    public async Task<UpdateDownloadOutcome> DownloadAsync(
        UpdateDownloadSource source,
        UpdatePackageAsset asset,
        string version,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var target = _store.ResolveInstallerPath(source.Url, version);
        var temporary = target + ".part";
        try
        {
            Directory.CreateDirectory(_store.DirectoryPath);
            _store.RemoveInstaller(temporary);

            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            using var response = await HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateDownloadOutcome.Failure(
                    Translations.Format("Update.Reason.DownloadHttp", (int)response.StatusCode),
                    source.HostName);
            }

            // 不再用服务器声明的 Content-Length 做预检：清单一律只以 SHA-256 判定，长度既不是承诺也不是证据。
            // 长度只在下载过程中充当一个防止失控的保护上限（见 MaximumDownloadBytes），与清单无关。
            // The server's Content-Length is no longer used as a pre-flight: the manifest is judged by SHA-256 alone,
            // and a length is neither a promise nor evidence. It only acts as a runaway guard while streaming (see
            // MaximumDownloadBytes) and has nothing to do with the manifest.
            progress?.Report(0d);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             CopyBufferSize,
                             FileOptions.Asynchronous))
            {
                var buffer = new byte[CopyBufferSize];
                while (true)
                {
                    var read = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    received += read;
                    if (received > MaximumDownloadBytes)
                    {
                        throw new InvalidDataException("下载内容超过保护上限。");
                    }

                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(asset.Size is { } declared && declared > 0
                        ? Math.Min(100d, received * 100d / declared)
                        : 0d);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _store.RemoveInstaller(temporary);
                return UpdateDownloadOutcome.Failure(
                    Translations.Format("Update.Reason.DownloadHashMismatch", received),
                    source.HostName);
            }

            _store.RemoveInstaller(target);
            File.Move(temporary, target, true);
            progress?.Report(100d);
            var record = UpdatePendingFilePolicy.CreateRecord(
                target,
                version,
                asset,
                received,
                new DateTimeOffset(File.GetLastWriteTimeUtc(target), TimeSpan.Zero));
            _store.WritePendingRecord(record);
            return UpdateDownloadOutcome.Success(record, source.HostName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _store.RemoveInstaller(temporary);
            return UpdateDownloadOutcome.CanceledOutcome;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidDataException)
        {
            Debug.WriteLine($"[Update] Download from {source.Url} failed: {exception.Message}");
            _store.RemoveInstaller(temporary);
            return UpdateDownloadOutcome.Failure(
                exception is TaskCanceledException
                    ? Translations.Get("Update.Reason.DownloadTimeout")
                    : Translations.Get("Update.Reason.DownloadFailed"),
                source.HostName);
        }
    }

    /// <summary>
    /// 对已有安装包执行完整 SHA-256 校验；安装准备始终调用此入口，不依赖时间戳判断可信度。
    /// Verifies the complete SHA-256 hash of an existing installer, including every installation preparation.
    /// </summary>
    /// <param name="path">安装包路径。/ Installer path.</param>
    /// <param name="asset">清单里的安装包。/ Package from the manifest.</param>
    /// <param name="version">安装包版本。/ Installer version.</param>
    /// <param name="progress">0–100 的进度回调。/ Progress callback reporting 0–100.</param>
    /// <param name="cancellationToken">取消标记。/ Cancellation token.</param>
    public Task<UpdateDownloadOutcome> VerifyAsync(
        string path,
        UpdatePackageAsset asset,
        string version,
        IProgress<double>? progress,
        CancellationToken cancellationToken) =>
        VerifyAsync(path, asset, version, progress, cancellationToken, persistRecord: true);

    // 安装准备持有额外的只读租约；失败清理须等租约释放，记录只在最终交接时消费。
    internal async Task<UpdateDownloadOutcome> VerifyAsync(
        string path, UpdatePackageAsset asset, string version, IProgress<double>? progress,
        CancellationToken cancellationToken, bool persistRecord)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long read = 0;
            await using (var stream = new FileStream(
                             path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             CopyBufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[CopyBufferSize];
                while (true)
                {
                    var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        break;
                    }

                    read += count;
                    hash.AppendData(buffer, 0, count);
                    progress?.Report(asset.Size is { } declared && declared > 0
                        ? Math.Min(100d, read * 100d / declared)
                        : 0d);
                }
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset());
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                if (persistRecord)
                {
                    _store.RemoveInstaller(path);
                    _store.ClearPendingRecord();
                }
                return UpdateDownloadOutcome.Failure(
                    Translations.Get("Update.Reason.VerifyHashMismatch"),
                    null);
            }

            var record = UpdatePendingFilePolicy.CreateRecord(
                path,
                version,
                asset,
                read,
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
            if (persistRecord)
                _store.WritePendingRecord(record);
            progress?.Report(100d);
            return UpdateDownloadOutcome.Success(record, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UpdateDownloadOutcome.CanceledOutcome;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Update] Re-verification of {path} failed: {exception.Message}");
            if (persistRecord)
            {
                _store.RemoveInstaller(path);
                _store.ClearPendingRecord();
            }
            return UpdateDownloadOutcome.Failure(Translations.Get("Update.Reason.VerifyUnreadable"), null);
        }
    }
}
