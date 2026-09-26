using System.Windows.Media;
using System.Windows;
using System.Windows.Markup;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Settings;

/// <summary>外观选择器中的字体及其原字体字样。/ A font and its own specimen in the appearance selectors.</summary>
public sealed record FontFamilyChoice(string Name, string DisplayName, string PreviewText = "", FontFamily? PreviewFontFamily = null);

/// <summary>枚举本机可用字体，并为 WPF 创建按字符范围选字的组合字体。/ Enumerates installed fonts and builds WPF script-mapped composite fonts.</summary>
public static class InstalledFontCatalog
{
    // Han ranges are placed before the catch-all map so a Latin font with Han glyphs cannot hide the CJK choice.
    // 汉字范围先于兜底映射，避免西文字体自带汉字时遮住用户选的中文字体。
    private const string HanRanges = "2E80-2FFF, 3000-303F, 31C0-31EF, 3400-4DBF, 4E00-9FFF, F900-FAFF, 20000-2FA1F";
    private const string AllCharacters = "0000-10FFFF";
    private static readonly Dictionary<string, (bool Latin, bool Cjk)> CoverageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> CommonChineseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["YouYuan"] = "幼圆",
        ["Microsoft YaHei"] = "微软雅黑",
        ["Microsoft YaHei UI"] = "微软雅黑 UI",
        ["DengXian"] = "等线",
        ["SimSun"] = "宋体",
        ["NSimSun"] = "新宋体",
        ["SimHei"] = "黑体",
        ["KaiTi"] = "楷体",
        ["FangSong"] = "仿宋"
    };

    /// <summary>枚举能显示目标文字的本机字体并为每项生成原字体预览。/ Lists installed fonts that cover the target script, with a specimen in each font.</summary>
    public static IReadOnlyList<FontFamilyChoice> GetChoices(string followSystemResourceKey, bool cjk)
    {
        var installed = Fonts.SystemFontFamilies
            .Where(family => !string.IsNullOrWhiteSpace(family.Source) && !family.Source.Contains(','))
            .GroupBy(family => family.Source, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.First())
            .Where(family => SupportsScript(family, cjk))
            .OrderBy(family => cjk ? GetChineseDisplayName(family) : family.Source, StringComparer.CurrentCultureIgnoreCase)
            .Select(family => new FontFamilyChoice(
                family.Source,
                cjk ? GetChineseDisplayName(family) : family.Source,
                Translations.Get(cjk ? "Appearance.CjkFont.Sample" : "Appearance.LatinFont.Sample"),
                family));

        return new[] { new FontFamilyChoice(
                string.Empty,
                Translations.Get(followSystemResourceKey),
                Translations.Get(cjk ? "Appearance.CjkFont.Sample" : "Appearance.LatinFont.Sample"),
                SystemFonts.MessageFontFamily) }
            .Concat(installed)
            .ToArray();
    }

    /// <summary>按字符范围创建应用字体，中文选择始终优先于西文字体的汉字字形。/ Creates a script-mapped app font so the CJK choice takes priority over Han glyphs in the Latin font.</summary>
    public static FontFamily CreateCompositeFont(AppearanceSettings appearance, string systemFontFamily)
    {
        var latin = appearance.ResolveLatinFontFamily(systemFontFamily);
        var cjk = appearance.ResolveCjkFontFamily(systemFontFamily);
        var fallback = appearance.ResolveFontFamilySource(systemFontFamily);
        var family = new FontFamily();
        family.FamilyNames.Add(XmlLanguage.GetLanguage("en-US"), "AF Media Bar text");
        var cjkFirstFallback = new[] { cjk, latin }
            .Concat(fallback.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = HanRanges, Target = string.Join(", ", cjkFirstFallback) });
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = AllCharacters, Target = fallback });
        return family;
    }

    private static bool SupportsScript(FontFamily family, bool cjk)
    {
        if (!CoverageCache.TryGetValue(family.Source, out var coverage))
        {
            coverage = (false, false);
            try
            {
                foreach (var typeface in family.GetTypefaces())
                {
                    if (!typeface.TryGetGlyphTypeface(out var glyphs)) continue;
                    coverage.Latin |= glyphs.CharacterToGlyphMap.ContainsKey('A') && glyphs.CharacterToGlyphMap.ContainsKey('a');
                    coverage.Cjk |= glyphs.CharacterToGlyphMap.ContainsKey('中') && glyphs.CharacterToGlyphMap.ContainsKey('文');
                    if (coverage.Latin && coverage.Cjk) break;
                }
            }
            catch (Exception) { /* An inaccessible or malformed installed font is not a selectable specimen. */ }
            CoverageCache[family.Source] = coverage;
        }
        return cjk ? coverage.Cjk : coverage.Latin;
    }

    private static string GetChineseDisplayName(FontFamily family)
    {
        var localized = family.FamilyNames
            .FirstOrDefault(pair => pair.Key.IetfLanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            .Value;
        if (string.IsNullOrWhiteSpace(localized))
            CommonChineseNames.TryGetValue(family.Source, out localized);
        return string.IsNullOrWhiteSpace(localized) ? family.Source : localized;
    }

    /// <summary>将已保存的字体名称映射到当前设备上的选项。/ Maps a saved family name to a choice installed on this device.</summary>
    public static string MatchSelection(string selectedName, IReadOnlyList<FontFamilyChoice> choices)
    {
        // The old Segoe preset contains two fallback names. Choose whichever is installed here.
        foreach (var name in selectedName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = choices.FirstOrDefault(choice =>
                string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Name;
        }

        return string.Empty;
    }
}
