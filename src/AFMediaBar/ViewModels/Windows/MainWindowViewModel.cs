using System.Collections.ObjectModel;
using Wpf.Ui.Controls;

namespace AFMediaBar.ViewModels.Windows
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty] private string _applicationTitle = "AFMediaBar";
    }
}