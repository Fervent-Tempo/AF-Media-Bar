using System.Windows;

namespace AFMediaBar.Services;

/// <summary>
/// 代码侧文本从语言词典取值的静态入口；
/// 缺 key 时回退返回 key 本身，便于排查遗漏的词典条目。
/// </summary>
internal static class Localization
{
    internal static string Get(string key)
    {
        var resources = Application.Current?.Resources;
        return resources is not null && resources.Contains(key) && resources[key] is string value
            ? value
            : key;
    }

    internal static string Get(string key, params object[] args)
    {
        return string.Format(Get(key), args);
    }
}