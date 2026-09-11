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

        /// <summary>
        /// 调用 AppearancePage，提供 API。
        /// Provides the public AppearancePage entry point required by this component.
        /// </summary>
        public AppearancePage(AppearanceViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (await SettingsResetDialog.ConfirmAsync("外观页")) ViewModel.ResetAppearance();
        }
    }
}
