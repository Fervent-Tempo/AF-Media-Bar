// 拥有当前重启确认窗口；合并重复请求，退出或取消时关闭，不保留业务窗口。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Resources;
using Wpf.Ui.Controls;
using DialogButton = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AFMediaBar.Views.Dialogs;

/// <summary>用独立 WPF-UI 窗口确认普通重启，无需设置窗口存在。</summary>
internal sealed class RestartConfirmationService(WindowAppearanceService appearance) : IRestartConfirmationService, IDeveloperConfirmationService, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private FluentWindow? _dialog;
    private Task<bool>? _pending;
    private Task<DeveloperConfirmationResult>? _developerPending;
    private string? _developerPurpose;
    private bool _disposed;

    public bool IsPreviewActive => _developerPending is not null;

    public Task<bool> ConfirmAsync(string reasonResourceKey, CancellationToken cancellationToken = default)
    {
        if (_disposed || cancellationToken.IsCancellationRequested) return Task.FromResult(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonResourceKey);
        if (_developerPending is not null) return Task.FromResult(false);
        return _pending ??= ShowAsync(reasonResourceKey, cancellationToken);
    }

    public Task<DeveloperConfirmationResult> ShowPreviewAsync(CancellationToken cancellationToken) =>
        ShowDeveloperAsync("Preview", "Developer.Confirm.Preview", cancellationToken);

    public Task<DeveloperConfirmationResult> ConfirmLyricsDisableAsync(CancellationToken cancellationToken) =>
        ShowDeveloperAsync("Disable", "Developer.Confirm.Disable", cancellationToken);

    private Task<DeveloperConfirmationResult> ShowDeveloperAsync(string purpose, string reason, CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested) return Task.FromResult(DeveloperConfirmationResult.Declined);
        if (_pending is not null || (_developerPending is not null && _developerPurpose != purpose))
            return Task.FromResult(DeveloperConfirmationResult.Busy);
        _developerPurpose = purpose;
        return _developerPending ??= ShowDeveloperCoreAsync(reason, token);
    }

    private async Task<DeveloperConfirmationResult> ShowDeveloperCoreAsync(string reason, CancellationToken token)
    {
        try { return await ShowAsync(reason, token) ? DeveloperConfirmationResult.Confirmed : DeveloperConfirmationResult.Declined; }
        finally { _developerPending = null; _developerPurpose = null; }
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
            var restart = new DialogButton { Content = Translations.Get(_developerPurpose == "Preview" ? "Developer.Confirm.PreviewButton" : _developerPurpose == "Disable" ? "Developer.Confirm.DisableButton" : "Restart.Dialog.Confirm"), Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
            var later = new DialogButton { Content = Translations.Get("Restart.Dialog.Later"), IsDefault = true, IsCancel = true, MinWidth = 90 };
            buttons.Children.Add(restart); buttons.Children.Add(later); content.Children.Add(buttons);
            var dialog = new FluentWindow
            {
                Title = Translations.Get(_developerPurpose is null ? "Restart.Dialog.Title" : "Developer.Window.Title"), Content = content, Width = 460,
                // 覆盖 FluentWindow 的默认最小高度，短提示也按内容收紧。
                MinHeight = 0,
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
