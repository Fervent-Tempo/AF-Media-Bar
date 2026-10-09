using AFMediaBar.Classes.Services.Localization;
using System.Collections;
using System.Globalization;
using System.Resources;

namespace AFMediaBar.Resources;

/// <summary>
/// 界面文案的唯一权威。
///
/// 界面有三种取值入口，但它们读的是同一张表，因此不可能出现"设置页已翻译、托盘仍是旧语言"这种分裂：
/// 1. XAML 通过 <c>{DynamicResource Loc.&lt;键&gt;}</c> 取值，键由 <c>LocalizationService</c> 在切换语言时整批重发；
/// 2. 代码通过 <see cref="Get(string)"/> / <see cref="Format(string, object?[])"/> 取值；
/// 3. 纯策略与可复用控件同样用第 2 条——它们拿不到依赖注入，而当前语言是进程级状态（与
///    <see cref="CultureInfo.CurrentUICulture"/> 同类），因此这里是有意的静态入口，唯一写入方是
///    <c>LocalizationService</c>。
///
/// 文案按语言放在 <c>Resources/Strings*.resx</c>，作为三个独立的中性资源嵌入主程序集，不产生卫星程序集。
/// <see cref="BuildTable"/> 合并三份资源；单条译文缺失时回退简体中文。
/// The single authority for interface text.
///
/// The interface reads it through three entries that all resolve against one table, so a split such as "the settings page
/// is translated while the tray still speaks the old language" cannot happen:
/// 1. XAML reads <c>{DynamicResource Loc.&lt;key&gt;}</c>, and <c>LocalizationService</c> republishes every key when the
///    language changes;
/// 2. code reads <see cref="Get(string)"/> or <see cref="Format(string, object?[])"/>;
/// 3. pure policies and reusable controls also use the second entry — they cannot receive dependency injection, and the
///    active language is process-wide state of the same kind as <see cref="CultureInfo.CurrentUICulture"/>. This static
///    entry is therefore deliberate, and <c>LocalizationService</c> is its only writer.
///
/// Text lives in one <c>Resources/Strings*.resx</c> file per language. The three neutral resources are embedded in the main
/// assembly, avoiding satellite assemblies in the single-file release. <see cref="BuildTable"/> merges them, and a missing
/// translation falls back to simplified Chinese.
/// </summary>
public static class Translations
{
    private static readonly Dictionary<string, LocalizedText> Table = BuildTable();
    private static LocalizationLanguage _activeLanguage = LocalizationLanguage.SimplifiedChinese;

    /// <summary>当前生效的界面语言。/ The interface language currently in effect.</summary>
    public static LocalizationLanguage ActiveLanguage => _activeLanguage;

    /// <summary>已登记的文案键；切换语言时按它整批重发 XAML 资源。/ Every registered text key, which the language switch republishes into the XAML resources.</summary>
    public static IReadOnlyCollection<string> Keys => Table.Keys;

    /// <summary>已登记的文案条数。/ Number of registered entries.</summary>
    public static int Count => Table.Count;

    /// <summary>
    /// 按当前语言取一条文案。键不存在时返回键本身：缺失的文案必须在界面上一眼可见，而不是渲染成空白，
    /// 也不是悄悄回退成另一种语言。
    /// Looks one string up in the active language. A missing key returns the key itself: a missing string has to be
    /// visible in the interface at a glance rather than rendered as blank or silently replaced by another language.
    /// </summary>
    /// <param name="key">文案键。/ Text key.</param>
    public static string Get(string key) => Get(key, _activeLanguage);

    /// <summary>按指定语言取一条文案，供测试与解析验证使用。/ Looks a string up in a given language, which tests and resolution checks use.</summary>
    /// <param name="key">文案键。/ Text key.</param>
    /// <param name="language">目标语言。/ Target language.</param>
    public static string Get(string key, LocalizationLanguage language)
    {
        if (string.IsNullOrEmpty(key) || !Table.TryGetValue(key, out var text))
        {
            return key ?? string.Empty;
        }

        var value = language switch
        {
            LocalizationLanguage.TraditionalChinese => text.TraditionalChinese,
            LocalizationLanguage.English => text.English,
            LocalizationLanguage.Vietnamese => text.Vietnamese,
            _ => text.SimplifiedChinese,
        };

        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        // 某一语言缺这条文案时回退到简体中文，而不是在界面上留下空白；简体中文也缺时
        // 时返回键本身，让缺的那一条在界面上一眼可见。
        // A language missing this entry falls back to simplified Chinese instead of leaving a blank, and when simplified
        // Chinese is missing too, the key itself is
        // returned so the gap is visible in the interface.
        return string.IsNullOrEmpty(text.SimplifiedChinese) ? key : text.SimplifiedChinese;
    }

    /// <summary>
    /// 取一条带参数的文案：文案本身在表里写成 <c>{0}</c> 形式，参数在显示时才填入，因此切换语言后同一条状态
    /// 会连同参数一起换成新语言。
    /// Looks up a string with arguments: the text itself is stored in <c>{0}</c> form and the arguments are filled in at
    /// display time, so the same status turns into the new language together with its arguments after a switch.
    /// </summary>
    /// <param name="key">文案键。/ Text key.</param>
    /// <param name="arguments">填入文案的参数。/ Arguments filled into the text.</param>
    public static string Format(string key, params object?[] arguments) =>
        arguments is { Length: > 0 }
            ? string.Format(CultureInfo.CurrentCulture, Get(key), arguments)
            : Get(key);

