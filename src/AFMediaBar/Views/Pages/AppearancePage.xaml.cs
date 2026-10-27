// 媒体栏外观保留原编辑器；应用主题与字体已移至个性化页。
using System.ComponentModel;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using System.Windows.Controls;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>媒体栏文字与背景设置，编辑环境由页面缓存提供。</summary>
public partial class AppearancePage : INavigableView<AppearanceViewModel>, INotifyPropertyChanged
{
    /// <summary>Requests the shared application font group from the owning settings window.</summary>
    public static readonly RoutedEvent OpenApplicationFontsEvent = EventManager.RegisterRoutedEvent(nameof(OpenApplicationFonts), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AppearancePage));
    /// <summary>Bubbles the font navigation intent without retaining the navigation window.</summary>
    public event RoutedEventHandler OpenApplicationFonts { add => AddHandler(OpenApplicationFontsEvent, value); remove => RemoveHandler(OpenApplicationFontsEvent, value); }

    /// <summary>提供页面作用域与取消状态，应用主题和字体在个性化页编辑。</summary>
    public AppearanceViewModel ViewModel { get; }
    public TaskbarAppearanceViewModel? Taskbar { get; private set; }
    public bool HasTaskbarSettings => Taskbar is not null;
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>创建媒体栏外观页，不创建平台或媒体资源。</summary>
    public AppearancePage(AppearanceViewModel viewModel, AppLogService log)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
    /// <summary>替换媒体栏编辑环境并保留页面状态。</summary>
    public void SetTaskbarEditor(TaskbarAppearanceViewModel? editor)
    {
        if (ReferenceEquals(Taskbar, editor)) return;
        Taskbar = editor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Taskbar)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTaskbarSettings)));
    }
    private void OpenApplicationFonts_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenApplicationFontsEvent));
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);
    private void OnPageUnloaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Cancel(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var taskbar = Taskbar;
        var cancellation = taskbar?.ContextCancellationToken ?? ViewModel.ContextCancellationToken;
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.Appearance", cancellationToken: cancellation) && !cancellation.IsCancellationRequested)
        {
            taskbar?.ResetAppearance();
        }
    }
}
