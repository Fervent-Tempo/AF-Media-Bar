// 拥有当前重启确认窗口；合并重复请求，退出或取消时关闭，不保留业务窗口。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Resources;
using Wpf.Ui.Controls;
using DialogButton = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AFMediaBar.Views.Dialogs;

/// <summary>用独立 WPF-UI 窗口确认普通重启，无需设置窗口存在。</summary>
internal sealed class RestartConfirmationService(WindowAppearanceService appearance) : IRestartConfirmationService, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private FluentWindow? _dialog;
    private Task<bool>? _pending;
    private bool _disposed;

    public Task<bool> ConfirmAsync(string reasonResourceKey, CancellationToken cancellationToken = default)
    {
        if (_disposed || cancellationToken.IsCancellationRequested) return Task.FromResult(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonResourceKey);
        return _pending ??= ShowAsync(reasonResourceKey, cancellationToken);
    }

    private async Task<bool> ShowAsync(string reasonResourceKey, CancellationToken cancellationToken)
    {
        // 先返回任务，保证重复请求在对话框创建前也能合并。
        await Task.Yield();
        if (_disposed || cancellationToken.IsCancellationRequested) { _pending = null; return false; }
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var confirmed = false;
            var content = new StackPanel { Margin = new Thickness(24) };
            content.Children.Add(new TextBlock { Text = Translations.Get(reasonResourceKey), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var restart = new DialogButton { Content = Translations.Get("Restart.Dialog.Confirm"), Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
            var later = new DialogButton { Content = Translations.Get("Restart.Dialog.Later"), IsDefault = true, IsCancel = true, MinWidth = 90 };
            buttons.Children.Add(restart); buttons.Children.Add(later); content.Children.Add(buttons);
            var dialog = new FluentWindow
            {
                Title = Translations.Get("Restart.Dialog.Title"), Content = content, Width = 460,
                SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = true, WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            _dialog = dialog;
            restart.Click += (_, _) => { confirmed = true; dialog.Close(); };
            later.Click += (_, _) => dialog.Close();
            dialog.Loaded += (_, _) => later.Focus();
            using var cancellation = request.Token.Register(() =>
            {
                if (dialog.Dispatcher.HasShutdownStarted) return;
                if (dialog.Dispatcher.CheckAccess()) dialog.Close();
                else _ = dialog.Dispatcher.BeginInvoke(() => dialog.Close());
            });
            appearance.Attach(dialog);
            dialog.ShowDialog();
            return confirmed && !request.IsCancellationRequested && !_disposed;
        }
        finally
        {
            _dialog?.Close();
            _dialog = null;
            _pending = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _dialog?.Close();
        _lifetime.Dispose();
    }
}
