// 拥有一次清理的管道、子进程和取消源；辅助进程只有固定白名单操作，不启动应用服务。
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace AFMediaBar.Classes.Services;

/// <summary>可用内存变化包含其他进程活动，不代表本次释放的精确字节数。</summary>
public sealed record SystemMemoryCleanupResult(SystemMemoryCleanupStatus Status, ulong? Before, ulong? After,
    MemoryCleanupReply? Details = null);

/// <summary>串行启动固定清理执行端，验证管道客户端和回复身份，释放后拒绝结果。</summary>
public sealed class SystemMemoryCleanupService : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private int _running;
    private bool _disposed;
    private readonly string _owner = WindowsIdentity.GetCurrent().User!.Value;
    private readonly string _instance = MemoryCleanupTasks.InstanceId(Environment.ProcessPath!);
    private byte[]? _helperHash;
    public MemoryCleanupAuthorization Authorization { get; private set; }
    public event EventHandler? Changed;

    public async Task<MemoryCleanupAuthorization> RefreshAuthorizationAsync()
    {
        var value = await Task.Run(() =>
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AFMediaBar.MemoryHelper.exe");
            if (resource is null) return MemoryCleanupAuthorization.Unavailable;
            _helperHash ??= SHA256.HashData(resource);
            return MemoryCleanupTasks.Inspect(_owner, _instance, _helperHash);
        });
        if (!_disposed) { Authorization = value; Changed?.Invoke(this, EventArgs.Empty); }
        return value;
    }

    public async Task<SystemMemoryCleanupResult> ManageAuthorizationAsync(bool install)
    {
        if (_disposed || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return new(SystemMemoryCleanupStatus.Busy, null, null);
        Changed?.Invoke(this, EventArgs.Empty);
        try { return await ExecuteAsync(SystemMemoryOptimizationLevel.Standard, install ? "--install" : "--revoke"); }
        finally { Interlocked.Exchange(ref _running, 0); if (!_disposed) await RefreshAuthorizationAsync(); }
    }
    public SystemMemoryCleanupResult? LastResult { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public static bool IsCleanupRequest(string[] args) => args.Any(arg => arg.StartsWith("--internal-memory-clean", StringComparison.Ordinal));

    public async Task<SystemMemoryCleanupResult> RunAsync(SystemMemoryOptimizationLevel level = SystemMemoryOptimizationLevel.Standard, bool requireAuthorization = false)
    {
        if (_disposed) return new(SystemMemoryCleanupStatus.Cancelled, null, null);
        if (!Enum.IsDefined(level)) return new(SystemMemoryCleanupStatus.InvalidRequest, null, null);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return new(SystemMemoryCleanupStatus.Busy, null, null);
        LastAttemptAt = DateTimeOffset.UtcNow;
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
        try
        {
            before = await Task.Run(MemoryToolsService.CaptureAvailableBytes);
            var nonce = Guid.NewGuid().ToString("N");
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));
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
                helper = await Task.Run(() => ExtractHelper(directory));
                var arguments = management is null
                    ? "--run " + (level == SystemMemoryOptimizationLevel.Deep ? "deep" : "standard") + " " + nonce
                    : management + " " + _owner + " " + _instance + " " + nonce;
                process = await Task.Run(() => Process.Start(new ProcessStartInfo(helper)
                { Arguments = arguments, UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }));
                if (process is null) throw new InvalidOperationException();
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(65));
            await pipe.WaitForConnectionAsync(timeout.Token);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientId)) throw new InvalidDataException();
            if (process is not null && clientId != process.Id) throw new InvalidDataException();
            var validImage = await Task.Run(() =>
            {
                using var client = Process.GetProcessById(clientId);
                return string.Equals(client.MainModule?.FileName, helper, StringComparison.OrdinalIgnoreCase);
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
                reply.Stages.Any(stage => !Enum.IsDefined(stage.Status))) throw new InvalidDataException();
            if (process is not null) await process.WaitForExitAsync(timeout.Token);
            return new(reply.Status, before, await Task.Run(MemoryToolsService.CaptureAvailableBytes), reply);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { return new(SystemMemoryCleanupStatus.Cancelled, before, null); }
        catch (Exception error) when (error is UnauthorizedAccessException || error is Win32Exception { NativeErrorCode: 5 }) { return new(SystemMemoryCleanupStatus.PermissionDenied, before, null); }
        catch (OperationCanceledException) { return new(_stopping.IsCancellationRequested ? SystemMemoryCleanupStatus.Cancelled : SystemMemoryCleanupStatus.TimedOut, before, null); }
        catch (Exception) { return new(SystemMemoryCleanupStatus.Failed, before, null); }
        finally
        {
            process?.Dispose();
            if (directory is not null) TryDeleteExtraction(directory);
        }
    }

    internal static string ExtractHelper(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "AFMediaBar.MemoryHelper.exe");
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AFMediaBar.MemoryHelper.exe")
            ?? throw new InvalidOperationException("Missing embedded memory helper.");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        resource.CopyTo(file);
        return path;
    }
    internal static void TryDeleteExtraction(string directory)
    {
        // 只清理本次创建的已知文件，不递归处理调用方目录。
        try { File.Delete(Path.Combine(directory, "AFMediaBar.MemoryHelper.exe")); Directory.Delete(directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _stopping.Cancel(); }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out int clientId);
}
