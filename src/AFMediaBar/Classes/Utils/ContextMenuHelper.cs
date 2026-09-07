using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 处理跨窗口点击时的菜单关闭行为。
/// Closes menus in response to pointer input outside their popup windows.
/// </summary>
public static class ContextMenuHelper
{
    /// <summary>注册 WPF 原生菜单外部点击关闭，作为全局鼠标观察的同进程补充。 / Registers WPF's native outside-click dismissal as an in-process fallback to global mouse observation.</summary>
    public static void AttachOutsideClickDismissal(ContextMenu menu) =>
        Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(menu, OnPreviewMouseDownOutsideCapturedElement);

    /// <summary>左键点击不在菜单及其已展开子菜单内时关闭菜单。 / Closes the menu when a left click is outside it and any open submenu.</summary>
    public static void CloseIfOutside(ContextMenu menu, int screenX, int screenY)
    {
        if (!menu.IsOpen)
        {
            return;
        }

        var screenPoint = new Point(screenX, screenY);
        if (ContainsScreenPoint(menu, screenPoint) || ContainsOpenSubmenuPoint(menu, screenPoint))
        {
            return;
        }

        menu.IsOpen = false;
    }

    private static void OnPreviewMouseDownOutsideCapturedElement(object sender, MouseButtonEventArgs e)
    {
        if (sender is ContextMenu menu && e.ChangedButton == MouseButton.Left)
        {
            menu.IsOpen = false;
        }
    }

    private static bool ContainsOpenSubmenuPoint(ItemsControl owner, Point screenPoint)
    {
        for (var index = 0; index < owner.Items.Count; index++)
        {
            if (owner.ItemContainerGenerator.ContainerFromIndex(index) is not MenuItem item)
            {
                continue;
            }

            if (ContainsScreenPoint(item, screenPoint) ||
                item.IsSubmenuOpen && ContainsOpenSubmenuPoint(item, screenPoint))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsScreenPoint(FrameworkElement element, Point screenPoint)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0 ||
            PresentationSource.FromVisual(element) is null)
        {
            return false;
        }

        try
        {
            var point = element.PointFromScreen(screenPoint);
            return point.X >= 0 && point.X <= element.ActualWidth &&
                   point.Y >= 0 && point.Y <= element.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
