namespace AFMediaBar.Models;

/// <summary>
/// 西文字体预设，负责英文曲名、歌手名、数字等拉丁字符的显示。
/// 目标用户均为中国大陆简体中文版 Windows，因此只包含系统自带、无需额外安装的字体。
/// </summary>
internal enum LatinFontPreset
{
    SegoeUi = 0,       // Segoe UI Variable Text，Win11 默认西文
    Arial = 1,         // 经典通用无衬线
    Calibri = 2,       // Win8+ 内置，现代圆润
    Verdana = 3,       // 宽字面，小字号可读性好
    Consolas = 4,      // 等宽，适合数字与时间
    TimesNewRoman = 5, // 传统衬线
    FollowCjk = 6      // 跟随中文字体（链中省略西文，使用中文字体自带的西文字形）
}

/// <summary>
/// 中文字体预设，负责中文标题与说明文字的显示。
/// </summary>
internal enum CjkFontPreset
{
    SystemDefault = 0, // 不指定中文，由 WPF 全局回退
    MicrosoftYaHei = 1,
    DengXian = 2,
    SimSun = 3,
    SimHei = 4,
    KaiTi = 5,
    FangSong = 6
}

internal enum PlayerFontWeightPreset
{
    Light = 0,
    Standard = 1,
    Bold = 2
}

internal readonly record struct FontSettings(
    LatinFontPreset Latin,
    CjkFontPreset Cjk,
    int Weight)
{
    internal const int MinWeight = 300;
    internal const int MaxWeight = 900;
    internal const int DefaultWeight = 600;

    /// <summary>
    /// 默认组合：西文 Segoe UI + 中文微软雅黑，中西文混排观感最接近 Windows 原生。
    /// 图标字体（AppIconFontFamily）不参与预设，始终为 Segoe Fluent Icons。
    /// </summary>
    internal static FontSettings Default { get; } = new(
        LatinFontPreset.SegoeUi,
        CjkFontPreset.MicrosoftYaHei,
        DefaultWeight);

    internal static int NormalizeWeight(int weight) =>
        Math.Clamp(weight, MinWeight, MaxWeight);

    internal static System.Windows.FontWeight ResolveTitleWeight(int weight) =>
        System.Windows.FontWeight.FromOpenTypeWeight(NormalizeWeight(weight));

    internal static System.Windows.FontWeight ResolveBodyWeight(int weight) =>
        System.Windows.FontWeight.FromOpenTypeWeight(
            NormalizeWeight(weight - 200));

    internal static int ResolveLegacyWeight(PlayerFontWeightPreset preset) =>
        preset switch
        {
            PlayerFontWeightPreset.Light => 400,
            PlayerFontWeightPreset.Bold => 700,
            _ => DefaultWeight
        };

    /// <summary>
    /// 拼接为 WPF 字体回退链。西文必须放在中文之前：回退链按逐字形解析，
    /// 拉丁字符命中西文字体，中文落到中文字体；顺序颠倒则英文会被中文字体自带的西文字形覆盖。
    /// 链尾追加东亚回退（简体中文版 Windows 均自带）：
    /// MS Gothic 接管日文和制汉字（辻/働/畑 等 GBK 之外的码位），但含 JIS 汉字，
    /// 仅在链中已有中文字体时追加，避免截胡"中文=系统默认"预设的中文字形回退；
    /// Malgun Gothic 为纯谚文字体、无汉字，可始终安全追加；
    /// Microsoft JhengHei（微軟正黑體）接管简体字体缺失的繁体码位，覆盖台港澳歌曲文本。
    /// </summary>
    internal static string ResolveText(LatinFontPreset latin, CjkFontPreset cjk)
    {
        var latinFont = latin switch
        {
            LatinFontPreset.SegoeUi => "Segoe UI Variable Text, Segoe UI",
            LatinFontPreset.Arial => "Arial",
            LatinFontPreset.Calibri => "Calibri",
            LatinFontPreset.Verdana => "Verdana",
            LatinFontPreset.Consolas => "Consolas",
            LatinFontPreset.TimesNewRoman => "Times New Roman",
            _ => null
        };
        var cjkFont = cjk switch
        {
            CjkFontPreset.MicrosoftYaHei => "Microsoft YaHei UI",
            CjkFontPreset.DengXian => "DengXian",
            CjkFontPreset.SimSun => "SimSun",
            CjkFontPreset.SimHei => "SimHei",
            CjkFontPreset.KaiTi => "KaiTi",
            CjkFontPreset.FangSong => "FangSong",
            _ => null
        };
        var parts = new List<string>(5);
        if (latinFont is not null)
        {
            parts.Add(latinFont);
        }

        if (cjkFont is not null)
        {
            parts.Add(cjkFont);
            parts.Add("MS Gothic");
        }

        parts.Add("Malgun Gothic");
        parts.Add("Microsoft JhengHei");
        return parts.Count == 0
            ? "Microsoft YaHei UI, Segoe UI"
            : string.Join(", ", parts);
    }
}
