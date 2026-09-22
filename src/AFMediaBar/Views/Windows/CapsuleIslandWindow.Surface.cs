using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Classes.Services.Layout;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 胶囊岛窗口的表面裁剪半部分：把根表面的整棵内容树裁进药丸 / 卡片那个圆角形状，让圆角外的像素真正透明。
/// Surface-clip half of the capsule-island window: it clips the root surface's whole content tree into the pill / card rounded shape so
/// the pixels outside the corners are genuinely transparent.
///
/// 为什么必须有这一层：`Border.CornerRadius` 只裁**它自己的背景**，WPF 不会把子元素裁成圆角（`ClipToBounds` 也只裁矩形）。
/// 封面模糊底铺满整窗、胶囊封面缩略图贴在药丸最外缘，于是**连接媒体的那一刻**圆角外那四块"透镜形"区域就会被画上内容：
/// 药丸两端露出方角、缩略图的直角戳出药丸外、形变途中卡片内容也从方角里透出来——读起来就是"UI 穿出胶囊"加"一圈方块阴影"。
/// 这里用一个与命中判定**同一个形状**（`RoundedRectHitTest` 的圆角矩形）的几何裁剪兜住它；视觉形状与可点区域从此一致。
/// Why this layer has to exist: `Border.CornerRadius` clips only the border's **own background** and WPF never clips children into
/// rounded corners (`ClipToBounds` clips the rectangle only). The blurred artwork backdrop fills the whole window and the capsule's
/// artwork thumbnail sits on the pill's outermost edge, so the moment media connects those four lens-shaped areas outside the corners get
/// painted: the pill shows square tips, the thumbnail's right angles poke out, and a morph leaks the card through the corners — which
/// reads as "UI bleeding out of the capsule" plus "a rectangular shadow". A geometric clip with the very shape the hit test uses
/// (`RoundedRectHitTest`'s rounded rectangle) holds it in, and the visual shape and the clickable region agree from then on.
/// </summary>
public partial class CapsuleIslandWindow
{
    private RectangleGeometry? _surfaceClip;
    private DependencyPropertyDescriptor? _cornerRadiusDescriptor;
    private EventHandler? _cornerRadiusChanged;

    /// <summary>两个封面槽的圆角裁剪钩子（描述符 + 目标 + 处理器），关闭时逐个摘掉。/ The two artwork slots' rounded-clip hooks (descriptor, target, handler), each detached on close.</summary>
    private readonly List<(DependencyPropertyDescriptor Descriptor, DependencyObject Target, EventHandler Handler)> _artworkClipHooks = [];

    /// <summary>
    /// 给内容树装上圆角裁剪，并让它与形态动画逐帧同步。
    /// Installs the rounded clip on the content tree and keeps it in step with the morph, frame by frame.
    /// </summary>
    private void InitializeIslandSurfaceClip()
    {
        _surfaceClip = new RectangleGeometry();
        ContentRoot.Clip = _surfaceClip;
        ApplyIslandSurfaceClip();

        // 形态动画每帧先写窗口宽高、紧接着写 RootSurface.CornerRadius（见 Animation.cs 的 ApplyShapeTarget 与
        // AdvanceShapeAnimation），因此在圆角变化的那一刻读 Width/Height 拿到的就是同一帧的尺寸，裁剪不会比形变慢一帧。
        // The morph writes the window's width and height first and RootSurface.CornerRadius right after (see ApplyShapeTarget and
        // AdvanceShapeAnimation in Animation.cs), so reading Width/Height at the instant the radius changes yields the same frame's
        // size and the clip never trails the morph by a frame.
        _cornerRadiusChanged = (_, _) => ApplyIslandSurfaceClip();
        _cornerRadiusDescriptor = DependencyPropertyDescriptor.FromProperty(
            Border.CornerRadiusProperty,
            typeof(Border));
        _cornerRadiusDescriptor?.AddValueChanged(RootSurface, _cornerRadiusChanged);

        // 兜底：真实布局尺寸变化（首次布局、DPI 变化）时圆角可能没动，但窗口尺寸动了。
        // A safety net for a real layout size change (the first layout, a DPI change) where the size moves without the radius moving.
        SizeChanged += (_, _) => ApplyIslandSurfaceClip();
        Closed += (_, _) => DisposeIslandSurfaceClip();

        InstallArtworkSlotClip(CapsuleArtworkSlot, CapsuleArtworkContent);
        InstallArtworkSlotClip(CardArtworkSlot, CardArtworkContent);
    }

