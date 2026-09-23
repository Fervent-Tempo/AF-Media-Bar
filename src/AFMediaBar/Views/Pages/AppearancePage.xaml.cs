using System;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages
{
    /// <summary>
    /// 全局外观设置页：字体、文字颜色、窗口材质和当前模式表面。
    /// Global appearance settings page covering fonts, player text colour, window materials, and the current surface.
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

        /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshInstalledFonts();
            SettingsRevealAnimator.Play(sender as Panel);
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (await SettingsResetDialog.ConfirmAsync("Common.Page.Appearance")) ViewModel.ResetAppearance();
        }
    }
}
