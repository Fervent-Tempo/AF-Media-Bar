using System;
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
        }
    }
}
