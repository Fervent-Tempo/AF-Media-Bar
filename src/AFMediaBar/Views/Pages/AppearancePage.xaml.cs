using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages
{
    /// <summary>
    /// AppearancePage.xaml 的交互逻辑
    /// </summary>
    public partial class AppearancePage : INavigableView<AppearanceViewModel>
    {
        public AppearanceViewModel ViewModel { get; }

        public AppearancePage(AppearanceViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
            AddHandler(Mouse.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(AppearancePage_OnPreviewMouseWheel),
                handledEventsToo: true);
        }

        private void AppearancePage_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (AppearanceScrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            AppearanceScrollViewer.ScrollToVerticalOffset(
                Math.Clamp(AppearanceScrollViewer.VerticalOffset - e.Delta / 3d, 0, AppearanceScrollViewer.ScrollableHeight));
            e.Handled = true;
        }
    }
}
