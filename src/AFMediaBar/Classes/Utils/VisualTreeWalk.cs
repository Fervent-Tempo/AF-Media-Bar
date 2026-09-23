using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 沿树向上走一层，同时接受可视元素与内容元素。
/// Walks one level up the tree, accepting visual and content elements alike.
///
/// 为什么需要它：鼠标事件的 <c>OriginalSource</c> 不一定是 <c>Visual</c>——文本的每个 <see cref="System.Windows.Documents.Run"/>
/// 都是 <c>ContentElement</c>，直接调用 <c>VisualTreeHelper.GetParent</c> 会抛
/// "Run 不是 Visual 或 Visual3D" 并把进程带崩。凡是"从事件源向上找某个祖先"的代码都必须经过这里：
/// Run 先走到承载它的 <see cref="System.Windows.Controls.TextBlock"/>，之后才是普通的可视树路径。
/// Why this exists: a mouse event's <c>OriginalSource</c> is not necessarily a <c>Visual</c> — every text
/// <see cref="System.Windows.Documents.Run"/> is a <c>ContentElement</c>, and calling <c>VisualTreeHelper.GetParent</c> on it throws
/// "Run is not a Visual or Visual3D" and takes the process down. Every "walk up from an event source looking for an ancestor" has to
/// go through here: a Run first reaches the <see cref="System.Windows.Controls.TextBlock"/> that hosts it, and only then the ordinary
/// visual path applies.
/// </summary>
public static class VisualTreeWalk
{
    /// <summary>
    /// 返回节点的父级：可视元素走 <see cref="VisualTreeHelper"/>，内容元素走它自己的父级引用；
    /// 没有父级（游离的 Run、普通 <see cref="DependencyObject"/>）时返回 null，绝不抛异常。
    /// Returns a node's parent: visual elements go through <see cref="VisualTreeHelper"/>, content elements use their own parent
    /// reference, and parentless nodes (a detached Run, a plain <see cref="DependencyObject"/>) return null instead of throwing.
    /// </summary>
    /// <param name="node">起点节点；可以为 null。/ The starting node; may be null.</param>
    public static DependencyObject? GetParent(DependencyObject? node) => node switch
    {
        null => null,
        Visual or Visual3D => VisualTreeHelper.GetParent(node),
        FrameworkContentElement content => content.Parent,
        ContentElement content => ContentOperations.GetParent(content),
        _ => null
    };
}
