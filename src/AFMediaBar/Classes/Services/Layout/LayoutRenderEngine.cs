using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 布局渲染引擎：根据 LayoutSchema 动态调整 TaskBarMediaControl 的外观和布局。
/// Layout render engine: dynamically adjusts TaskBarMediaControl's appearance and layout based on LayoutSchema.
///
/// 职责 Responsibilities:
/// 1. 应用画布配置到窗口（尺寸、背景、圆角、边框）
///    Apply canvas config to window (size, background, corner radius, border)
/// 2. 根据布局配置动态调整组件位置和大小
///    Dynamically adjust component positions and sizes based on layout config
/// 3. 应用视觉效果（模糊、透明度）
///    Apply visual effects (blur, opacity)
/// 4. 保持与现有 TaskBarMediaControl 的兼容性
///    Maintain compatibility with existing TaskBarMediaControl
///
/// ⚠️ 架构约束 Architecture Constraints:
/// - 此类只负责 UI 渲染，不包含业务逻辑
///   This class only handles UI rendering, no business logic
/// - 不直接操作 MediaSnapshot，数据更新由 TaskBarMediaControl 自己处理
///   Does not directly manipulate MediaSnapshot, data updates handled by TaskBarMediaControl itself
/// - 通过 Border、Canvas 和各组件的属性调整来实现布局变化
///   Implements layout changes through Border, Canvas, and component property adjustments
/// </summary>
public sealed class LayoutRenderEngine
{
    private readonly Border _mainBorder;
    private readonly Canvas? _contentCanvas;
    private readonly Image? _backgroundImage;

    // 组件引用 Component references
    private readonly Border? _artworkBorder;
    private readonly StackPanel? _songInfoPanel;

    private LayoutSchema? _currentLayout;

    /// <summary>
    /// 构造函数：传入 TaskBarMediaControl 中的关键 UI 元素。
    /// Constructor: pass in key UI elements from TaskBarMediaControl.
    /// </summary>
    /// <param name="mainBorder">主边框（MainBorder）/ Main border (MainBorder)</param>
    /// <param name="contentCanvas">内容画布（用于组件定位）/ Content canvas (for component positioning)</param>
    /// <param name="backgroundImage">背景图片（用于应用模糊效果）/ Background image (for blur effect)</param>
    /// <param name="artworkBorder">封面边框 / Artwork border</param>
    /// <param name="songInfoPanel">歌曲信息面板 / Song info panel</param>
    public LayoutRenderEngine(
        Border mainBorder,
        Canvas? contentCanvas = null,
        Image? backgroundImage = null,
        Border? artworkBorder = null,
        StackPanel? songInfoPanel = null)
    {
        _mainBorder = mainBorder;
        _contentCanvas = contentCanvas;
        _backgroundImage = backgroundImage;
        _artworkBorder = artworkBorder;
        _songInfoPanel = songInfoPanel;
    }

    /// <summary>
    /// 应用布局：根据 LayoutSchema 调整所有 UI 属性。
    /// Apply layout: adjust all UI properties based on LayoutSchema.
    ///
    /// 算法 Algorithm:
    /// 1. 保存当前布局配置
    ///    Save current layout config
    /// 2. 应用画布配置（尺寸、背景、圆角、边框）
    ///    Apply canvas config (size, background, corner radius, border)
    /// 3. 应用组件布局（位置、大小）
    ///    Apply component layout (positions, sizes)
    /// 4. 应用视觉效果（模糊、透明度）
    ///    Apply visual effects (blur, opacity)
    /// 5. 触发布局更新
    ///    Trigger layout update
    /// </summary>
    /// <param name="layout">布局配置 / Layout configuration</param>
    public void ApplyLayout(LayoutSchema layout)
    {
        _currentLayout = layout;

        // 应用画布配置
        // Apply canvas config
        ApplyCanvasConfig(layout.Canvas);

        // 应用组件布局
        // Apply component layout
        ApplyComponentLayout(layout.Components);

        // 应用视觉效果
        // Apply visual effects
        ApplyEffects(layout.Canvas.Effects);

        // 强制刷新布局
        // Force layout refresh
        _mainBorder.UpdateLayout();
    }

    /// <summary>
    /// 应用画布配置：尺寸、背景、圆角、边框。
    /// Apply canvas config: size, background, corner radius, border.
    /// </summary>
    private void ApplyCanvasConfig(CanvasConfig config)
    {
        // 设置尺寸
        // Set size
        _mainBorder.Width = config.Width;
        _mainBorder.Height = config.Height;

        // 设置圆角
        // Set corner radius
        _mainBorder.CornerRadius = new CornerRadius(config.CornerRadius);

        // 设置背景颜色
        // Set background color
        try
        {
            var bgColor = (Color)ColorConverter.ConvertFromString(config.Background);
            _mainBorder.Background = new SolidColorBrush(bgColor);
        }
        catch
        {
            // 如果颜色格式无效，使用透明背景
            // If color format is invalid, use transparent background
            _mainBorder.Background = Brushes.Transparent;
        }

        // 设置边框
        // Set border
        if (config.Border is not null)
        {
            ApplyBorder(config.Border);
        }
        else
        {
            _mainBorder.BorderThickness = new Thickness(0);
        }
    }

