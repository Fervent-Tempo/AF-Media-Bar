using System.Collections.Generic;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 设置窗口搜索框的静态索引。它是“搜索能搜到什么”的唯一事实来源：
/// 每条记录指向一个页面分组，分组序号必须与该页滚动内容里 <c>SettingsGroup</c> 的声明顺序一致。
///
/// 为什么用静态索引而不是在运行时遍历可视树：可视树只包含当前页，遍历它无法搜索其他页面；
/// 而把关键词写在页面里又会让“能被搜到”散落在六个页面。这里集中一处，并用单元测试盯住序号边界。
///
/// 条目里的标题与说明不写死，而是按当前界面语言取自文案表：分组标题复用页面上的 <c>Common.Group.*</c> 等
/// 共用键（两处引用同一个键，标题就不可能对不上），说明用本领域自己的 <c>Search.*</c> 键。因此语言一变，
/// 下一次读取 <see cref="Entries"/> 就是新语言的条目，而不是停在创建时的语言。
/// The static index behind the settings-window search box, and the single source of truth for what search can
/// find: every record names a page and a group index that must match the declaration order of the
/// <c>SettingsGroup</c> containers in that page's scroll content.
///
/// Why a static index rather than walking the visual tree: the visual tree only holds the current page, so
/// walking it cannot search the other five, and scattering keywords across the pages would spread
/// "findability" over six places. Keeping them together also lets a unit test guard the group bounds.
///
/// Titles and descriptions are not literals but read from the text table in the active interface language: a group
/// title reuses the shared <c>Common.Group.*</c> key its page uses (one key in both places, so the two can never
/// disagree) and a description uses this area's own <c>Search.*</c> key. A language switch therefore shows in the
/// very next read of <see cref="Entries"/> instead of leaving the entries in the language they were built in.
/// </summary>
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

    /// <summary>导航栏里的页面名称，与 <c>SettingsWindow</c> 的菜单项文案一致；按当前语言解析，因此与页面标题永远同步。/ Navigation labels, matching the menu items in <c>SettingsWindow</c>, resolved in the active language so they never drift from the page headers.</summary>
    public static string GetPageTitle(SettingsPageKey page) => Translations.Get(PageTitleKey(page));

    private static string PageTitleKey(SettingsPageKey page) => page switch
    {
        SettingsPageKey.DisplayModes => "Common.Page.DisplayModes",
        SettingsPageKey.MediaAndNotifications => "Common.Page.MediaAndNotifications",
        SettingsPageKey.Interaction => "Common.Page.Interaction",
        SettingsPageKey.Lyrics => "Common.Page.Lyrics",
        SettingsPageKey.Appearance => "Common.Page.Appearance",
        SettingsPageKey.Application => "Common.Page.Application",
        SettingsPageKey.About => "Common.Page.About",
        _ => string.Empty,
    };

    private static SettingsSearchEntry Create(
        SettingsPageKey page,
        int groupIndex,
        string titleKey,
        string descriptionKey,
        LocalizationLanguage language,
        string[] keywords) =>
        new(
            page,
            groupIndex,
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

    /// <summary>
    /// 模式选择分组：四种模式下它都可见，因此在索引里每种模式各占序号 0。
    /// 分组序号是「该模式下标签条的第几项」，而标签条永远从模式选择开始。
    /// The mode-picker group is visible in all four modes, so the index carries it at index 0 for each of them.
    /// A group index counts tabs within one mode, and the tabs always start with the picker.
    /// </summary>
    private static SettingsSearchEntry CreateModePicker(SettingsSearchMode mode, LocalizationLanguage language) =>
        Create(
            SettingsPageKey.DisplayModes,
            0,
            "Common.Group.ChooseDisplayMode",
            "Search.DisplayModes.ChooseDisplayMode.Description",
            language,
            ["模式", "mode", "显示模式", "承载模式", "灵动岛", "island", "桌面卡片", "悬浮球", "切换"]) with
        {
            Mode = mode,
        };

    /// <summary>
    /// 构建某一门语言的索引。分组顺序、分组序号与模式分区在这里固定下来，与 <c>SettingsGroup</c> 的声明顺序一致；
    /// 换语言只会换掉文案，不会换掉结构。
    /// Builds the index for one language. The group order, group indices, and mode sections are fixed here and follow
    /// the declaration order of the <c>SettingsGroup</c> containers; a language switch changes the text, never the shape.
    /// </summary>
    private static SettingsSearchEntry[] Build(LocalizationLanguage language) =>
    [
        // ---- 显示模式 · 模式选择（四种模式下都可见，因此每种模式各有一条，序号固定为 0）----
        // ---- Display modes, the mode picker, visible in all four modes, so every mode carries it at index 0 ----
        CreateModePicker(SettingsSearchMode.Taskbar, language),
        CreateModePicker(SettingsSearchMode.DynamicIsland, language),
        CreateModePicker(SettingsSearchMode.DesktopCard, language),
        CreateModePicker(SettingsSearchMode.FloatingBall, language),

        // ---- 显示模式 · 任务栏 / Display modes, taskbar ----
        Create(
            SettingsPageKey.DisplayModes,
            1,
            "Common.Group.ScreenAndPlacement",
            "Search.DisplayModes.ScreenAndPlacement.Description",
            language,
            ["显示器", "屏幕", "monitor", "display", "朝向", "横向", "纵向", "orientation", "避让", "图标", "锁定", "位置", "偏移", "offset", "重置", "承载", "承载显示器", "承载与位置", "边缘偏移", "排列方向"]),
        Create(
            SettingsPageKey.DisplayModes,
            2,
            "Common.Group.MediaBarWidth",
            "Search.DisplayModes.MediaBarWidth.Description",
            language,
            ["长度", "尺寸", "宽度", "间距", "spacing", "length", "固定", "跟随", "组件", "width", "固定长度", "组件间距"]),
        Create(
            SettingsPageKey.DisplayModes,
            3,
            "Common.RestLayer",
            "Search.DisplayModes.RestLayer.Description",
            language,
            ["静置", "常驻", "rest", "信息密度", "density", "精简", "均衡", "排列", "布局", "layout", "对齐", "标题", "歌手", "artist", "内容排列", "字号", "字体大小", "文字大小", "font size", "完整层入口", "横杆", "细杠", "进入完整层", "进度", "进度条", "播放进度", "progress"]),
        Create(
            SettingsPageKey.DisplayModes,
            4,
            "Common.HoverLayer",
            "Search.DisplayModes.HoverLayer.Description",
            language,
            ["悬停", "hover", "鼠标", "按钮", "播放暂停", "上一首", "下一首", "输出设备", "音量", "进度"]),
        Create(
            SettingsPageKey.DisplayModes,
            5,
            "Common.FullLayer",
            "Search.DisplayModes.FullLayer.Description",
            language,
            ["完整", "面板", "full", "panel", "预设", "preset", "分区", "媒体信息", "播放控制", "音频控制", "性能", "套用预设"]),

        // ---- 显示模式 · 灵动岛 / Display modes, dynamic island ----
        Create(
            SettingsPageKey.DisplayModes,
            1,
            "Common.Group.IslandAppearance",
            "Search.DisplayModes.IslandAppearance.Description",
            language,
            ["灵动岛", "island", "外观", "表面", "surface", "背景样式", "不透明度", "opacity", "圆角", "radius", "未实现", "灵动岛表面", "基础表面风格"]) with
        {
            Mode = SettingsSearchMode.DynamicIsland,
        },

        // ---- 媒体与通知 / Media and notifications ----
        Create(
            SettingsPageKey.MediaAndNotifications,
            0,
            "Common.MediaSource",
            "Search.MediaAndNotifications.MediaSource.Description",
            language,
            ["来源", "source", "SMTC", "允许", "过滤", "filter", "白名单", "应用", "播放器", "媒体来源", "已检测来源", "允许列表"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            1,
            "Common.Group.QuickLaunch",
            "Search.MediaAndNotifications.QuickLaunch.Description",
            language,
            ["快速启动", "quick", "launch", "启动", "音符", "播放器", "exe", "lnk", "浏览", "快捷方式"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            2,
            "Common.Group.RestLayerComponents",
            "Search.MediaAndNotifications.RestLayerComponents.Description",
            language,
            ["频谱", "spectrum", "均衡", "柱", "柱数", "刷新率", "灵敏度", "性能", "performance", "内存", "cpu", "gpu", "指标", "任务管理器", "柱子数量", "刷新速度", "跳动幅度", "样式", "波形", "波形图", "像素", "像素柱状图", "点阵", "对称", "上下对称", "采样间隔", "刷新间隔", "秒", "毫秒", "ms"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            3,
            "Common.Group.TrackChangeNotification",
            "Search.MediaAndNotifications.TrackChangeNotification.Description",
            language,
            ["通知", "notification", "切歌", "曲目", "track", "位置", "锚点", "停留", "时长", "duration", "全屏", "显示器", "停留时间", "目标显示器"]),

        // ---- 交互 / Interaction ----
        Create(
            SettingsPageKey.Interaction,
            0,
            "Common.Group.SharedModifier",
            "Search.Interaction.SharedModifier.Description",
            language,
            ["修饰键", "modifier", "shift", "滚轮", "wheel", "组合", "chord", "左键", "右键", "共用修饰键", "按键"]),
        Create(
            SettingsPageKey.Interaction,
            1,
            "Common.Group.InAppRestLayer",
            "Search.Interaction.InAppRestLayer.Description",
            language,
            ["点击", "click", "封面", "artwork", "标题", "歌词", "程序内", "绑定", "上一首", "下一首", "设备", "音量", "媒体源", "普通滚轮", "组合滚轮", "完整层", "打开完整层", "面板"]),
        Create(
            SettingsPageKey.Interaction,
            2,
            "Common.TrayIcon",
            "Search.Interaction.TrayIcon.Description",
            language,
            ["托盘", "tray", "通知区域", "溢出", "菜单", "音量", "设置", "设备", "输出设备菜单", "应用音量菜单", "当前应用音量"]),

        // ---- 歌词 / Lyrics ----
        Create(
            SettingsPageKey.Lyrics,
            0,
            "Common.Group.LyricsDisplay",
            "Search.Lyrics.Display.Description",
            language,
            ["歌词", "lyrics", "实时", "双行", "第二行", "翻译", "音译", "下一句", "translation", "romanization", "逐字", "擦亮", "亮起", "karaoke", "署名", "作词", "作曲", "实时歌词", "双行歌词"]),
        Create(
            SettingsPageKey.Lyrics,
            1,
            "Common.Group.LyricsAlignment",
            "Search.Lyrics.Alignment.Description",
            language,
            ["对齐", "align", "左", "中", "右", "居中", "left", "center", "right"]),
        Create(
            SettingsPageKey.Lyrics,
            2,
            "Common.Group.LyricsSources",
            "Search.Lyrics.Sources.Description",
            language,
            ["来源", "歌词来源", "source", "sources", "网易云", "网易云音乐", "netease", "qq 音乐", "qqmusic", "酷狗", "kugou", "汽水", "soda", "lrclib", "搜索", "search", "匹配", "严格", "match", "strict", "顺序", "优先级", "priority", "order"]),

        // ---- 外观 / Appearance ----
        Create(
            SettingsPageKey.Appearance,
            0,
            "Common.Group.Fonts",
            "Search.Appearance.Fonts.Description",
            language,
            ["字体", "font", "字重", "weight", "粗细", "西文", "英文", "中文", "本机字体", "已安装字体", "installed fonts", "预览", "preview", "segoe", "雅黑", "字体粗细"]),
        Create(
            SettingsPageKey.Appearance,
            1,
            "Common.Group.ThemeAndBackdrop",
            "Search.Appearance.ThemeAndBackdrop.Description",
            language,
            ["主题", "theme", "浅色", "深色", "light", "dark", "材质", "backdrop", "mica", "云母", "acrylic", "亚克力", "动效", "motion", "动画", "背景材质", "交互动效"]),
        Create(
            SettingsPageKey.Appearance,
            2,
            "Common.Group.MediaBarText",
            "Search.Appearance.MediaBarText.Description",
            language,
            ["文字颜色", "foreground", "文字", "颜色", "自动", "浅色文字", "深色文字", "对比", "可读", "播放器文字", "媒体文字大小", "字号", "文字大小", "font size", "缩放"]),

        // ---- 应用 / Application ----
        //
        // 这一页原来是「应用与关于」的前半部分，拆分后只保留"应用自身的设置"：版本与更新、开机自启、界面语言、
        // 默认设置、设置文件与诊断日志。开发人员、赞助与开源许可移到了页脚的「关于」，因此那里的 key 不再指向本页。
        // This page is the first half of what used to be "application and about"; after the split it keeps only the application's own
        // settings: version and updates, run-at-startup, interface language, user defaults, the settings file, and diagnostics.
        // Developers, sponsors, and open-source licenses moved to "about" in the footer, so their keys no longer point here.
        Create(
            SettingsPageKey.Application,
            0,
            "Common.Group.Application",
            "Search.Application.Application.Description",
            language,
            ["更新", "update", "升级", "版本", "version", "检查更新", "自动更新", "自动下载", "下载", "安装", "安装程序", "静默安装", "重启", "加速", "镜像", "跳过此版本", "开机", "启动", "startup", "自启", "语言", "language", "中文", "预留"]),
        Create(
            SettingsPageKey.Application,
            1,
            "Common.Group.SettingsFile",
            "Search.Application.SettingsFile.Description",
            language,
            ["设置文件", "settings.json", "文件夹", "folder", "打开", "重置", "reset", "恢复默认", "还原"]),
        Create(
            SettingsPageKey.Application,
            2,
            "Common.Group.Diagnostics",
            "Search.Application.Diagnostics.Description",
            language,
            ["日志", "log", "logs", "诊断", "diagnostics", "报错", "崩溃", "crash", "上报", "报告", "排查", "打开文件夹", "导出", "内存", "memory", "ram", "占用", "压缩", "释放", "工作集"]),

        // ---- 关于 / About ----
        Create(
            SettingsPageKey.About,
            0,
            "Common.Group.Developers",
            "Search.About.Developers.Description",
            language,
            ["开发", "developer", "贡献者", "contributor", "名单", "人员", "github", "感谢", "credits"]),
        Create(
            SettingsPageKey.About,
            1,
            "Common.Group.Sponsors",
            "Search.About.Sponsors.Description",
            language,
            ["赞助", "sponsor", "支持", "捐赠", "donate", "名单", "感谢"]),
        Create(
            SettingsPageKey.About,
            2,
            "Common.Group.Support",
            "Search.About.Support.Description",
            language,
            ["赞助我", "请我喝咖啡", "打赏", "二维码", "微信", "wechat", "支付宝", "alipay", "爱发电", "afdian", "支持"]),
        Create(
            SettingsPageKey.About,
            3,
            "Common.Group.Licenses",
            "Search.About.Licenses.Description",
            language,
            ["开源", "许可", "license", "licence", "许可证", "第三方", "依赖", "package", "apache", "mit", "gpl"]),
        Create(
            SettingsPageKey.About,
            4,
            "Common.Group.ProjectInfo",
            "Search.About.ProjectInfo.Description",
            language,
            ["版本", "version", "关于", "about", "github", "反馈", "issue", "star", "仓库", "repository"]),
    ];
}
