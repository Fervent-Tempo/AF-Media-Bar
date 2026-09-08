using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 灵动岛主题 - 竖向布局（独立桌面窗口，竖向显示）。
/// Dynamic island theme - vertical layout (independent desktop window, vertical display).
///
/// 特点 Features:
/// - 尺寸：88×194（比任务栏竖向略宽）
///   Size: 88×194 (slightly wider than taskbar vertical)
/// - 更大的封面（66×66）和更宽松的间距
///   Larger artwork (66×66) and more spacious layout
/// - 适合竖屏显示器或桌面边缘放置
///   Suitable for vertical monitors or desktop edge placement
///
/// 组件布局 Component Layout:
/// ┌────────────┐
/// │   [封面]   │  60×60
/// │            │
/// ├────────────┤
/// │  歌曲名称  │
/// │            │  90px
/// │  艺术家    │
/// ├────────────┤
/// │  [控制]    │  24px
/// └────────────┘
///   88px × 194px
/// </summary>
public static class DynamicIslandVerticalLayout
{
    public static LayoutSchema Create() => new()
    {
        Orientation = LayoutOrientation.Vertical,
        Description = "灵动岛主题（竖向）：可拖动并支持桌面边缘收起",
        Canvas = new CanvasConfig
        {
            Width = 88,
            Height = 194,
            Background = "#01000000",
            CornerRadius = 8,
            Border = new BorderConfig
            {
                Thickness = 1.5,
                Color = "#60FFFFFF",
                TopOnly = false
            },
            Effects = new EffectsConfig
            {
                Blur = 100,
                BackgroundOpacity = 0.5
            }
        },
        Components = new List<ComponentConfig>
        {
            // 封面组件（大）Artwork Component (Large)
            new ComponentConfig
            {
                Id = "artwork",
                Type = "Artwork",
                SpacingAfter = 8,
                Bounds = new ComponentBounds(11, 10, 66, 66),
                Properties = new Dictionary<string, object>
                {
                    ["cornerRadius"] = 8.5,
                    ["showPlaceholder"] = true,
                    ["placeholderIcon"] = "MusicNote220",
                    ["placeholderIconSize"] = 40.0
                }
            },

            // 歌曲信息组件（居中对齐，支持换行）MediaText Component (Center-aligned, wrap enabled)
            new ComponentConfig
            {
                Id = "song-info",
                Type = "MediaText",
                AutoSizePrimary = true,
                Bounds = new ComponentBounds(6, 84, 76, 104),
                Properties = new Dictionary<string, object>
                {
                    ["showTitle"] = true,
                    ["showArtist"] = true,
                    ["titleFontSize"] = 13.0,
                    ["artistFontSize"] = 12.0,
                    ["artistOpacity"] = 0.6,
                    ["layout"] = "vertical",
                    ["textAlignment"] = "center",
                    ["maxLines"] = 4,
                    ["textWrapping"] = "wrap"
                }
            }
        }
    };
}
