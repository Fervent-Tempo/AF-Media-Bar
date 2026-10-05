using System;
using System.Threading;
using System.Windows.Controls;
using AFMediaBar.Classes.Services;
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
        private readonly AppLogService _log;
        private CancellationTokenSource? _fontRefresh;

        public AppearanceViewModel ViewModel { get; }

        public AppearancePage(AppearanceViewModel viewModel, AppLogService log)
        {
            ViewModel = viewModel;
            _log = log;
            DataContext = this;

            InitializeComponent();
        }

        /// <summary>加载时执行入场揭示并异步补全字体列表，离页取消当前等待。</summary>
        private async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            SettingsRevealAnimator.Play(sender as Panel);
            _fontRefresh?.Cancel();
            _fontRefresh?.Dispose();
            var request = new CancellationTokenSource();
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
            if (await SettingsResetDialog.ConfirmAsync("Common.Page.Appearance")) ViewModel.ResetAppearance();
        }
    }
}
