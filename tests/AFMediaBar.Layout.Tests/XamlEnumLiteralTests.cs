using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// XAML 里枚举字面量的校验测试。
///
/// 为什么需要它：XAML 把 <c>StatusTone="Success"</c> 这类写法留到**运行时**才解析，编译、单元测试与架构扫描
/// 都发现不了；而设置页是懒加载的，写错的分支可能几周都碰不到。这个测试在编译期之后、运行期之前把每一处
/// 枚举字面量对着真实的类型检查一遍，因为一次这样的错误会让整页打不开
/// （表现为 <c>XamlParseException</c> + <c>EnumConverter</c> 的 “Requested value was not found”）。
/// Validation tests for enum literals written in XAML.
///
/// Why this exists: XAML resolves a spelling such as <c>StatusTone="Success"</c> only at run time, so compilation,
/// unit tests and the architecture scan all pass; and because settings pages load lazily, a broken branch can go
/// unnoticed for weeks. This test checks every enum literal against its real type between compile time and run time,
/// because one such mistake makes the whole page fail to open (a <c>XamlParseException</c> wrapping the
/// <c>EnumConverter</c>'s "Requested value was not found").
/// </summary>
[TestClass]
public sealed class XamlEnumLiteralTests
{
    private static readonly Regex IconMarkup = new(
        @"^\{\s*ui:SymbolIcon(?:Source)?\s+(?:Symbol\s*=\s*)?(?<name>[A-Za-z0-9_]+)\s*\}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [TestMethod]
    public void EveryComponentEnumAttributeAndIconNameInXamlIsValid()
    {
        var problems = new List<string>();
        var checkedLiteralCount = 0;

        foreach (var file in EnumerateXamlFiles())
        {
            XDocument document;
            try
            {
                document = XDocument.Load(file);
            }
            catch (Exception exception)
            {
                problems.Add($"{Path.GetFileName(file)}: XAML 无法解析 - {exception.Message}");
                continue;
            }

            foreach (var element in document.Descendants())
            {
                var elementType = ResolveType(element.Name);
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        continue;
                    }

                    var property = FindProperty(elementType, attribute.Name.LocalName);
                    var value = attribute.Value.Trim();
                    if (property is null)
                    {
                        continue;
                    }

                    // 标记扩展（Binding / StaticResource / ui:SymbolIcon）不是字面量，只有图标写法可以在这里校验。
                    if (value.StartsWith('{'))
                    {
                        var icon = IconMarkup.Match(value);
                        if (icon.Success && !IsEnumMember(typeof(Wpf.Ui.Controls.SymbolRegular), icon.Groups["name"].Value))
                        {
                            problems.Add(
                                $"{Path.GetFileName(file)}: {element.Name.LocalName}.{attribute.Name.LocalName} 使用了不存在的图标 {icon.Groups["name"].Value}");
                        }

                        continue;
                    }

                    if (!property.PropertyType.IsEnum)
                    {
                        continue;
                    }

                    checkedLiteralCount++;
                    if (!IsEnumMember(property.PropertyType, value))
                    {
                        problems.Add(
                            $"{Path.GetFileName(file)}: {element.Name.LocalName}.{attribute.Name.LocalName}=\"{value}\" 不是 {property.PropertyType.Name} 的成员" +
                            $"（可用值：{string.Join(", ", Enum.GetNames(property.PropertyType))}）");
                    }
                }
            }
        }

        Assert.IsTrue(checkedLiteralCount > 0, "没有校验到任何枚举字面量，说明扫描逻辑已经失效。");
        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public void TheEnumCheckWouldCatchAnInventedStatusTone()
    {
        // 自检：这条规则必须真的能捕获违规，否则它只是一段永远通过的代码。
        // Self-check: the rule must really catch a violation, otherwise it is code that can only ever pass.
        Assert.IsFalse(IsEnumMember(typeof(AFMediaBar.Components.SettingsChipTone), "Success"));
        Assert.IsTrue(IsEnumMember(typeof(AFMediaBar.Components.SettingsChipTone), "Accent"));
    }

    [TestMethod]
    public void SettingsCalloutCompiledResourceIsPresent()
    {
        var assembly = typeof(AFMediaBar.Components.SettingsCallout).Assembly;
        using var stream = assembly.GetManifestResourceStream("AFMediaBar.g.resources");
        Assert.IsNotNull(stream, "WPF compiled-resource table is missing.");
        using var resources = new ResourceReader(stream!);
        var keys = resources.Cast<DictionaryEntry>()
            .Select(entry => entry.Key?.ToString())
            .Where(key => key is not null)
            .ToArray();

        CollectionAssert.Contains(keys, "components/settingscallout.baml");
    }

    private static bool IsEnumMember(Type enumType, string value) =>
        Enum.TryParse(enumType, value, ignoreCase: false, out var parsed) && Enum.IsDefined(enumType, parsed);

    /// <summary>
    /// 逐层查找属性。
    ///
    /// 不能用 <c>Type.GetProperty</c>：<c>Wpf.Ui.Controls.MenuItem</c> 同时从 <c>IconElement</c> 继承并在自身声明
    /// <c>Icon</c>，直接查询会抛 <c>AmbiguousMatchException</c>。逐层 <c>DeclaredOnly</c> 查询既避开歧义，
    /// 也仍然能找到基类上的属性。
    /// Looks a property up one level at a time.
    ///
    /// <c>Type.GetProperty</c> cannot be used here: <c>Wpf.Ui.Controls.MenuItem</c> both inherits <c>Icon</c> from
    /// <c>IconElement</c> and declares its own, which makes a direct lookup throw <c>AmbiguousMatchException</c>.
    /// Walking the hierarchy with <c>DeclaredOnly</c> avoids the ambiguity while still finding inherited properties.
    /// </summary>
    private static PropertyInfo? FindProperty(Type? type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static Type? ResolveType(XName name)
    {
        if (name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var declaration = name.NamespaceName["clr-namespace:".Length..];
            var separator = declaration.IndexOf(';');
            var namespaceName = separator >= 0 ? declaration[..separator] : declaration;
            var assemblyName = separator >= 0 && declaration[(separator + 1)..].StartsWith("assembly=", StringComparison.Ordinal)
                ? declaration[(separator + 1 + "assembly=".Length)..]
                : null;
            var assembly = assemblyName is null
                ? typeof(AFMediaBar.App).Assembly
                : Assembly.Load(assemblyName);
            return assembly.GetType($"{namespaceName}.{name.LocalName}");
        }

        // WPF-UI 的 ui: 命名空间：图标与控件的枚举都在同一个程序集里。
        // The WPF-UI ui: namespace: both its icons and its control enums live in one assembly.
        if (name.NamespaceName.Contains("wpfui", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(Wpf.Ui.Controls.SymbolIcon).Assembly.GetType($"Wpf.Ui.Controls.{name.LocalName}");
        }

        return null;
    }

    private static IEnumerable<string> EnumerateXamlFiles()
    {
        var root = FindRepositoryRoot();
        Assert.IsNotNull(root, "找不到仓库根目录，无法扫描 XAML。");
        var sourceRoot = Path.Combine(root!, "src", "AFMediaBar");
        Assert.IsTrue(Directory.Exists(sourceRoot), $"找不到源码目录 {sourceRoot}。");

        return Directory
            .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FindRepositoryRoot()
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

        return null;
    }
}
