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
    /// LayoutPage.xaml 的交互逻辑
    /// </summary>
    public partial class LayoutPage : INavigableView<LayoutViewModel>
    {
        public LayoutViewModel ViewModel { get; }

        /// <summary>
        /// 调用 LayoutPage，提供 API。
        /// Provides the public LayoutPage entry point required by this component.
        /// </summary>
        public LayoutPage(LayoutViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
