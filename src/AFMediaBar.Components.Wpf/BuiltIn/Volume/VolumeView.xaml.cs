using System.Windows.Controls;
using System.Windows.Input;

namespace AFMediaBar.Components.Wpf.BuiltIn.Volume;

public partial class VolumeView : UserControl
{
    public VolumeView() => InitializeComponent();

    private void VolumeButton_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not VolumeViewModel viewModel || !viewModel.IsAvailable) return;
        e.Handled = true;
        viewModel.RequestWheel(e.Delta, VolumeButton);
    }
}
