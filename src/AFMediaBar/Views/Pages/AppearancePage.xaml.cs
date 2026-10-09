// 统一呈现全局与媒体栏外观；全局页面及上下文编辑器由页面缓存分别持有和释放。
using System.ComponentModel;
using System.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using System.Windows.Controls;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>统一外观入口，全局编辑器与媒体栏上下文编辑器分别由缓存持有。</summary>
public partial class AppearancePage : INavigableView<AppearanceViewModel>, INotifyPropertyChanged
{
    /// <summary>Requests the shared application font group from the owning settings window.</summary>
    public static readonly RoutedEvent OpenApplicationFontsEvent = EventManager.RegisterRoutedEvent(nameof(OpenApplicationFonts), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AppearancePage));
    /// <summary>Bubbles the font navigation intent without retaining the navigation window.</summary>
    public event RoutedEventHandler OpenApplicationFonts { add => AddHandler(OpenApplicationFontsEvent, value); remove => RemoveHandler(OpenApplicationFontsEvent, value); }

    /// <summary>窗口作用域内复用的全局主题与字体编辑器。</summary>
    public AppearanceViewModel ViewModel { get; }
    public TaskbarAppearanceViewModel? Taskbar { get; private set; }
    public bool HasTaskbarSettings => Taskbar is not null;
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly AppLogService _log;
    private CancellationTokenSource? _fontRefresh;
    /// <summary>创建统一外观页，不创建平台或媒体资源。</summary>
    public AppearancePage(AppearanceViewModel viewModel, AppLogService log)
    {
        ViewModel = viewModel;
        _log = log;
        DataContext = this;
        InitializeComponent();
    }
    /// <summary>替换媒体栏编辑环境，全局主题、字体及页面状态保持不变。</summary>
    public void SetTaskbarEditor(TaskbarAppearanceViewModel? editor)
    {
        if (ReferenceEquals(Taskbar, editor)) return;
        Taskbar = editor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Taskbar)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTaskbarSettings)));
    }
    private void OpenApplicationFonts_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenApplicationFontsEvent));
    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        SettingsRevealAnimator.Play(sender as Panel);
        OnPageUnloaded(sender, e);
        var request = CancellationTokenSource.CreateLinkedTokenSource(ViewModel.ContextCancellationToken);
        _fontRefresh = request;
        try { await ViewModel.RefreshInstalledFontsAsync(request.Token); }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception) { _log.Error("Settings", "后台读取字体失败，保留当前选择", exception); }
        finally
        {
            if (ReferenceEquals(_fontRefresh, request)) _fontRefresh = null;
            request.Dispose();
        }
    }
    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _fontRefresh?.Cancel();
        _fontRefresh = null;
    }
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var taskbar = Taskbar;
        var cancellation = taskbar?.ContextCancellationToken ?? ViewModel.ContextCancellationToken;
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.Appearance", cancellationToken: cancellation) && !cancellation.IsCancellationRequested)
        {
            ViewModel.ResetAppearance();
            taskbar?.ResetAppearance();
        }
    }
}
