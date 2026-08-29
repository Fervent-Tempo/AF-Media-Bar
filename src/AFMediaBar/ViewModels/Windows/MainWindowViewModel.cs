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

        /// <summary>当前歌曲标题。</summary>
        [ObservableProperty]
        private string _songTitle = "Example Song Title";

        /// <summary>当前歌手。</summary>
        [ObservableProperty]
        private string _songArtist = "Example Artist";

        /// <summary>歌曲封面，同时用作模糊背景图。</summary>
        [ObservableProperty]
        private ImageSource? _songImage = new BitmapImage(
            new Uri("pack://application:,,,/Assets/wpfui-icon-256.png"));

        /// <summary>没有媒体播放时是否完全隐藏媒体栏。</summary>
        [ObservableProperty]
        private bool _taskbarWidgetHideCompletely;

        /// <summary>歌曲信息变化时是否播放入场动画。</summary>
        [ObservableProperty]
        private bool _taskbarWidgetAnimated = true;

        /// <summary>暂停时是否在封面图上显示暂停覆盖图标。</summary>
        [ObservableProperty]
        private bool _taskbarWidgetShowPauseOverlay = true;

        /// <summary>是否启用封面模糊背景。</summary>
        [ObservableProperty]
        private bool _taskbarWidgetBackgroundBlur;
    }
}
