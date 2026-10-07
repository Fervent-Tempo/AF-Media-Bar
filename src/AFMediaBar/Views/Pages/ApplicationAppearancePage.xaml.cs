using System;
using System.Threading;
using System.Windows.Controls;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages
{
    /// <summary>Application-wide theme, materials and interface typography.</summary>
    public partial class ApplicationAppearancePage : INavigableView<AppearanceViewModel>
    {
        private readonly AppLogService _log;
        private CancellationTokenSource? _fontRefresh;

        public AppearanceViewModel ViewModel { get; }

        public ApplicationAppearancePage(AppearanceViewModel viewModel, AppLogService log)
        {
            ViewModel = viewModel;
            _log = log;
            DataContext = this;

            InitializeComponent();
        }

        /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
        private async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            SettingsRevealAnimator.Play(sender as Panel);
            _fontRefresh?.Cancel();
            _fontRefresh?.Dispose();
            var request = CancellationTokenSource.CreateLinkedTokenSource(ViewModel.ContextCancellationToken);
            _fontRefresh = request;
            try
            {
                await ViewModel.RefreshInstalledFontsAsync(request.Token);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _log.Error("Settings", "后台读取已安装字体失败，外观页保留当前字体选择", exception);
            }
            finally
            {
                if (ReferenceEquals(_fontRefresh, request))
                {
                    _fontRefresh = null;
                    request.Dispose();
                }
            }
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            _fontRefresh?.Cancel();
            _fontRefresh?.Dispose();
            _fontRefresh = null;
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (await SettingsResetDialog.ConfirmAsync("Common.Page.ApplicationAppearance")) ViewModel.ResetAppearance();
        }
    }
}
