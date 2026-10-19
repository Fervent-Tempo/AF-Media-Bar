// 无 WPF/Host 的一次性清理入口；通信仅向发起用户写入固定结果，永不读取管道命令。
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.MemoryHelper;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var watchdog = new Timer(_ => Environment.Exit((int)SystemMemoryCleanupStatus.TimedOut),
            null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        try
        {
            if (args.Length != 3 || args[0] != "--run" ||
                !Guid.TryParseExact(args[2], "N", out _) ||
                args[1] is not ("standard" or "deep")) return (int)SystemMemoryCleanupStatus.InvalidRequest;
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                return (int)SystemMemoryCleanupStatus.PermissionDenied;
            using var pipe = new NamedPipeClientStream(".", "AFMB.Memory." + args[2], PipeDirection.Out,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous);
            await pipe.ConnectAsync(5000);
            var level = args[1] == "deep" ? SystemMemoryOptimizationLevel.Deep : SystemMemoryOptimizationLevel.Standard;
            var reply = await Task.Run(() => MemoryCleanupOperations.Execute(level, args[2]));
            await using var writer = new StreamWriter(pipe);
            await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
            await writer.FlushAsync();
            return (int)reply.Status;
        }
        catch (Exception) { return (int)SystemMemoryCleanupStatus.Failed; }
    }
}
