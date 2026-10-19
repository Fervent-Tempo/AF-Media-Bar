// 拥有一次清理的管道、子进程和取消源；辅助进程只有固定白名单操作，不启动应用服务。
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Audio;

namespace AFMediaBar.Classes.Services;

/// <summary>可用内存变化包含其他进程活动，不代表本次释放的精确字节数。</summary>
public sealed record SystemMemoryCleanupResult(SystemMemoryCleanupStatus Status, ulong? Before, ulong? After,
    MemoryCleanupReply? Details = null, ulong? CacheBefore = null, ulong? CacheAfter = null, string? ErrorCode = null);

/// <summary>串行启动固定清理执行端，验证管道客户端和回复身份，释放后拒绝结果。</summary>
public sealed class SystemMemoryCleanupService : IDisposable
{
    private readonly AudioProcessInfoService _processInfo;
    private readonly AppLogService _log;
    private readonly CancellationTokenSource _stopping = new();
    private int _running;
    private bool _disposed;
    private readonly string _owner = CurrentUserSid();
    private readonly string _instance = MemoryCleanupTasks.InstanceId(Environment.ProcessPath!);
    private IReadOnlyDictionary<string, byte[]>? _payloadHashes;
    public MemoryCleanupAuthorization Authorization { get; private set; }
    public event EventHandler? Changed;

    public SystemMemoryCleanupService(AudioProcessInfoService processInfo, AppLogService log)
    {
        _processInfo = processInfo;
        _log = log;
    }

    public async Task<MemoryCleanupAuthorization> RefreshAuthorizationAsync()
    {
        var value = await Task.Run(() =>
        {
            try
            {
                _payloadHashes ??= ReadPayloadHashes();
                return MemoryCleanupTasks.Inspect(_owner, _instance, _payloadHashes);
            }
            catch (Exception) { return MemoryCleanupAuthorization.Unavailable; }
        });
        if (!_disposed) { Authorization = value; Changed?.Invoke(this, EventArgs.Empty); }
        return value;
    }

