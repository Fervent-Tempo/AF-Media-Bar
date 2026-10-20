// 实验页只表达开关与导航意图，设置窗口复用原有页面和作用域。
using System.Windows.Controls;
using AFMediaBar.ViewModels.Pages;

namespace AFMediaBar.Views.Pages;

/// <summary>实验开关、风险提示及唯一设置页面的跳转入口。</summary>
public partial class ExperimentalPage : Page
{
    public ExperimentalViewModel ViewModel { get; }
    public ExperimentalPage(ExperimentalViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
