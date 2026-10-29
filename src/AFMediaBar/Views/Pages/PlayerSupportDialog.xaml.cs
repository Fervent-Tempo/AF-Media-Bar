// 管理播放器支持弹窗的内容与关闭时序；数据来自快速开始视图模型，不读写设置或启动网络请求。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Components;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>播放器支持弹窗：说明、筛选、接入方式列表与无结果提示。</summary>
public partial class PlayerSupportDialog : UserControl
{
    // 窗口级对话框宿主只在设置窗口存活期间有效，因此弱引用宿主并跟随窗口生命周期取消。
    private static WeakReference<ContentDialogHost>? _host;
    private static CancellationToken _lifetime;
    private ContentDialog? _dialog;

    internal PlayerSupportDialog(QuickStartViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Loaded += OnLoaded;
        AddHandler(SettingsNavigationButton.NavigateEvent, new RoutedEventHandler(OnRelatedSettingsRequested));
    }

    internal static void SetHost(ContentDialogHost host, CancellationToken lifetime)
    {
        _host = new(host);
        _lifetime = lifetime;
    }

    internal static void ClearHost(ContentDialogHost host)
    {
        if (_host?.TryGetTarget(out var current) == true && ReferenceEquals(current, host))
        {
            _host = null;
            _lifetime = CancellationToken.None;
        }
    }

    /// <summary>在设置窗口打开弹窗；没有宿主或已取消时直接返回。</summary>
    internal static async Task ShowAsync(QuickStartViewModel model, CancellationToken cancellationToken = default)
    {
        if (_lifetime.IsCancellationRequested || cancellationToken.IsCancellationRequested || _host is null || !_host.TryGetTarget(out var host) || host is null) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime, cancellationToken);
        var service = new ContentDialogService();
        service.SetDialogHost(host);
        var content = new PlayerSupportDialog(model);
        var dialog = new ContentDialog
        {
            Title = Translations.Get("Start.Players"),
            Content = content,
            CloseButtonText = Translations.Get("Common.Close"),
            DefaultButton = ContentDialogButton.Close
        };
        content._dialog = dialog;
        try { await service.ShowAsync(dialog, request.Token); }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => FilterBox.Focus();

    /// <summary>内容里的导航先关闭弹窗，再让导航事件继续冒泡到设置窗口；路由在事件抛出时已确定。</summary>
    private void OnRelatedSettingsRequested(object sender, RoutedEventArgs e) => _dialog?.Hide(ContentDialogResult.None);
}
