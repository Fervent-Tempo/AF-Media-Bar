// 性能组件菜单只表达命令；隐藏主窗口拥有菜单窗口并在退出时关闭。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AFMediaBar.ViewModels.Pages;
using WpfMenuItem = Wpf.Ui.Controls.MenuItem;

namespace AFMediaBar.Components
{
    /// <summary>与现有菜单共用主题和顶层菜单窗口的内存操作入口。</summary>
    public sealed class MemoryContextMenu : StackPanel
    {
        public MemoryContextMenu(MemoryToolsViewModel viewModel, Action openSettings)
        {
            DataContext = viewModel;
            var af = Item("Memory.OptimizeAf", "DeepCommand");
            af.SetBinding(VisibilityProperty, new Binding(nameof(viewModel.IsEnabled)) { Converter = new BooleanToVisibilityConverter() });
            Children.Add(af);
            var system = Item("Memory.OptimizeSystem", null);
            system.SetBinding(VisibilityProperty, new Binding(nameof(viewModel.IsSystemCleanupEnabled)) { Converter = new BooleanToVisibilityConverter() });
            system.Items.Add(Item("Memory.System.Standard", "CleanSystemCommand"));
            system.Items.Add(Item("Memory.System.Deep", "CleanSystemDeepCommand"));
            Children.Add(system);
            var details = Item("Memory.Result.ViewLatest", "ShowLastResultCommand");
            details.SetBinding(VisibilityProperty, new Binding(nameof(viewModel.HasManualResult)) { Converter = new BooleanToVisibilityConverter() });
            Children.Add(details);
            var settings = Item("Memory.Open", null);
            settings.Click += (_, _) => openSettings();
            Children.Add(settings);
        }

        private static WpfMenuItem Item(string key, string? command)
        {
            var item = new WpfMenuItem();
            item.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Loc." + key);
            if (command is not null)
            {
                item.SetBinding(MenuItem.CommandProperty, new Binding(command));
                item.SetBinding(IsEnabledProperty, new Binding("CanRun"));
            }
            return item;
        }
    }
}
