// 区分实际服务错误与普通启动异常，防止误报精简系统；不停止或修改任何 Windows 服务。
using System.ComponentModel;
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Services.Startup;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>启动异常经过任务或 DI 包装后仍能识别服务故障。</summary>
[TestClass]
public sealed class StartupServiceDiagnosticsTests
{
    [TestMethod]
    public void WrappedServiceFailureIsRecognizedWithoutMisclassifyingOtherFailures()
    {
        Assert.IsTrue(StartupServiceDiagnostics.IsServiceFailure(new InvalidOperationException("DI", new Win32Exception(1058))));
        Assert.IsTrue(StartupServiceDiagnostics.IsServiceFailure(new AggregateException(new IOException(), new COMException("Missing service", unchecked((int)0x80070424)))));
        Assert.IsFalse(StartupServiceDiagnostics.IsServiceFailure(new InvalidOperationException("DI", new IOException("Missing settings"))));
        Assert.IsFalse(StartupServiceDiagnostics.IsServiceFailure(new COMException("Access denied", unchecked((int)0x80070005))));
    }
}