    public async Task<SystemMemoryCleanupResult> ManageAuthorizationAsync(bool install)
    {
        if (_disposed || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return new(SystemMemoryCleanupStatus.Busy, null, null);
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await ExecuteAsync(SystemMemoryOptimizationLevel.Standard, install ? "--install" : "--revoke");
            if (!_disposed) await RefreshAuthorizationAsync();
            if (install && result.Status == SystemMemoryCleanupStatus.Completed && Authorization != MemoryCleanupAuthorization.Ready)
                result = result with { Status = SystemMemoryCleanupStatus.AuthorizationRequired };
            _log.Info("Memory", $"系统优化授权操作：install={install}, result={result.Status}, authorization={Authorization}");
            return result;
        }
        finally { Interlocked.Exchange(ref _running, 0); if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
    }
    public SystemMemoryCleanupResult? LastResult { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public long? LastAttemptTimestamp { get; private set; }
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public static bool IsCleanupRequest(string[] args) => args.Any(arg => arg.StartsWith("--internal-memory-clean", StringComparison.Ordinal));

    public async Task<SystemMemoryCleanupResult> RunAsync(SystemMemoryOptimizationLevel level = SystemMemoryOptimizationLevel.Standard, bool requireAuthorization = false)
    {
        if (_disposed) return new(SystemMemoryCleanupStatus.Cancelled, null, null);
        if (!Enum.IsDefined(level)) return new(SystemMemoryCleanupStatus.InvalidRequest, null, null);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return new(SystemMemoryCleanupStatus.Busy, null, null);
        LastAttemptAt = DateTimeOffset.UtcNow;
        LastAttemptTimestamp = Stopwatch.GetTimestamp();
        Changed?.Invoke(this, EventArgs.Empty);
        SystemMemoryCleanupResult result;
        try
        {
            await RefreshAuthorizationAsync();
            result = requireAuthorization && Authorization != MemoryCleanupAuthorization.Ready
                ? new(SystemMemoryCleanupStatus.AuthorizationRequired, null, null) : await ExecuteAsync(level, null);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
        if (!_disposed) { LastResult = result; Changed?.Invoke(this, EventArgs.Empty); }
        return result;
    }

    private async Task<SystemMemoryCleanupResult> ExecuteAsync(SystemMemoryOptimizationLevel level, string? management)
    {
        ulong? before = null;
        string? directory = null;
        Process? process = null;
        var extractionGuards = new List<FileStream>();
        try
        {
            before = await Task.Run(MemoryToolsService.CaptureAvailableBytes);
            var nonce = Guid.NewGuid().ToString("N");
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(_owner), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            using var pipe = NamedPipeServerStreamAcl.Create("AFMB.Memory." + nonce, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
            string helper;
            if (management is null && Authorization == MemoryCleanupAuthorization.Ready)
            {
                helper = MemoryCleanupTasks.HelperPath(_owner, _instance);
                await Task.Run(() => MemoryCleanupTasks.Run(_owner, _instance, level, nonce));
            }
            else
            {
                directory = Path.Combine(Path.GetTempPath(), "AFMB.Memory." + nonce);
                helper = await Task.Run(() =>
                {
                    var path = ExtractHelper(directory);
                    _payloadHashes ??= ReadPayloadHashes();
                    foreach (var entry in _payloadHashes)
                    {
                        var guard = new FileStream(Path.Combine(directory, entry.Key), FileMode.Open, FileAccess.Read, FileShare.Read);
                        extractionGuards.Add(guard);
                        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(guard), entry.Value)) throw new InvalidDataException();
                    }
                    return path;
                });
                var arguments = management is null
                    ? "--run " + (level == SystemMemoryOptimizationLevel.Deep ? "deep" : "standard") + " " + nonce
                    : management + " " + _owner + " " + _instance + " " + nonce;
                process = await Task.Run(() => Process.Start(new ProcessStartInfo(helper)
                { Arguments = arguments, UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }));
                if (process is null) throw new InvalidOperationException();
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(65));
            var connection = pipe.WaitForConnectionAsync(timeout.Token);
            if (process is not null)
            {
                var exited = process.WaitForExitAsync(timeout.Token);
                if (await Task.WhenAny(connection, exited) == exited && !connection.IsCompleted)
                {
                    await exited;
                    timeout.Cancel();
                    try { await connection; } catch (OperationCanceledException) { }
                    return new(process.ExitCode != 0 && Enum.IsDefined(typeof(SystemMemoryCleanupStatus), process.ExitCode)
                        ? (SystemMemoryCleanupStatus)process.ExitCode : SystemMemoryCleanupStatus.Failed, before, null);
                }
            }
            await connection;
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientId)) throw new InvalidDataException();
            if (process is not null && clientId != process.Id) throw new InvalidDataException();
            var validImage = await Task.Run(() =>
            {
                // 辅助进程已经提权，只查询映像路径，避免 MainModule 额外要求模块读取权限。
                var clientPath = _processInfo.GetExecutablePath((uint)clientId);
                return string.Equals(clientPath, helper, StringComparison.OrdinalIgnoreCase);
            });
            if (!validImage) throw new InvalidDataException();
            var buffer = new byte[16384];
            var length = 0;
            while (length < buffer.Length)
            {
                var count = await pipe.ReadAsync(buffer.AsMemory(length), timeout.Token);
                if (count == 0) break;
                length += count;
                if (buffer[length - 1] == (byte)'\n') break;
            }
            if (length == buffer.Length) throw new InvalidDataException();
            var reply = JsonSerializer.Deserialize<MemoryCleanupReply>(Encoding.UTF8.GetString(buffer, 0, length));
            var expected = management is not null ? new[] { "Authorization" } : level == SystemMemoryOptimizationLevel.Deep ? new[] { "FileCache", "Standby" } : new[] { "LowStandby" };
            if (reply is null || reply.Version != MemoryCleanupReply.ProtocolVersion || reply.RequestId != nonce || reply.Level != level ||
                !Enum.IsDefined(reply.Status) || reply.Stages is null || !reply.Stages.Select(stage => stage.Operation).SequenceEqual(expected) ||
                reply.Stages.Any(stage => !Enum.IsDefined(stage.Status) || stage.ErrorCode?.Length > 128)) throw new InvalidDataException();
            if (process is not null) await process.WaitForExitAsync(timeout.Token);
            var after = await Task.Run(MemoryToolsService.CaptureAvailableBytes);
            _log.Info("Memory", $"系统优化执行：level={level}, operation={management ?? "cleanup"}, status={reply.Status}, stages={string.Join(",", reply.Stages.Select(stage => stage.Operation + ":" + stage.Status + ":" + stage.ErrorCode))}");
            return new(reply.Status, reply.Before?.AvailableBytes ?? before, reply.After?.AvailableBytes ?? after, reply,
                reply.Before?.CacheBytes, reply.After?.CacheBytes);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { return new(SystemMemoryCleanupStatus.Cancelled, before, null); }
        catch (Exception error) when (error is UnauthorizedAccessException || error is Win32Exception { NativeErrorCode: 5 }) { return new(SystemMemoryCleanupStatus.PermissionDenied, before, null); }
        catch (OperationCanceledException) { return new(_stopping.IsCancellationRequested ? SystemMemoryCleanupStatus.Cancelled : SystemMemoryCleanupStatus.TimedOut, before, null); }
        catch (Exception error)
        {
            _log.Warn("Memory", $"系统优化执行端失败：level={level}, operation={management ?? "cleanup"}", error);
            return new(SystemMemoryCleanupStatus.Failed, before, null, ErrorCode: $"{error.GetType().Name} 0x{error.HResult:X8}");
        }
        finally
        {
            process?.Dispose();
            foreach (var guard in extractionGuards) guard.Dispose();
            if (directory is not null) TryDeleteExtraction(directory);
        }
    }

    private static string CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }
    private static IReadOnlyDictionary<string, byte[]> ReadPayloadHashes()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AFMediaBar.MemoryHelper.zip") ?? throw new InvalidDataException();
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        var hashes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!MemoryCleanupTasks.ValidPayloadName(entry.FullName) || !hashes.TryAdd(entry.FullName, [])) throw new InvalidDataException();
            using var input = entry.Open();
            hashes[entry.FullName] = SHA256.HashData(input);
        }
        if (!hashes.ContainsKey(MemoryCleanupTasks.PayloadManifest) || !hashes.ContainsKey("AFMediaBar.MemoryHelper.exe")) throw new InvalidDataException();
        return hashes;
    }
    internal static string ExtractHelper(string directory)
    {
        Directory.CreateDirectory(directory);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AFMediaBar.MemoryHelper.zip") ?? throw new InvalidDataException();
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (!MemoryCleanupTasks.ValidPayloadName(entry.FullName)) throw new InvalidDataException();
            entry.ExtractToFile(Path.Combine(directory, entry.FullName), overwrite: false);
        }
        return Path.Combine(directory, "AFMediaBar.MemoryHelper.exe");
    }
    internal static void TryDeleteExtraction(string directory)
    {
        // 只删除编译进程序的平铺文件名，不递归处理任何调用方路径。
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AFMediaBar.MemoryHelper.zip");
            if (resource is null) return;
            using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
                if (MemoryCleanupTasks.ValidPayloadName(entry.FullName)) File.Delete(Path.Combine(directory, entry.FullName));
            Directory.Delete(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _stopping.Cancel(); }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out int clientId);
}
