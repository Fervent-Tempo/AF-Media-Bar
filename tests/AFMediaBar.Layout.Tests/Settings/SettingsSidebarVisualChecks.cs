// 展开的一级菜单曾把整组子项画进选中框；使用真实模板和选中呈现器验证标题行边界。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Controls;

namespace AFMediaBar.Layout.Tests;

internal static class SettingsSidebarVisualChecks
{
    internal static void Verify(Application app)
    {
        var navigation = new NavigationView
        {
            IsPaneOpen = true, PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            ItemTemplate = (ControlTemplate)app.FindResource("AfSettingsSidebarItemTemplate")
        };
        var category = new NavigationViewItem { Content = "Category", IsExpanded = true };
        category.MenuItems.Add(new NavigationViewItem { Content = "Child" });
        navigation.MenuItems.Add(category);
        var overlay = new Canvas { IsHitTestVisible = false };
        var host = new Grid(); host.Children.Add(navigation); host.Children.Add(overlay);
        var window = new Window { Content = host, Width = 600, Height = 400, Left = -32000, Top = -32000,
            ShowActivated = false, ShowInTaskbar = false };
        using var selection = new SettingsSidebarSelectionAnimator(navigation, overlay);
        try
        {
            window.Show();
            selection.Highlight(category);
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(1));
            var header = (FrameworkElement)category.Template.FindName("MainBorder", category);
            var surface = overlay.Children.OfType<Border>().Single();
            Assert.IsTrue(category.ActualHeight > header.ActualHeight, "检查必须覆盖已展开的一级菜单。");
            Assert.AreEqual(header.ActualHeight - 4, surface.Height, .01, "选中框只能覆盖标题行。");
            var chevron = (FrameworkElement)category.Template.FindName("PART_ChevronGrid", category);
            var frame = (FrameworkElement)category.Template.FindName("ChevronSurface", category);
            Assert.IsTrue(frame.TranslatePoint(new Point(), category).X >= category.ActualWidth - chevron.ActualWidth,
                "整个箭头框必须位于库按行末宽度计算的展开命中区域内。");
            var selectedHeight = surface.Height;
            category.IsExpanded = false;
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(1));
            Assert.AreEqual(selectedHeight, surface.Height, .01, "折叠不能改变标题选中框的高度。");
            navigation.IsPaneOpen = false;
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(1));
            var label = (FrameworkElement)category.Template.FindName("ItemContent", category);
            Assert.AreEqual(Visibility.Collapsed, label.Visibility, "收起侧栏时文字不能溢出到页面区域。");
        }
        finally { window.Close(); }
    }
}
