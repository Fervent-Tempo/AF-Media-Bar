// 仅在首页请求时后台读取进程名与卸载登记，不读取命令行、不联网、不加载第三方模块。
// 每次扫描拥有并释放 Process 和 RegistryKey；不持有计时器、订阅或跨页面状态。
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using AFMediaBar.Classes.Models.Home;
using Microsoft.Win32;

namespace AFMediaBar.Classes.Services.Home;

/// <summary>检测可能修改任务栏的已知美化工具，不判定其配置或兼容性。</summary>
public sealed class TaskbarCustomizationService
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private sealed record Tool(string Name, string[] Processes, string[] Products);
    // TranslucentTB 已兼容，不纳入提醒；注入 Explorer 的工具也通过安装登记识别。
    private static readonly Tool[] _tools =
    [
        new("Windhawk", ["windhawk"], ["Windhawk"]),
        new("ExplorerPatcher", [], ["ExplorerPatcher"]),
        new("StartAllBack", [], ["StartAllBack"]),
        new("StartIsBack", [], ["StartIsBack", "StartIsBack+", "StartIsBack++"]),
        new("Start11", ["Start11", "Start11_64"], ["Start11", "Stardock Start11"]),
        new("TaskbarX", ["TaskbarX"], ["TaskbarX"]),
        new("RoundedTB", ["RoundedTB"], ["RoundedTB"]),
        new("TaskbarXI", ["TaskbarXI"], ["TaskbarXI"]),
        new("RetroBar", ["RetroBar"], ["RetroBar"]),
        new("Open-Shell", [], ["Open-Shell", "Open-Shell Menu"])
    ];

    public Task<CustomizationDetectionResult> DetectAsync(CancellationToken token) => Task.Run(() => Scan(token), token);

    /// <summary>精确匹配进程及产品名；产品名允许附加空格分隔的说明。</summary>
    public static IReadOnlyList<CustomizationToolMatch> Match(IEnumerable<string> processNames, IEnumerable<string> productNames)
    {
        var processes = processNames.Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var products = productNames.Select(name => name.Trim()).ToArray();
        return _tools.Select(tool => new CustomizationToolMatch(tool.Name,
            tool.Products.Any(alias => products.Any(name => name.Equals(alias, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))),
            tool.Processes.Any(processes.Contains))).Where(tool => tool.IsInstalled || tool.IsRunning).ToArray();
    }

    private static CustomizationDetectionResult Scan(CancellationToken token)
    {
        var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var products = new List<string>();
        var complete = true;
        try
        {
            using var current = Process.GetCurrentProcess();
            var session = current.SessionId;
            var snapshot = Process.GetProcesses();
            try
            {
                foreach (var process in snapshot)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var name = process.ProcessName;
                        if (_tools.Any(tool => tool.Processes.Contains(name, StringComparer.OrdinalIgnoreCase)) && process.SessionId == session)
                            processes.Add(name);
                    }
                    catch (Exception exception) when (IsReadFailure(exception)) { complete = false; }
                }
            }
            finally { foreach (var process in snapshot) process.Dispose(); }
        }
        catch (Exception exception) when (IsReadFailure(exception)) { complete = false; }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(UninstallPath, writable: false);
                    if (uninstall is null) continue;
                    var names = uninstall.GetSubKeyNames();
                    if (names.Length > 4096) complete = false;
                    foreach (var name in names.Take(4096))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            using var entry = uninstall.OpenSubKey(name, writable: false);
                            if (entry?.GetValue("DisplayName", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string displayName)
                                products.Add(displayName);
                        }
                        catch (Exception exception) when (IsReadFailure(exception)) { complete = false; }
                    }
                }
                catch (Exception exception) when (IsReadFailure(exception)) { complete = false; }
            }
        }
        token.ThrowIfCancellationRequested();
        return new(Match(processes, products), complete);
    }

    private static bool IsReadFailure(Exception exception) => exception is Win32Exception or InvalidOperationException
        or UnauthorizedAccessException or SecurityException or IOException;
}
