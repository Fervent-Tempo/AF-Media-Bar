// 性能组件菜单只表达命令；隐藏主窗口拥有菜单并在退出时关闭。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AFMediaBar.ViewModels.Pages;

namespace AFMediaBar.Components;

/// <summary>与现有菜单共用主题的内存操作入口。</summary>
public sealed class MemoryContextMenu : ContextMenu
{
    public MemoryContextMenu(MemoryToolsViewModel viewModel, Action openSettings)
    {
        DataContext = viewModel;
        SetResourceReference(StyleProperty, typeof(ContextMenu));
        var af = Item("Memory.OptimizeAf", "DeepCommand");
        af.SetBinding(VisibilityProperty, new Binding(nameof(viewModel.IsEnabled)) { Converter = new BooleanToVisibilityConverter() });
        Items.Add(af);
        var system = Item("Memory.OptimizeSystem", null);
        system.SetBinding(VisibilityProperty, new Binding(nameof(viewModel.IsSystemCleanupEnabled)) { Converter = new BooleanToVisibilityConverter() });
        system.Items.Add(Item("Memory.System.Standard", "CleanSystemCommand"));
        system.Items.Add(Item("Memory.System.Deep", "CleanSystemDeepCommand"));
        Items.Add(system);
        var details = Item("Memory.Result.ViewLatest", "ShowLastResultCommand");
        details.SetBinding(VisibilityProperty, new Binding(nameof(viewModel.HasManualResult)) { Converter = new BooleanToVisibilityConverter() });
        Items.Add(details);
        var settings = Item("Memory.Open", null);
        settings.Click += (_, _) => openSettings();
        Items.Add(settings);
    }
    private static Wpf.Ui.Controls.MenuItem Item(string key, string? command)
    {
        var item = new Wpf.Ui.Controls.MenuItem();
        item.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Loc." + key);
        if (command is not null)
        {
            item.SetBinding(MenuItem.CommandProperty, new Binding(command));
            item.SetBinding(IsEnabledProperty, new Binding("CanRun"));
        }
        return item;
    }
}
