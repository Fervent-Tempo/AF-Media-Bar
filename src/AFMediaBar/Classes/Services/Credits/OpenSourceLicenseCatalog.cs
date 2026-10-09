using AFMediaBar.Classes.Models.Credits;

namespace AFMediaBar.Classes.Services.Credits;

/// <summary>
/// 开源许可清单：本程序实际使用了哪些第三方包与哪些项目的代码。
/// The open-source license catalog: which third-party packages and which projects' code this program actually uses.
///
/// 这份清单是**手写但可核对**的：每一条的版本与许可都能在 `AFMediaBar.csproj` 的 <c>PackageReference</c> 与本地
/// NuGet 包的 <c>.nuspec</c>（<c>license type="expression"</c>）里逐条对上。因此它不随构建自动生成——自动生成要么引入
/// 构建期依赖，要么在打包方式变化时悄悄漏项，而这一节的全部价值就是"说的是真的"。
/// The catalog is **hand-written but checkable**: every version and license can be matched one by one against the
/// <c>PackageReference</c> entries in `AFMediaBar.csproj` and the <c>.nuspec</c> of the local NuGet packages
/// (<c>license type="expression"</c>). It is therefore not generated during the build, because generation would either add a
/// build-time dependency or silently miss entries when packaging changes, and the entire value of this section is that it is true.
/// </summary>
public static class OpenSourceLicenseCatalog
{
    /// <summary>
    /// 本程序使用的第三方 NuGet 包（按包名字母序）。
    /// The third-party NuGet packages this program uses, in package-name order.
    /// </summary>
    public static IReadOnlyList<LicenseEntry> Packages { get; } =
    [
        new("CommunityToolkit.Mvvm", "8.4.0", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("Dubya.WindowsMediaController", "2.5.6", "MIT", "https://github.com/DubyaDude/WindowsMediaController"),
        new("F23.StringSimilarity", "7.0.1", "MIT", "https://github.com/feature23/StringSimilarity.NET"),
        new("Lyricify.Lyrics.Helper", "0.2.0", "Apache-2.0", "https://github.com/WXRIW/Lyricify-Lyrics-Helper"),
        new("MicaWPF", "7.1.0", "MIT", "https://github.com/Simnico99/MicaWPF"),
        new("Microsoft.Extensions.Hosting", "10.0.1", "MIT", "https://github.com/dotnet/runtime"),
        new("Microsoft.Web.WebView2", "1.0.4191.47", "BSD-3-Clause", "https://aka.ms/webview"),
        new("NAudio.Wasapi", "3.1.0", "MIT", "https://github.com/naudio/NAudio"),
        new("OpenccNetLib", "1.7.0", "MIT", "https://github.com/laisuk/OpenccNet"),
        new("WPF-UI", "4.2.0", "MIT", "https://github.com/lepoco/wpfui"),
        new("WPF-UI.DependencyInjection", "4.2.0", "MIT", "https://github.com/lepoco/wpfui")
    ];

    /// <summary>
    /// 本程序**衍生代码**的来源项目：文件头保留着它们的版权与许可声明，因此必须与包一起列出，
    /// 否则这一节会漏掉"我们自己的文件里也有别人的代码"这一事实。
    /// The projects this program's **derived code** comes from: their copyright and license notices are kept in the file
    /// headers, so they have to be listed next to the packages, otherwise this section would hide the fact that some of our own
    /// files carry someone else's code.
    /// </summary>
    public static IReadOnlyList<LicenseEntry> DerivedCode { get; } =
    [
        new(
            "FluentFlyout",
            string.Empty,
            "GPL-3.0",
            "https://github.com/unchihugo/FluentFlyout",
            "ArtworkLoader / BitmapHelper / LruCache 等文件的头部保留了原项目的版权与许可声明 / file headers in ArtworkLoader, BitmapHelper, LruCache and others keep the original notice")
        
    ];

    /// <summary>完整的许可清单：包在前、衍生代码在后。/ The complete catalog: packages first, derived code after.</summary>
    public static IReadOnlyList<LicenseEntry> All { get; } =
        [.. Packages, .. DerivedCode];

    /// <summary>
    /// 许可标识的展示文本。界面上不翻译 SPDX 标识本身（它是标识符），只在旁边补一句可读说明。
    /// The display text of a license identifier. The SPDX identifier itself is not translated on the interface — it is an
    /// identifier — and a readable note is added next to it instead.
    /// </summary>
    /// <param name="license">许可标识。/ License identifier.</param>
    /// <returns>展示文本。/ Display text.</returns>
    public static string DescribeLicense(string license) =>
        string.IsNullOrWhiteSpace(license) ? string.Empty : license.Trim();
}
