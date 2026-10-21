// 隔离会进入 HWND/UIA 的回归，避免进程级 WPF 状态或未返回的原生线程污染后续测试。
// 本帮助类拥有测试子进程和临时结果目录；结束或超时后回收进程并验证用例确实执行。
using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class NativeTestProcess
{
    internal static async Task<bool> RunIfNeededAsync(string testName)
    {
        const string IsolationVariable = "AFMB_NATIVE_TEST_CHILD";
        if (Environment.GetEnvironmentVariable(IsolationVariable) == testName) return false;

        var results = Path.Combine(Path.GetTempPath(), "AFMB-NativeTest-" + Guid.NewGuid().ToString("N"));
        var reportPath = Path.Combine(results, "native.trx");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(NativeTestProcess).Assembly.Location);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + testName);
        start.ArgumentList.Add("/Logger:trx;LogFileName=native.trx");
        start.ArgumentList.Add("/ResultsDirectory:" + results);
        start.Environment[IsolationVariable] = testName;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start native regression test.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            Assert.AreEqual(0, child.ExitCode, (await output) + (await errors));
            var report = XDocument.Load(reportPath);
            var counters = report.Descendants().Single(element => element.Name.LocalName == "Counters");
            Assert.AreEqual("1", counters.Attribute("executed")?.Value, "The isolated test must actually execute.");
            Assert.AreEqual("1", counters.Attribute("passed")?.Value);
            return true;
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await output;
            await errors;
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!Path.GetFullPath(results).StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Native test results must stay inside the temporary directory.");
            if (Directory.Exists(results)) Directory.Delete(results, recursive: true);
        }
    }
}
