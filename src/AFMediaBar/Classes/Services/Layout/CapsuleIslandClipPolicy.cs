using System;
using System.Windows;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 胶囊岛内容裁剪的几何解析：把窗口尺寸与圆角换成 <see cref="Rect"/> 与半径，并处理不可用的输入。
/// Geometry resolution for the capsule island's content clip: it turns the window size and the corner radius into a
/// <see cref="Rect"/> and a radius, and handles unusable input.
///
/// 为什么需要它：`Border.CornerRadius` 只裁自己的背景，WPF 不会把子元素裁成圆角，所以内容树必须用一个几何
/// <c>Clip</c> 兜住；而这个裁剪每帧都跟着形变改写，输入来自动画值，**不能**假定它们总是有限的正数。
/// Why it exists: `Border.CornerRadius` only clips its own background and WPF never clips children into rounded corners, so the content
/// tree needs a geometric <c>Clip</c>; that clip is rewritten every frame of a morph, its input comes from animation values, and those
/// **cannot** be assumed to be finite positive numbers.
/// </summary>
public static class CapsuleIslandClipPolicy
{
    /// <summary>
    /// 解析内容裁剪矩形：左上角固定在原点，尺寸就是窗口尺寸；尺寸不可用（NaN / 非正 / 非有限）时返回 null，
    /// 调用方据此**保留上一次的裁剪值**。
    ///
    /// 两种退化都必须避免：不裁剪会让圆角外重新露出内容（正是要修的缺陷），而空矩形会把整座岛裁没（窗口看起来直接消失）。
    /// Resolves the content clip rectangle, anchored at the origin and sized like the window; an unusable size (NaN, non-positive,
    /// non-finite) answers null so the caller **keeps the previous clip**.
    ///
    /// Both degradations have to be avoided: no clip shows the content outside the corners again — the very defect being fixed — while an
    /// empty rectangle would erase the whole island, making the window vanish outright.
    /// </summary>
    /// <param name="width">窗口宽度（DIP）/ Window width in DIP.</param>
    /// <param name="height">窗口高度（DIP）/ Window height in DIP.</param>
    public static Rect? ResolveClipRect(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0
            ? new Rect(0, 0, width, height)
            : null;

    /// <summary>
    /// 解析裁剪矩形的圆角半径：非法或非正值取 0（直角裁剪），合法值夹到短边的一半——
    /// 与 <c>Border</c> 的圆角口径一致（药丸的半径本来就是高度的一半，卡片态也是这么夹的）。
    /// Resolves the clip rectangle's corner radius: an invalid or non-positive value becomes 0 (a square clip) and a valid one is
    /// clamped to half the shorter side, matching the <c>Border</c> basis (the pill's radius already is half its height, and the card
    /// clamps the same way).
    /// </summary>
    /// <param name="cornerRadius">当前圆角（DIP）/ Current corner radius in DIP.</param>
    /// <param name="width">裁剪矩形宽度（DIP）/ Clip rectangle width in DIP.</param>
    /// <param name="height">裁剪矩形高度（DIP）/ Clip rectangle height in DIP.</param>
    public static double ResolveClipRadius(double cornerRadius, double width, double height)
    {
        if (!double.IsFinite(cornerRadius) || cornerRadius <= 0)
            return 0;

        var limit = Math.Min(width, height) / 2;
        return double.IsFinite(limit) && limit > 0 ? Math.Min(cornerRadius, limit) : 0;
    }
}
