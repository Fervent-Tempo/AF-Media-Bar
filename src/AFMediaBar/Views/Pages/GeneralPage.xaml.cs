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
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Views.Pages
{
    /// <summary>
    /// GeneralPage.xaml 的交互逻辑
    /// </summary>
    public partial class GeneralPage : INavigableView<GeneralViewModel>
    {
        public GeneralViewModel ViewModel { get; }
        private readonly SettingsPersistenceService _persistence;

        /// <summary>
        /// 调用 GeneralPage，提供 API。
        /// Provides the public GeneralPage entry point required by this component.
        /// </summary>
        public GeneralPage(GeneralViewModel viewModel, SettingsPersistenceService persistence)
        {
            ViewModel = viewModel;
            _persistence = persistence;
            DataContext = this;

            InitializeComponent();
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (await SettingsResetDialog.ConfirmAsync("常规页")) ViewModel.ResetGeneral();
        }

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => _persistence.OpenSettingsFolder();
    }
}
