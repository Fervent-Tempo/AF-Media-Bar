// 防止把配置界面、同名前缀或已兼容的 TranslucentTB 错报成任务栏兼容风险。
using AFMediaBar.Classes.Services.Home;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证安装型与便携工具的证据匹配，不访问用户进程或注册表。</summary>
[TestClass]
public sealed class TaskbarCustomizationTests
{
    [TestMethod]
    public void InstalledAndPortableToolsKeepTheirEvidenceAndDeduplicate()
    {
        var matches = TaskbarCustomizationService.Match(["WINDHAWK.exe", "windhawk", "RetroBar"],
            ["Windhawk v1.6", "ExplorerPatcher", "StartAllBack 3.9.21", "Stardock Start11 2.6", "Open-Shell", "StartIsBack++"]);
        Assert.AreEqual(7, matches.Count);
        var windhawk = matches.Single(item => item.Name == "Windhawk");
        Assert.IsTrue(windhawk.IsInstalled && windhawk.IsRunning);
        Assert.IsTrue(matches.Single(item => item.Name == "ExplorerPatcher").IsInstalled);
        Assert.IsTrue(matches.Single(item => item.Name == "RetroBar").IsRunning);
        Assert.IsFalse(matches.Single(item => item.Name == "RetroBar").IsInstalled);
    }

    [TestMethod]
    public void CompatibleToolsAndUnrelatedNamesCannotCreateWarnings()
    {
        var matches = TaskbarCustomizationService.Match(
            ["TranslucentTB", "TaskbarX Configurator", "StartMenu", "windhawk-backup", "not-windhawk"],
            ["TranslucentTB", "Not StartAllBack", "Start11Backup", "ExplorerPatcherHelper", "Microsoft Windows"]);
        Assert.AreEqual(0, matches.Count);
    }
}
