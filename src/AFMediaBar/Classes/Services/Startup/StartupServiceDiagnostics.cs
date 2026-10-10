// 仅在启动失败后查询系统服务；查询句柄由本次调用拥有并在 finally 中释放，不修改系统配置。
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AFMediaBar.Classes.Services.Startup;

/// <summary>识别系统服务或 COM 激活故障，并读取可确认的服务异常。</summary>
public static class StartupServiceDiagnostics
{
    internal readonly record struct ServiceFault(string Name, string StatusKey);

    public static bool IsServiceFailure(Exception exception)
    {
        if (exception is AggregateException aggregate)
            return aggregate.InnerExceptions.Any(IsServiceFailure);
        var code = exception is Win32Exception win32 ? win32.NativeErrorCode : exception.HResult;
        var matches = code is 1058 or 1060 or 1062 or 1068 or 1722 ||
            unchecked((uint)code) is 0x80070422 or 0x80070424 or 0x80070426 or 0x8007042C or 0x800706BA or 0x80040154 or 0x80080005;
        return matches || exception.InnerException is { } inner && IsServiceFailure(inner);
    }

    internal static IReadOnlyList<ServiceFault> Inspect()
    {
        var faults = new List<ServiceFault>();
        var manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) return faults;
        try
        {
            foreach (var name in new[] { "RpcSs", "DcomLaunch", "AudioEndpointBuilder", "Audiosrv", "TimeBrokerSvc" })
            {
                var service = OpenService(manager, name, 4);
                if (service == IntPtr.Zero)
                {
                    if (Marshal.GetLastWin32Error() == 1060)
                        faults.Add(new(name, "Startup.Service.Missing"));
                    continue;
                }
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
                    if (key?.GetValue("Start") is int start && start == 4)
                        faults.Add(new(name, "Startup.Service.Disabled"));
                    // Time Broker 按需启动，正常停止不能作为故障报告。
                    else if (name != "TimeBrokerSvc" && QueryServiceStatus(service, out var status) && status.CurrentState == 1)
                        faults.Add(new(name, "Startup.Service.Stopped"));
                }
                catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
                {
                    // 查询失败无法证明服务缺失，保留原始启动异常供用户定位。
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
        return faults;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr service);
}
