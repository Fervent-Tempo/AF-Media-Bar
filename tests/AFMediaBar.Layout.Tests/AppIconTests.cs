using System.Text.RegularExpressions;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 程序自身图标的两条回归守卫。
///
/// 按 `../IMPLEMENTATION_CONSTRAINTS.md` §13.1，这里只留"已经真实发生过一次"的故障：素材引用失效（图标不显示、
/// 安装包构建失败）与托盘跟错主题（图标在深色任务栏上看不见）。主题到图形的取值映射、深浅判定权重、系统模式读不到时的
/// 回退分支都**刻意不加测试**——它们守的是看代码就知道的常量与算术，改错了切一次主题就能看见，正是 §13.1 列为"不该新增"
/// 的那一类。
/// Two regression guards for the application's own icon. Per §13.1 only failures that really happened once are kept here:
/// references to assets that no longer exist (an icon that never draws, an installer build that fails) and a tray icon following
/// the wrong theme (invisible on a dark taskbar). The theme-to-artwork mapping, the luminance weights, and the fallback branch for
/// an unreadable system mode are deliberately **not** tested: they guard constants and arithmetic that reading the code already
/// reveals, a mistake shows up the first time the theme is switched, and §13.1 names exactly that as "should not be added".
/// </summary>
[TestClass]
public sealed class AppIconTests
{
    /// <summary>
    /// 托盘跟系统模式（任务栏）而不是本程序主题。
    ///
    /// 守的故障：真实发生过一次——深浅色切换时托盘图标不变，而窗口图标变了。当时托盘与窗口共用"本程序主题"这一个判定，
    /// 但 Windows 允许"任务栏浅色 + 应用深色"这类组合，共用一套必然有一边贴错底色的图形。
    /// Guards a failure that really happened once: the tray icon stayed put when the theme was switched while the window icons
    /// changed. The tray and the windows shared a single "application theme" decision, but Windows allows a light taskbar with dark
    /// applications, so one shared decision always puts the wrong artwork on one of the two surfaces.
    /// </summary>
    [TestMethod]
    public void TrayFollowsTheSystemModeInsteadOfTheApplicationTheme()
    {
        Assert.AreEqual(
            AppIconPolicy.DarkArtworkUri,
            AppIconPolicy.ResolveTrayArtworkUri(WindowsThemeDetector.ThemeMode.Light, ApplicationTheme.Dark, Colors.White),
            "浅色任务栏必须用深色图形，即使应用主题是深色。");

        Assert.AreEqual(
            AppIconPolicy.LightArtworkUri,
            AppIconPolicy.ResolveTrayArtworkUri(WindowsThemeDetector.ThemeMode.Dark, ApplicationTheme.Light, Colors.Black),
            "深色任务栏必须用白色图形，即使应用主题是浅色。");
    }

    /// <summary>
    /// 每一处被引用的图标素材都必须真的存在。
    ///
    /// 守的故障：真实发生过一次——四张旧图标被删除、两张新图标放进来，而 `.csproj`、`SettingsWindow.xaml` 与安装脚本仍然
    /// 指向旧文件。XAML 上的表现是图标画不出来，安装脚本上的表现是 CI 直接编译失败，两者都不会出现在"生成成功"里。
    /// Guards a failure that really happened once: four old icons were deleted and two new ones added while the `.csproj`,
    /// `SettingsWindow.xaml` and the installer script still pointed at the old files. In XAML the symptom is an icon that never
    /// draws, in the installer script it is a CI build that fails, and neither appears in a successful build.
    /// </summary>
    [TestMethod]
    public void EveryReferencedIconAssetExists()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root, "src", "AFMediaBar");
        var missing = new List<string>();
        var checkedCount = 0;

        foreach (var xaml in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories).Where(IsSourceFile))
        {
            var text = File.ReadAllText(xaml);
            foreach (Match match in Regex.Matches(text, @"pack://application:,,,/(Assets/[^""'\s]+)"))
            {
                checkedCount++;
                var target = Path.Combine(appRoot, match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(target))
                {
                    missing.Add($"{Path.GetFileName(xaml)} → {match.Groups[1].Value}");
                }
            }
        }

        var projectFile = Path.Combine(appRoot, "AFMediaBar.csproj");
        foreach (Match match in Regex.Matches(File.ReadAllText(projectFile), @"(?:ApplicationIcon|Include|Remove)=""(Assets\\[^""]+)"""))
        {
            var relative = match.Groups[1].Value;

            // 通配项允许匹配到零个文件（赞助收款码就是"用户以后再放"的），因此只检查具体文件名。
            // A wildcard entry may legitimately match nothing (the sponsor payment codes are added later), so only concrete names
            // are checked.
            if (relative.Contains('*'))
            {
                continue;
            }

            checkedCount++;
            if (!File.Exists(Path.Combine(appRoot, relative)))
            {
                missing.Add($"AFMediaBar.csproj → {relative}");
            }
        }

        var installerScript = Path.Combine(root, "installer", "AFMediaBar.iss");
        foreach (Match match in Regex.Matches(File.ReadAllText(installerScript), @"\.\.\\src\\AFMediaBar\\([^""\r\n]+)"))
        {
            checkedCount++;
            var relative = match.Groups[1].Value;
            if (!File.Exists(Path.Combine(appRoot, relative)))
            {
                missing.Add($"AFMediaBar.iss → {relative}");
            }
        }

        Assert.IsTrue(checkedCount >= 4, $"只检查到 {checkedCount} 处素材引用，扫描逻辑可能已经失效。");
        Assert.AreEqual(
            0,
            missing.Count,
            "以下位置引用了不存在的素材：\n  " + string.Join("\n  ", missing));
    }

    /// <summary>排除 obj 与 bin：生成目录里会出现从 XAML 复制出来的中间文件。/ Excludes obj and bin, whose generated copies of XAML would otherwise be scanned too.</summary>
    private static bool IsSourceFile(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>从测试输出目录向上找到含解决方案与主项目的仓库根。/ Walks up from the test output directory to the repository root holding the solution and the app project.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "AFMediaBar", "AFMediaBar.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }
}
