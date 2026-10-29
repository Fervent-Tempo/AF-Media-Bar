// 仅呈现媒体栏背景与几何，现有编辑器由页面作用域拥有。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>呈现外观与布局设置，编辑器与服务由既有作用域拥有。</summary>
public partial class AppearancePage : INavigableView<ContentLayoutViewModel>
{
    public static readonly RoutedEvent OpenApplicationFontsEvent = EventManager.RegisterRoutedEvent(nameof(OpenApplicationFonts), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AppearancePage));
    public event RoutedEventHandler OpenApplicationFonts { add => AddHandler(OpenApplicationFontsEvent, value); remove => RemoveHandler(OpenApplicationFontsEvent, value); }
    public ContentLayoutViewModel ViewModel { get; }
    public TaskbarAppearanceViewModel Taskbar => ViewModel.Layout;
    public bool HasTaskbarSettings => true;
    public AppearancePage(ContentLayoutViewModel viewModel) { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);
    private void OnPageUnloaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Cancel(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Settings.Page.BarAppearance", cancellationToken: ViewModel.ContextCancellationToken)) ViewModel.ResetSection(SettingsResetScope.BarAppearance);
    }
}
