using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 胶囊岛自动前景的采样门控：什么时候值得去采桌面像素。
/// Sampling gate for the island's automatic foreground: when it is worth reading desktop pixels at all.
///
/// 采样读的是**最终桌面合成结果**，因此表面不透明时采到的就是我们自己画的那块表面——决定会自反馈锁定在某个颜色上。
/// 所以只有"自动模式"且"表面真的透出背景"（旧设置 Transparent，或不透明度低于 100%）时才采样；窗口不可见、拖拽或形变中也
/// 不采，那些时刻采样矩形每帧都会过期。这与任务栏把频谱排除在采样区之外是同一个理由。
/// The sampler reads the **final desktop composition**, so an opaque surface would feed our own paint back into the decision and lock it
/// onto one colour. Sampling therefore happens only in automatic mode while the surface really shows the background (the legacy
/// Transparent setting, or an opacity below 100%); it also stops while the window is invisible or busy dragging/morphing, when the
/// sample rectangle goes stale every frame. This is the same reason the taskbar keeps its spectrum out of the sample region.
/// </summary>
public static class CapsuleIslandForegroundPolicy
{
    /// <summary>
    /// 当前是否应当请求一次背景采样。
    /// Whether a background sample should be requested right now.
    /// </summary>
    /// <param name="mode">播放器前景模式。/ Player-foreground mode.</param>
    /// <param name="backgroundTransparent">是否处于旧的 Transparent 背景样式（表面 alpha=1）。/ Whether the legacy Transparent background style is active (an alpha-1 surface).</param>
    /// <param name="backgroundOpacityPercent">表面不透明度（0–100）。/ Surface opacity percentage.</param>
    /// <param name="isVisible">窗口是否可见。/ Whether the window is visible.</param>
    /// <param name="isBusy">是否正在拖拽或形变。/ Whether a drag or a morph is in progress.</param>
    public static bool ShouldSample(
        PlayerForegroundMode mode,
        bool backgroundTransparent,
        int backgroundOpacityPercent,
        bool isVisible,
        bool isBusy) =>
        mode == PlayerForegroundMode.Automatic &&
        (backgroundTransparent || backgroundOpacityPercent < 100) &&
        isVisible &&
        !isBusy;
}
