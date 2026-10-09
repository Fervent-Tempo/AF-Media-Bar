using System.Collections.Generic;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>Localized search groups with stable identities; available destinations follow the page catalog and environment.</summary>
public static class SettingsSearchIndex
{
    // 条目按语言缓存：语言没变就复用上一次构建的结果，语言变了就在这一次读取时重建。
    // 索引只在用户往搜索框里输入时被读到，因此用一把锁换取"读到的条目一定属于同一门语言"完全够用，
    // 不值得为这点频率写无锁版本。
    // Entries are cached per language: an unchanged language reuses the previous build, a changed one rebuilds on
    // that very read. The index is only read while the user types in the search box, so one lock is a cheap way to
    // guarantee that the entries read belong to one and the same language, and there is nothing here worth a
    // lock-free version.
    private static readonly object CacheGate = new();
    private static LocalizationLanguage _cachedLanguage;
    private static SettingsSearchEntry[]? _cachedEntries;

    /// <summary>三种具体语言，按固定顺序排列：附加关键词时每种语言都要各取一份标题与说明。/ The three concrete languages in a fixed order, because each of them contributes the title and the description as keywords.</summary>
    private static readonly LocalizationLanguage[] AllLanguages =
    [
        LocalizationLanguage.SimplifiedChinese,
        LocalizationLanguage.TraditionalChinese,
        LocalizationLanguage.English,
        LocalizationLanguage.Vietnamese,
    ];

    /// <summary>
    /// 当前语言下的全部可搜索分组。取的是“读的那一刻”的语言，因此切换语言之后不需要任何通知或重建调用：
    /// 下一次读取这里就会拿到新语言的标题、说明与关键词。
    /// Every searchable group in the active language, resolved at read time, so a language switch needs neither a
    /// notification nor a rebuild call: the next read of this property returns the new language's titles,
    /// descriptions, and keywords.
    /// </summary>
    public static IReadOnlyList<SettingsSearchEntry> Entries
    {
        get
        {
            lock (CacheGate)
            {
                var language = Translations.ActiveLanguage;
                var entries = _cachedEntries;
                if (entries is null || _cachedLanguage != language)
                {
                    entries = Build(language);
                    _cachedLanguage = language;
                    _cachedEntries = entries;
                }

                return entries;
            }
        }
    }

    /// <summary>Only searches the current family and common pages, skipping orientation-specific hidden groups.</summary>
    public static IReadOnlyList<SettingsSearchEntry> ForContext(SettingsContext context) => Entries.Where(entry =>
        SettingsPageCatalog.Find(entry.Page, context.Mode) is not null &&
        (entry.Page != SettingsPageKey.ScreenAndPlacement || context.Mode == SettingsMode.Taskbar || entry.GroupId == "Common.Group.ChooseDisplayMode") &&
        (entry.Page != SettingsPageKey.Appearance || context.Mode == SettingsMode.Taskbar ||
            entry.GroupId is "Common.Group.ThemeAndBackdrop" or "Common.Group.Fonts"))
        .Select(entry => ForOrientation(entry, context)).ToArray();

