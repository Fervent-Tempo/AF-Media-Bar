using System.Collections.ObjectModel;
namespace AFMediaBar.ViewModels.Windows
{
    /// <summary>
    /// 设置窗口的纯数据视图模型，仅提供窗口标题。
    /// Pure data view model for the settings window, exposing only window metadata.
    /// </summary>
    public partial class SettingsWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "AFMediaBar";

    }
}