    /// <summary>
    /// 把某个封面槽的子内容裁成**槽位自己的**圆角形状。
    ///
    /// 与 <c>RootSurface</c> 同一个根因、低一层：`Border.CornerRadius` 只裁自己的背景，槽里的 `Image` 于是永远是一张方图。
    /// 结果是封面在两种错误之间二选一：外层没裁时方角戳出药丸（"UI 穿行"），外层裁了之后药丸的弧把方角切掉
    /// （"封面过于靠左、被圆角遮挡"）。裁成槽位自己的形状——胶囊槽是圆、卡片槽是圆角方——封面才按 XAML 里写的圆角呈现，
    /// 药丸的弧也不会再切到任何封面像素。
    /// Clips one artwork slot's content into the **slot's own** rounded shape.
    ///
    /// Same root cause as <c>RootSurface</c>, one level down: a Border's CornerRadius clips only its own background, so the Image inside
    /// the slot is always a square. The cover therefore had to pick one of two wrong looks: with the outer clip absent its right angles
    /// poked out of the pill ("UI bleeding out"), and with the outer clip present the pill's arc cut those right angles off ("the cover
    /// sits too far left and the rounded corner occludes it"). Clipping it to the slot's own shape — a circle for the capsule slot, a
    /// rounded square for the card's — shows the cover with the radius XAML declares and no cover pixel is ever cut by the pill's arc.
    /// </summary>
    private void InstallArtworkSlotClip(Border slot, FrameworkElement content)
    {
        var clip = new RectangleGeometry();
        content.Clip = clip;
        ApplyArtworkSlotClip(slot, clip);

        // 画布缩放会改写胶囊槽的尺寸与圆角（ApplyCapsuleViewSize 先写宽高、再写圆角），因此挂在圆角上就能拿到同一轮的最终尺寸；
        // 卡片槽是固定像素，这一钩子不会触发，初始化那一次就够。
        // A canvas scale rewrites the capsule slot's size and radius (ApplyCapsuleViewSize writes the size first and the radius right
        // after), so hanging on the radius yields that round's final size; the card slot is fixed-pixel, so this hook never fires there
        // and the call above is all it needs.
        var handler = new EventHandler((_, _) => ApplyArtworkSlotClip(slot, clip));
        var descriptor = DependencyPropertyDescriptor.FromProperty(Border.CornerRadiusProperty, typeof(Border));
        descriptor?.AddValueChanged(slot, handler);
        if (descriptor is not null)
            _artworkClipHooks.Add((descriptor, slot, handler));
    }

    /// <summary>按槽位当前的尺寸与圆角改写裁剪几何（尺寸不可用时保留上一次的值，见 <see cref="CapsuleIslandClipPolicy.ResolveClipRect"/>）。/ Rewrites the clip geometry from the slot's current size and radius, keeping the previous values when the size is unusable (see <see cref="CapsuleIslandClipPolicy.ResolveClipRect"/>).</summary>
    private static void ApplyArtworkSlotClip(Border slot, RectangleGeometry clip)
    {
        var width = double.IsNaN(slot.Width) || slot.Width <= 0 ? slot.ActualWidth : slot.Width;
        var height = double.IsNaN(slot.Height) || slot.Height <= 0 ? slot.ActualHeight : slot.Height;
        if (CapsuleIslandClipPolicy.ResolveClipRect(width, height) is not { } rect)
            return;

        clip.Rect = rect;
        clip.RadiusX = clip.RadiusY = CapsuleIslandClipPolicy.ResolveClipRadius(
            slot.CornerRadius.TopLeft,
            rect.Width,
            rect.Height);
    }

