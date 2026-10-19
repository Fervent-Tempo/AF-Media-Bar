// 固定操作的一次性执行端；只向原用户写入结果，不读取管道命令，不初始化应用服务。
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using AFMediaBar.Classes.Services;
using Microsoft.Win32.SafeHandles;

using AFMediaBar.Classes.Settings;

namespace AFMediaBar.MemoryHelper;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var watchdog = new Timer(_ => Environment.Exit((int)SystemMemoryCleanupStatus.TimedOut),
            null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        try
        {
            var run = args.Length == 3 && args[0] == "--run" && (args[1] is "standard" or "deep") && Guid.TryParseExact(args[2], "N", out _);
            var manage = args.Length == 4 && (args[0] is "--install" or "--revoke") && Guid.TryParseExact(args[3], "N", out _);
            var uninstall = args.Length == 3 && args[0] == "--uninstall";
            if (!run && !manage && !uninstall) return (int)SystemMemoryCleanupStatus.InvalidRequest;
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return (int)SystemMemoryCleanupStatus.PermissionDenied;
            if ((manage || uninstall) && (identity.User?.Value != args[1] || !MemoryCleanupTasks.ValidInstance(args[2]))) return (int)SystemMemoryCleanupStatus.InvalidRequest;
            if (uninstall)
            {
                MemoryCleanupTasks.Remove(args[1], args[2]);
                return (int)SystemMemoryCleanupStatus.Completed;
            }
            var nonce = args[^1];
            if (!Guid.TryParseExact(nonce, "N", out _)) return (int)SystemMemoryCleanupStatus.InvalidRequest;
            using var pipe = new NamedPipeClientStream(".", "AFMB.Memory." + nonce, PipeDirection.Out,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous);
            await pipe.ConnectAsync(5000);
            MemoryCleanupReply reply;
            var level = run && args[1] == "deep" ? SystemMemoryOptimizationLevel.Deep : SystemMemoryOptimizationLevel.Standard;
            if (manage)
            {
                var status = SystemMemoryCleanupStatus.Completed;
                string? errorCode = null;
                try
                {
                    if (args[0] == "--install")
                    {
                        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverId)) throw new UnauthorizedAccessException();
                        using var server = Process.GetProcessById(serverId);
                        var origin = server.MainModule?.FileName ?? throw new UnauthorizedAccessException();
                        if (!Path.GetFileName(origin).Equals("AFMediaBar.exe", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
                        MemoryCleanupTasks.Install(args[1], args[2], origin);
                    }
                    else MemoryCleanupTasks.Remove(args[1], args[2]);
                }
                catch (UnauthorizedAccessException error) { status = SystemMemoryCleanupStatus.PermissionDenied; errorCode = $"HRESULT 0x{error.HResult:X8}"; }
                catch (Exception error) { status = SystemMemoryCleanupStatus.Failed; errorCode = $"{error.GetType().Name} 0x{error.HResult:X8}"; }
                reply = new(MemoryCleanupReply.ProtocolVersion, nonce, level, status, [new("Authorization", status, errorCode)], 0);
            }
            else reply = await Task.Run(() => MemoryCleanupOperations.Execute(level, nonce));
            await using var writer = new StreamWriter(pipe);
            await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
            await writer.FlushAsync();
            // 保持客户端存活到父进程完成身份检查并读完结果，不接收任何命令。
            pipe.WaitForPipeDrain();
            return (int)reply.Status;
        }
        catch (Exception) { return (int)SystemMemoryCleanupStatus.Failed; }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out int processId);
}
