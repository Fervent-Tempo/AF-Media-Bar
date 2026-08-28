using System.Windows;
using System.Windows.Controls;
using AFMediaBar.LayoutEditor.Wpf.ViewModels;

namespace AFMediaBar.LayoutEditor.Wpf.Views;

public partial class LayoutTreeView : UserControl
{
    public LayoutTreeView() => InitializeComponent();

    public event EventHandler<LayoutTreeItemViewModel>? ItemSelected;

    private void Tree_OnSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is LayoutEditorViewModel viewModel &&
            e.NewValue is LayoutTreeItemViewModel item &&
            item.Kind != LayoutEditorNodeKind.Slot)
        {
            viewModel.SelectNode(item.InstanceId);
            ItemSelected?.Invoke(this, item);
        }
    }
}
