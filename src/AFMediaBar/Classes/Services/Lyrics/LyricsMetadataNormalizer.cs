// 在线取词请求侧的元数据归一化，纯字符串函数，不持有外部资源。
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 把 SMTC 元数据里常见的全半角、中点与空白差异折叠成统一写法，供搜索词与评分共用。
/// 例如半角中点 ･(U+FF65) 经 NFKC 变成全角中点 ・(U+30FB)，于是「ふ･れ･ん･ど･し･た･い」
/// 能和在线曲库的「ふ・れ・ん・ど・し・た・い」匹配；只统一写法，不删除中点，
/// 避免把「ふ・れ…」误当成无中点的「ふれんどしたい」。不改显示值与缓存键。
/// </summary>
internal static partial class LyricsMetadataNormalizer
{
    /// <summary>NFKC + 连续空白折叠 + Trim；保留大小写，搜索词直接可用。</summary>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return MultipleWhitespaces().Replace(value.Normalize(NormalizationForm.FormKC), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleWhitespaces();
}