    private static SettingsSearchEntry ForOrientation(SettingsSearchEntry entry, SettingsContext context)
    {
        if (context.Orientation != LayoutOrientation.Vertical) return entry;
        var (description, hiddenTerms) = (entry.Page, entry.GroupId) switch
        {
            (SettingsPageKey.Components, "Common.RestLayer") => ("Search.ContentLayout.Vertical.Rest", new[] { "顺序", "順序", "排序", "order", "sort", "上移", "下移" }),
            (SettingsPageKey.Components, "Appearance.Group.RestLayout") => ("Search.ContentLayout.Vertical.Text", new[] { "标题", "標題", "歌手", "artist", "title", "排列", "排布", "展開", "展开", "direction", "rest layout" }),
            (SettingsPageKey.ScreenAndPlacement, "Common.Group.ScreenAndPlacement") => ("Search.Placement.Vertical", new[] { "靠左", "靠右", "居中", "置中", "对齐", "對齊", "align", "left", "right", "center" }),
            _ => (string.Empty, Array.Empty<string>())
        };
        return hiddenTerms.Length == 0 ? entry : entry with
        {
            Description = Translations.Get(description),
            Keywords = entry.Keywords.Where(keyword => !hiddenTerms.Any(term => keyword.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray()
        };
    }

    /// <summary>导航栏里的页面名称，与 <c>SettingsWindow</c> 的菜单项文案一致；按当前语言解析，因此与页面标题永远同步。/ Navigation labels, matching the menu items in <c>SettingsWindow</c>, resolved in the active language so they never drift from the page headers.</summary>
    public static string GetPageTitle(SettingsPageKey page) => Translations.Get(PageTitleKey(page));

    private static string PageTitleKey(SettingsPageKey page) => SettingsPageCatalog.Find(
        SettingsPageCatalog.ResolveDestination(page, string.Empty).Page, SettingsMode.Taskbar)?.TitleKey ?? string.Empty;

    private static SettingsSearchEntry Create(
        SettingsPageKey page,
        string titleKey,
        string descriptionKey,
        LocalizationLanguage language,
        string[] keywords) =>
        new(
            page,
            titleKey,
            Translations.Get(PageTitleKey(page), language),
            Translations.Get(titleKey, language),
            Translations.Get(descriptionKey, language),
            ComposeKeywords(titleKey, descriptionKey, keywords));

    /// <summary>
    /// 关键词表：手工同义词在前，三种语言的分组标题与说明在后。
    ///
    /// 手工同义词是维护者写的、界面从未用过的说法（旧术语、英文标识、选项名），删掉它们等于删掉搜索能力；
    /// 附加三种语言的标题与说明则让用户在任何界面语言下都能用另一种语言的词搜到——中文界面下输入
    /// <c>rest layer</c>，或英文界面下输入「静置层」，都要命中同一个分组。
    /// The keyword list: the hand-written synonyms first, then the group title and description in all three languages.
    ///
    /// The synonyms are wording the interface never uses (retired terms, English identifiers, option names), and
    /// dropping them would drop search capability. Appending the three languages' titles and descriptions lets a user
    /// search in a language other than the interface's own: typing <c>rest layer</c> under a Chinese interface, or
    /// 「静置层」 under an English one, has to reach the same group.
    /// </summary>
    private static string[] ComposeKeywords(string titleKey, string descriptionKey, string[] synonyms)
    {
        // 空串永远不进入关键词表：搜索策略虽然会跳过空串，但索引自己也不该留下"一项什么都没有"的关键词。
        // An empty string never enters the list: the search policy skips empty keywords, but the index itself should
        // not carry a keyword that says nothing either.
        var keywords = new List<string>(synonyms.Length + (AllLanguages.Length * 2));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var synonym in synonyms)
        {
            Add(synonym);
        }

        foreach (var language in AllLanguages)
        {
            Add(Translations.Get(titleKey, language));
            Add(Translations.Get(descriptionKey, language));
        }

        return keywords.ToArray();

        void Add(string? candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                keywords.Add(candidate);
            }
        }
    }

    /// <summary>Common mode-choice destination; unsupported families have no editable search groups.</summary>
    private static SettingsSearchEntry CreateModePicker(LocalizationLanguage language) =>
        Create(
            SettingsPageKey.ScreenAndPlacement,
            "Common.Group.ChooseDisplayMode",
            "Search.DisplayModes.ChooseDisplayMode.Description",
            language,
            ["模式", "mode", "显示模式", "承载模式", "灵动岛", "island", "桌面卡片", "悬浮球", "切换"]);

