// 拥有单个授权或结果窗口；退出关闭窗口，视图只呈现结果与返回选择，不安装任务或执行清理。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Resources;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AFMediaBar.Views.Dialogs;

/// <summary>独立于设置页面的内存操作授权与结果提示。</summary>
public sealed class MemoryAuthorizationPrompt(WindowAppearanceService appearance) : IMemoryAuthorizationPrompt, IDisposable
{
    private FluentWindow? _window;
    private bool _disposed;
    public async Task<MemoryAuthorizationChoice> ChooseAsync(bool persistentRequired, CancellationToken token = default)
    {
        if (_disposed || _window is not null || token.IsCancellationRequested) return MemoryAuthorizationChoice.Cancel;
        await Task.Yield();
        if (_disposed || _window is not null || token.IsCancellationRequested) return MemoryAuthorizationChoice.Cancel;
        var choice = MemoryAuthorizationChoice.Cancel;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = Translations.Get("Memory.Authorization.Explanation"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new FluentWindow
        {
            Title = Translations.Get("Memory.Authorization.Title"),
            Content = panel,
            Width = 580,
            MinHeight = 0,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        _window = dialog;
        void Add(string key, MemoryAuthorizationChoice value)
        {
            var button = new Button
            {
                Content = Translations.Get(key),
                Margin = new Thickness(4),
                IsCancel = value == MemoryAuthorizationChoice.Cancel,
                IsDefault = value == MemoryAuthorizationChoice.Cancel
            };
            button.Click += (_, _) => { choice = value; dialog.Close(); };
            buttons.Children.Add(button);
        }
        if (!persistentRequired) Add("Memory.Authorization.Once", MemoryAuthorizationChoice.Once);
        Add("Memory.Authorization.Remember", MemoryAuthorizationChoice.Remember);
        Add("Common.Cancel", MemoryAuthorizationChoice.Cancel);
        panel.Children.Add(buttons);
        using var cancellation = token.Register(() => { if (!dialog.Dispatcher.HasShutdownStarted) dialog.Dispatcher.BeginInvoke(() => dialog.Close()); });
        try { appearance.Attach(dialog); dialog.ShowDialog(); return _disposed || token.IsCancellationRequested ? MemoryAuthorizationChoice.Cancel : choice; }
        finally { _window = null; }
    }
    public async Task ShowResultAsync(string title, string message, string? explanation = null, CancellationToken token = default)
    {
        if (_disposed || _window is not null || token.IsCancellationRequested) return;
        await Task.Yield();
        if (_disposed || _window is not null || token.IsCancellationRequested) return;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        var details = new StackPanel();
        details.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(explanation))
            details.Children.Add(new Expander
            {
                Header = Translations.Get("Memory.Result.Explanation"),
                Margin = new Thickness(0, 16, 0, 0),
                Content = new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }
            });
        panel.Children.Add(new ScrollViewer
        {
            Content = details,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 480
        });
        var close = new Button
        {
            Content = Translations.Get("Memory.Result.Close"), IsDefault = true, IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0)
        };
        panel.Children.Add(close);
        var dialog = new FluentWindow
        {
            Title = title, Content = panel, Width = 600, MinHeight = 0, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = true, WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        close.Click += (_, _) => dialog.Close();
        _window = dialog;
        using var cancellation = token.Register(() => { if (!dialog.Dispatcher.HasShutdownStarted) dialog.Dispatcher.BeginInvoke(() => dialog.Close()); });
        try { appearance.Attach(dialog); dialog.ShowDialog(); }
        finally { _window = null; }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _window?.Close(); }
}