    /// <summary>
    /// 生效语言变化时发布。它保持 internal：界面代码走 <c>LocalizationService.LanguageChanged</c>（依赖注入的那条路径），
    /// 而这个事件是给**拿不到依赖注入的可复用控件**用的——例如分组标签条把分组标题快照成了自己的标签文本，
    /// 不重建就会在切换语言后一直显示旧语言。
    /// Published when the language in effect changes. It stays internal: interface code uses
    /// <c>LocalizationService.LanguageChanged</c>, the path that comes from dependency injection, while this event exists for
    /// reusable controls that cannot receive injection — the group strip, for example, snapshots group headers into its own tab
    /// text and would keep showing the old language without a rebuild.
    /// </summary>
    internal static event EventHandler? LanguageChanged;

    /// <summary>
    /// 设置当前语言。它只允许 <c>LocalizationService</c> 调用，并且必须与 <see cref="RaiseLanguageChanged"/> 配对：
    /// 服务先写语言、再重发 XAML 资源，最后才通知订阅者，因为订阅者（例如分组标签条）要读的是**已经换过**的
    /// 动态资源值，反过来的顺序会让它们把旧语言重新快照一遍。
    /// Sets the active language. Only <c>LocalizationService</c> may call it, and it has to be paired with
    /// <see cref="RaiseLanguageChanged"/>: the service writes the language, republishes the XAML resources, and only then
    /// notifies subscribers, because a subscriber such as the group strip reads the *already changed* dynamic-resource values
    /// and the opposite order would make it snapshot the old language once more.
    /// </summary>
    /// <param name="language">新的生效语言。/ The new active language.</param>
    internal static void SetActiveLanguage(LocalizationLanguage language) => _activeLanguage = language;

    /// <summary>通知语言变化的订阅者；只由 <c>LocalizationService</c> 在资源重发之后调用。/ Notifies the language-change subscribers; only <c>LocalizationService</c> calls it, after the resources were republished.</summary>
    internal static void RaiseLanguageChanged() => LanguageChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>
    /// 把三份 resx 资源合并成一张"键 → 三种语言"的表。
    ///
    /// 键取三份文件的并集并按序排列：只有这样"某一语言漏了一条"才是可表示的（那一份取空值、取值时回退简体中文、
    /// 由完整性测试判失败），而不是让静态初始化直接崩掉。
    /// Merges the three resx resources into one "key to three languages" table.
    ///
    /// The keys are the union of the three files, in order: that is what makes "one language is missing an entry" representable
    /// — the missing language reads as empty and falls back to simplified Chinese at lookup time. Completeness tests flag it.
    /// </summary>
    private static Dictionary<string, LocalizedText> BuildTable()
    {
        var simplifiedChinese = Load("StringsZhHans");
        var traditionalChinese = Load("StringsZhHant");
        var english = Load("StringsEn");
        var vietnamese = Load("StringsVi");

        var keys = new SortedSet<string>(simplifiedChinese.Keys, StringComparer.Ordinal);
        keys.UnionWith(traditionalChinese.Keys);
        keys.UnionWith(english.Keys);
        keys.UnionWith(vietnamese.Keys);

        var table = new Dictionary<string, LocalizedText>(keys.Count, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            table[key] = new LocalizedText(
                simplifiedChinese.GetValueOrDefault(key, string.Empty),
                traditionalChinese.GetValueOrDefault(key, string.Empty),
                english.GetValueOrDefault(key, string.Empty),
                vietnamese.GetValueOrDefault(key, string.Empty));
        }

        return table;
    }

    /// <summary>从主程序集读取一份中性 resx 资源；缺失时留空，供取值回退。/ Loads one neutral resx from the main assembly, leaving a missing resource empty for lookup fallback.</summary>
    /// <param name="name">资源文件名，不含扩展名。/ Resource filename without its extension.</param>
    private static IReadOnlyDictionary<string, string> Load(string name)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var manager = new ResourceManager($"AFMediaBar.Resources.{name}", typeof(Translations).Assembly);
        ResourceSet? resources;
        try
        {
            resources = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        }
        catch (MissingManifestResourceException)
        {
            return result;
        }

        if (resources is null)
            return result;

        foreach (DictionaryEntry entry in resources)
        {
            if (entry.Key is string key && entry.Value is string value && !string.IsNullOrEmpty(value))
                result.Add(key, value);
        }

        return result;
    }
}

/// <summary>一条界面文案的四种语言取值。/ The four language values of one interface string.</summary>
/// <param name="SimplifiedChinese">简体中文，兼作回退。/ Simplified Chinese, also the fallback.</param>
/// <param name="TraditionalChinese">繁体中文。/ Traditional Chinese.</param>
/// <param name="English">英文。/ English.</param>
/// <param name="Vietnamese">越南语。/ Vietnamese.</param>
internal readonly record struct LocalizedText(string SimplifiedChinese, string TraditionalChinese, string English, string Vietnamese);