    /// <summary>Builds localized labels while keeping page and group identities stable.</summary>
    private static SettingsSearchEntry[] Build(LocalizationLanguage language) =>
    [
        Create(
            SettingsPageKey.ReleaseHighlights,
            "ReleaseHighlights.Latest",
            "ReleaseHighlights.Header.Subtitle",
            language,
            ["更新亮点", "release notes", "what's new"]),
        Create(
            SettingsPageKey.ReleaseHighlights,
            "ReleaseHighlights.History",
            "ReleaseHighlights.Application.Description",
            language,
            ["历史版本", "version history"]),
        // Common mode choice.
        CreateModePicker(language),

        // ---- 显示模式 · 任务栏 / Display modes, taskbar ----
        Create(
            SettingsPageKey.ScreenAndPlacement,
            "Common.Group.ScreenAndPlacement",
            "Search.DisplayModes.ScreenAndPlacement.Description",
            language,
            ["显示器", "屏幕", "monitor", "display", "朝向", "横向", "纵向", "orientation", "避让", "图标", "锁定", "位置", "偏移", "厚度方向偏移", "offset", "重置", "承载", "承载显示器", "承载与位置", "边缘偏移", "靠左", "靠右", "居中", "置中", "对齐", "對齊", "alignment", "left", "right", "center"]),
        Create(
            SettingsPageKey.Components,
            "Common.RestLayer",
            "Search.DisplayModes.RestLayer.Description",
            language,
            ["静置", "静置层", "靜置層", "常驻", "常驻状态", "常駐狀態", "rest", "rest layer", "always-on view", "进度", "播放进度", "progress", "设备按钮", "输出设备", "音量按钮", "没有媒体", "无媒体", "空闲", "idle", "隐藏", "保留组件", "小音符", "组件设置", "components", "频谱", "spectrum", "柱数", "刷新率", "灵敏度", "性能", "performance", "内存", "cpu", "gpu", "任务管理器", "波形", "像素", "上下对称", "采样间隔", "刷新间隔", "顺序", "排序", "order", "组件", "上移", "下移", "封面放大", "artwork zoom"]),
        Create(
            SettingsPageKey.Components,
            "Common.HoverLayer",
            "Search.DisplayModes.HoverLayer.Description",
            language,
            ["悬停", "悬停层", "懸停層", "快捷控制", "hover", "hover layer", "quick controls", "鼠标", "按钮", "播放暂停", "上一首", "下一首", "输出设备", "音量", "进度"]),
        Create(
            SettingsPageKey.Components,
            "Common.FullLayer",
            "Search.DisplayModes.FullLayer.Description",
            language,
            ["完整", "完整层", "完整層", "完整面板", "面板", "full", "full layer", "panel", "预设", "preset", "分区", "媒体信息", "播放控制", "音频控制", "性能", "套用预设", "完整层入口", "横杆", "细杠", "进入完整层"]),

        // ---- 媒体与通知 / Media and notifications ----
        Create(
            SettingsPageKey.MediaAndNotifications,
            "Common.MediaSource",
            "Search.MediaAndNotifications.MediaSource.Description",
            language,
            ["来源", "source", "SMTC", "允许", "过滤", "filter", "白名单", "应用", "播放器", "媒体来源", "已检测来源", "允许列表"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            "Common.Group.QuickLaunch",
            "Search.MediaAndNotifications.QuickLaunch.Description",
            language,
            ["快速启动", "quick", "launch", "启动", "音符", "播放器", "exe", "lnk", "浏览", "快捷方式"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            "Common.Group.TrackChangeNotification",
            "Search.MediaAndNotifications.TrackChangeNotification.Description",
            language,
            ["通知", "notification", "切歌", "曲目", "track", "位置", "锚点", "停留", "时长", "duration", "全屏", "显示器", "停留时间", "目标显示器"]),

        // ---- 交互 / Interaction ----
        Create(
            SettingsPageKey.Interaction,
            "Interaction.Group.Wheel",
            "Search.Interaction.SharedModifier.Description",
            language,
            ["修饰键", "modifier", "shift", "滚轮", "wheel", "组合", "chord", "左键", "右键", "共用修饰键", "按键", "普通滚轮", "组合滚轮", "切换播放器"]),
        Create(
            SettingsPageKey.Interaction,
            "Common.Group.InAppRestLayer",
            "Search.Interaction.InAppRestLayer.Description",
            language,
            ["点击", "click", "封面", "artwork", "标题", "歌词", "程序内", "绑定", "完整层", "打开完整层", "面板"]),
        Create(
            SettingsPageKey.Interaction,
            "Common.TrayIcon",
            "Search.Interaction.TrayIcon.Description",
            language,
            ["托盘", "tray", "通知区域", "溢出", "菜单", "音量", "设置", "设备", "输出设备菜单", "应用音量菜单", "当前应用音量"]),

        // ---- 歌词 / Lyrics ----
        Create(
            SettingsPageKey.Lyrics,
            "Common.Group.LyricsDisplay",
            "Search.Lyrics.Display.Description",
            language,
            ["歌词", "lyrics", "实时", "双行", "第二行", "翻译", "音译", "下一句", "translation", "romanization", "实时歌词", "双行歌词", "优先级", "回退"]),
        Create(
            SettingsPageKey.Lyrics,
            "Lyrics.Group.Presentation",
            "Search.Lyrics.Presentation.Description",
            language,
            ["逐字", "擦亮", "karaoke", "署名", "作词", "作曲", "透明度", "行距", "字距", "字间距", "spacing", "line gap", "character spacing", "繁简", "简繁", "繁体", "简体", "conversion", "traditional", "simplified"]),
        Create(
            SettingsPageKey.Lyrics,
            "Common.Group.LyricsSources",
            "Search.Lyrics.Sources.Description",
            language,
            ["来源", "歌词来源", "source", "sources", "网易云", "网易云音乐", "netease", "qq 音乐", "qqmusic", "酷狗", "kugou", "汽水", "soda", "lrclib", "搜索", "search", "匹配", "match", "艺术家分隔符", "artist separators"]),

        // ---- 外观 / Appearance ----
        Create(SettingsPageKey.Appearance, "Appearance.Group.TaskbarBackground", "Appearance.Row.TaskbarBackground.Description", language,
            ["背景层", "磨砂", "透明任务栏", "frost", "background", "TranslucentTB", "浓度", "opacity"]),
        Create(
            SettingsPageKey.Appearance,
            "Common.Group.ThemeAndBackdrop",
            "Search.Appearance.ThemeAndBackdrop.Description",
            language,
            ["应用外观", "application appearance", "主题", "theme", "浅色", "深色", "light", "dark", "材质", "backdrop", "mica", "云母", "acrylic", "亚克力", "动效", "motion", "动画", "背景材质", "交互动效"]),
        Create(
            SettingsPageKey.Appearance,
            "Common.Group.MediaBarText",
            "Search.Appearance.MediaBarText.Description",
            language,
            ["媒体栏外观", "taskbar appearance", "文字颜色", "foreground", "文字", "颜色", "自动", "浅色文字", "深色文字", "对比", "可读", "播放器文字", "媒体文字大小", "字号", "文字大小", "font size", "缩放"]),
        Create(
            SettingsPageKey.Components,
            "Common.Group.MediaBarWidth",
            "Search.Appearance.MediaBarWidth.Description",
            language,
            ["长度", "尺寸", "宽度", "间距", "spacing", "length", "固定", "跟随", "组件", "width", "固定长度", "组件间距", "歌词框", "歌词框长度", "歌词区域", "fixed width", "lyric box"]),
        Create(
            SettingsPageKey.Components,
            "Appearance.Group.RestLayout",
            "Search.Appearance.RestLayout.Description",
            language,
            ["静置层外观", "rest layout", "排列", "布局", "layout", "对齐", "标题", "歌手", "artist", "内容排列", "歌词对齐", "lyric alignment", "居中", "left", "center", "right", "展开方向", "左右排布"]),
        Create(
            SettingsPageKey.Components,
            "Appearance.Group.InteractionButtons",
            "Search.Appearance.InteractionButtons.Description",
            language,
            ["交互按钮大小", "按钮尺寸", "小", "中", "大", "hover button size", "button spacing", "悬停层间距", "静置层按钮", "图标大小"]),

        Create(SettingsPageKey.Appearance, "Common.Group.Fonts", "Search.Appearance.Fonts.Description", language,
            ["字体", "font", "字重", "weight", "西文", "中文", "预览", "segoe", "雅黑"]),
        // ---- 应用 / Application ----
        //
        // 这一页原来是「应用与关于」的前半部分，拆分后只保留"应用自身的设置"：版本与更新、开机自启、界面语言、
        // 默认设置、设置文件与诊断日志。开发人员、赞助与开源许可移到了页脚的「关于」，因此那里的 key 不再指向本页。
        // This page is the first half of what used to be "application and about"; after the split it keeps only the application's own
        // settings: version and updates, run-at-startup, interface language, user defaults, the settings file, and diagnostics.
        // Developers, sponsors, and open-source licenses moved to "about" in the footer, so their keys no longer point here.
        Create(
            SettingsPageKey.Application,
            "Common.Group.Application",
            "Search.Application.Application.Description",
            language,
            ["开机", "启动", "startup", "自启", "语言", "language", "中文"]),
        Create(SettingsPageKey.Application, "Application.Group.Updates", "Search.Application.Updates.Description", language,
            ["更新", "update", "升级", "版本", "version", "检查更新", "自动更新", "下载", "安装", "安装程序", "重启", "加速", "镜像", "跳过此版本"]),
        Create(
            SettingsPageKey.Application,
            "Common.Group.SettingsFile",
            "Search.Application.SettingsFile.Description",
            language,
            ["设置文件", "settings.json", "文件夹", "folder", "打开", "重置", "reset", "恢复默认", "还原"]),
        Create(
            SettingsPageKey.Application,
            "Common.Group.Diagnostics",
            "Search.Application.Diagnostics.Description",
            language,
            ["日志", "log", "logs", "诊断", "diagnostics", "报错", "崩溃", "crash", "上报", "报告", "排查", "打开文件夹", "导出", "内存", "memory", "ram", "占用", "压缩", "释放", "工作集"]),

        // ---- 关于 / About ----
        Create(
            SettingsPageKey.About,
            "Common.Group.ProjectInfo",
            "Search.About.ProjectInfo.Description",
            language,
            ["版本", "version", "关于", "about", "github", "反馈", "issue", "star", "仓库", "repository"]),
        Create(
            SettingsPageKey.About,
            "Common.Group.Developers",
            "Search.About.Developers.Description",
            language,
            ["开发", "developer", "贡献者", "contributor", "名单", "人员", "github", "感谢", "credits"]),
        Create(
            SettingsPageKey.About,
            "Common.Group.Sponsors",
            "Search.About.Sponsors.Description",
            language,
            ["赞助", "sponsor", "支持", "捐赠", "donate", "名单", "感谢"]),
        Create(
            SettingsPageKey.About,
            "Common.Group.Support",
            "Search.About.Support.Description",
            language,
            ["赞助我", "请我喝咖啡", "打赏", "二维码", "微信", "wechat", "支付宝", "alipay", "支持"]),
        Create(
            SettingsPageKey.About,
            "Common.Group.Licenses",
            "Search.About.Licenses.Description",
            language,
            ["开源", "许可", "license", "licence", "许可证", "第三方", "依赖", "package", "apache", "mit", "gpl"]),
    ];
}
