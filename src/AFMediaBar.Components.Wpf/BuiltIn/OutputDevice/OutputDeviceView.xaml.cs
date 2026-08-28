using System.Windows.Controls;
using System.Windows.Input;

namespace AFMediaBar.Components.Wpf.BuiltIn.OutputDevice;

public partial class OutputDeviceView : UserControl
{
    public OutputDeviceView() => InitializeComponent();

    private void DeviceButton_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not OutputDeviceViewModel viewModel) return;
        e.Handled = true;
        viewModel.RequestWheel(e.Delta, DeviceButton);
    }
}
