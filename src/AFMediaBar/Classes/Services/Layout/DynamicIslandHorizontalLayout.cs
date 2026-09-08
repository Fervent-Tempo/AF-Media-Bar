using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 灵动岛主题 - 横向布局（独立桌面窗口，横向显示）。
/// Dynamic island theme - horizontal layout (independent desktop window, horizontal display).
///
/// 特点 Features:
/// - 尺寸：288×52（比任务栏模式略高）
///   Size: 288×52 (slightly taller than taskbar mode)
/// - 更大的圆角（8）和更明显的边框
///   Larger corner radius (8) and more prominent border
/// - 适合桌面边缘收起和播放时常驻显示
///   Suitable for desktop-edge retraction and persistent playback display
///
/// 组件布局 Component Layout:
/// ┌──────────────────────────────────────────┐
/// │  [封面]  歌曲标题 - 艺术家                │
/// │   40px     224px                          │
/// └──────────────────────────────────────────┘
///    288px × 52px
/// </summary>
public static class DynamicIslandHorizontalLayout
{
    public static LayoutSchema Create() => new()
    {
        Orientation = LayoutOrientation.Horizontal,
        Description = "灵动岛主题（横向）：可拖动并支持桌面边缘收起",
        Canvas = new CanvasConfig
        {
            Width = 288,
            Height = 52,
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
            // 封面组件（略大）Artwork Component (Slightly Larger)
            new ComponentConfig
            {
                Id = "artwork",
                Type = "Artwork",
                SpacingAfter = 6,
                Bounds = new ComponentBounds(6, 6, 40, 40),
                Properties = new Dictionary<string, object>
                {
                    ["cornerRadius"] = 6.5,
                    ["showPlaceholder"] = true,
                    ["placeholderIcon"] = "MusicNote220",
                    ["placeholderIconSize"] = 27.0
                }
            },

            // 歌曲信息组件 MediaText Component
            new ComponentConfig
            {
                Id = "song-info",
                Type = "MediaText",
                AutoSizePrimary = true,
                Bounds = new ComponentBounds(52, 6, 228, 40),
                Properties = new Dictionary<string, object>
                {
                    ["showTitle"] = true,
                    ["showArtist"] = true,
                    ["titleFontSize"] = 15.0,
                    ["artistFontSize"] = 13.0,
                    ["artistOpacity"] = 0.6,
                    ["layout"] = "vertical",
                    ["verticalAlignment"] = "center"
                }
            }
        }
    };
}