    /// <summary>
    /// 按当前窗口尺寸与圆角改写裁剪几何。
    ///
    /// 尺寸优先取 <c>Width</c>/<c>Height</c>（形变逐帧动画值，与命中判定同一口径），它们不可用时才回退 <c>Actual*</c>
    /// （上一次布局的结果，形变中会滞后一帧）；两者都不可用时保留上一次的裁剪（见
    /// <see cref="CapsuleIslandClipPolicy.ResolveClipRect"/>：不裁剪会重新露内容，空矩形会把整座岛裁没）。
    /// Rewrites the clip geometry from the current window size and corner radius.
    ///
    /// The size comes from <c>Width</c>/<c>Height</c> first — the per-frame animation values the hit test uses as well — and only falls
    /// back to <c>Actual*</c> (the lagging result of the last layout pass) when those are unusable; with both unusable the previous clip
    /// stays (see <see cref="CapsuleIslandClipPolicy.ResolveClipRect"/>: no clip would expose the content again, an empty rectangle would
    /// erase the whole island).
    ///
    /// **限定（已知 1 帧差，见 M-6）**：这里用的是**动画值**，而 <c>WindowProc</c> 的 <c>WM_NCHITTEST</c> 用的是
    /// <c>ActualWidth/ActualHeight</c>（滞后一次布局）。因此"视觉形状 == 可点区域"只在**平时**成立：形变生长的那条边缘上，
    /// 大约 1 帧的窄带是"看得到、点不到"（收拢时反过来）。控制器裁定不改 <c>WindowProc</c>（会碰到 <c>HTTRANSPARENT</c>
    /// 穿透机制，风险大于收益），所以这个 1 帧差是**有意保留**的，不是遗漏。
    /// **Limitation (a known one-frame difference, see M-6)**: this uses the **animation values** while <c>WindowProc</c>'s
    /// <c>WM_NCHITTEST</c> uses <c>ActualWidth/ActualHeight</c> (one layout pass behind). "The visual shape equals the clickable region"
    /// therefore holds only **at rest**: on the edge a morph is growing along, a band about one frame wide is visible but not clickable
    /// (reversed while collapsing). The controller ruled against changing <c>WindowProc</c> (it would touch the <c>HTTRANSPARENT</c>
    /// pass-through mechanism, a bigger risk than the gain), so that one-frame difference is **deliberately kept**, not an oversight.
    /// </summary>
    private void ApplyIslandSurfaceClip()
    {
        if (_surfaceClip is null || _isClosing)
            return;

        var width = double.IsNaN(Width) || Width <= 0 ? ActualWidth : Width;
        var height = double.IsNaN(Height) || Height <= 0 ? ActualHeight : Height;
        if (CapsuleIslandClipPolicy.ResolveClipRect(width, height) is not { } rect)
            return;

        _surfaceClip.Rect = rect;
        _surfaceClip.RadiusX = _surfaceClip.RadiusY = CapsuleIslandClipPolicy.ResolveClipRadius(
            RootSurface.CornerRadius.TopLeft,
            rect.Width,
            rect.Height);
    }

    /// <summary>
    /// 摘掉圆角变化的通知。值变化通知是从**静态**描述符挂到实例上的强引用：不摘掉，窗口关掉之后也永远不会被回收。
    /// Detaches the radius notifications. A value-changed notification is a strong reference hung on the instance by a **static**
    /// descriptor: left attached, the window can never be collected after it closes.
    /// </summary>
    private void DisposeIslandSurfaceClip()
    {
        if (_cornerRadiusDescriptor is { } descriptor && _cornerRadiusChanged is { } handler)
            descriptor.RemoveValueChanged(RootSurface, handler);

        foreach (var (hookDescriptor, target, hookHandler) in _artworkClipHooks)
            hookDescriptor.RemoveValueChanged(target, hookHandler);
        _artworkClipHooks.Clear();

        _cornerRadiusDescriptor = null;
        _cornerRadiusChanged = null;
        _surfaceClip = null;
    }
}
