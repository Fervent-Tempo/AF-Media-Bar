using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AFMediaBar.ViewModels.Windows
{
    /// <summary>
    /// MainWindow 的视图模型，属性均为预填的实例数据，供 UI 绑定。
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "AFMediaBar";
    }
}
