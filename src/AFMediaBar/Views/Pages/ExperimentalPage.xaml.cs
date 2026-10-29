// 实验页只表达开关与导航意图，设置窗口复用原有页面和作用域。
using System.Windows.Controls;
using AFMediaBar.ViewModels.Pages;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现实验功能设置，编辑器与服务由既有作用域拥有。</summary>
public partial class ExperimentalPage : Page
{
    private void OnPageLoaded(object sender, RoutedEventArgs e) => AFMediaBar.Classes.Utils.SettingsRevealAnimator.Play(sender as Panel);
    public ExperimentalViewModel ViewModel { get; }
    public ExperimentalPage(ExperimentalViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