    /// <summary>
    /// 应用边框配置：粗细、颜色、是否仅顶部边框。
    /// Apply border config: thickness, color, top-only option.
    /// </summary>
    private void ApplyBorder(BorderConfig border)
    {
        // 设置边框粗细
        // Set border thickness
        _mainBorder.BorderThickness = border.TopOnly
            ? new Thickness(0, border.Thickness, 0, 0)  // 仅顶部 / Top only
            : new Thickness(border.Thickness);           // 四边 / All sides

        // 设置边框颜色
        // Set border color
        try
        {
            var borderColor = (Color)ColorConverter.ConvertFromString(border.Color);
            _mainBorder.BorderBrush = new SolidColorBrush(borderColor);
        }
        catch
        {
            // 如果颜色格式无效，使用透明边框
            // If color format is invalid, use transparent border
            _mainBorder.BorderBrush = Brushes.Transparent;
        }
    }

    /// <summary>
    /// 应用组件布局：根据 ComponentConfig 动态调整组件的位置和大小。
    /// Apply component layout: dynamically adjust component positions and sizes based on ComponentConfig.
    ///
    /// 算法 Algorithm:
    /// 遍历所有组件配置，根据 Id 找到对应的 UI 元素，应用位置和大小。
    /// Iterate through all component configs, find corresponding UI elements by Id, apply positions and sizes.
    /// </summary>
    private void ApplyComponentLayout(IReadOnlyList<ComponentConfig> components)
    {
        if (_contentCanvas is null) return;

        foreach (var component in components)
        {
            switch (component.Id)
            {
                case "artwork":
                    ApplyArtworkLayout(component);
                    break;

                case "song-info":
                    ApplySongInfoLayout(component);
                    break;

            }
        }
    }

    /// <summary>
    /// 应用封面组件布局。
    /// Apply artwork component layout.
    /// </summary>
    private void ApplyArtworkLayout(ComponentConfig config)
    {
        if (_artworkBorder is null) return;

        // 设置尺寸
        // Set size
        _artworkBorder.Width = config.Bounds.Width;
        _artworkBorder.Height = config.Bounds.Height;

        // 设置位置
        // Set position
        Canvas.SetLeft(_artworkBorder, config.Bounds.X);
        Canvas.SetTop(_artworkBorder, config.Bounds.Y);

        // 应用自定义属性（如圆角）
        // Apply custom properties (e.g., corner radius)
        if (config.Properties.TryGetValue("cornerRadius", out var cornerRadiusValue)
            && cornerRadiusValue is double cornerRadius)
        {
            _artworkBorder.CornerRadius = new CornerRadius(cornerRadius);
        }

        // 显示组件
        // Show component
        _artworkBorder.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 应用歌曲信息组件布局。
    /// Apply song info component layout.
    /// </summary>
    private void ApplySongInfoLayout(ComponentConfig config)
    {
        if (_songInfoPanel is null) return;

        // 设置尺寸
        // Set size
        _songInfoPanel.Width = config.Bounds.Width;
        _songInfoPanel.Height = config.Bounds.Height;

        // 设置位置
        // Set position
        Canvas.SetLeft(_songInfoPanel, config.Bounds.X);
        Canvas.SetTop(_songInfoPanel, config.Bounds.Y);

        // 应用自定义属性（如文本对齐方式）
        // Apply custom properties (e.g., text alignment)
        if (config.Properties.TryGetValue("textAlign", out var textAlignValue)
            && textAlignValue is string textAlign)
        {
            _songInfoPanel.HorizontalAlignment = textAlign switch
            {
                "left" => HorizontalAlignment.Left,
                "center" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left
            };
        }

        // 显示组件
        // Show component
        _songInfoPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 应用视觉效果：模糊和透明度。
    /// Apply visual effects: blur and opacity.
    /// </summary>
    private void ApplyEffects(EffectsConfig? effects)
    {
        if (effects is null)
        {
            return;
        }

        // 应用背景模糊效果（如果有背景图片）
        // Apply background blur effect (if background image exists)
        if (_backgroundImage is not null)
        {
            if (effects.Blur > 0)
            {
                _backgroundImage.Effect = new BlurEffect
                {
                    Radius = effects.Blur,
                    KernelType = KernelType.Gaussian
                };
            }
            else
            {
                _backgroundImage.Effect = null;
            }

            // 设置背景图片的透明度
            // Set background image opacity
            _backgroundImage.Opacity = effects.BackgroundOpacity;
        }
    }

    /// <summary>
    /// 获取当前应用的布局配置。
    /// Get currently applied layout configuration.
    /// </summary>
    public LayoutSchema? CurrentLayout => _currentLayout;

    /// <summary>
    /// 获取当前布局的方向。
    /// Get current layout orientation.
    /// </summary>
    public LayoutOrientation? CurrentOrientation => _currentLayout?.Orientation;
}
