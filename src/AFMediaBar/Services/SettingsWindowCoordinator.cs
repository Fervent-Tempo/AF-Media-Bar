using System.Windows;
using AFMediaBar.Settings;
using Microsoft.Extensions.DependencyInjection;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar.Services;

/// <summary>
/// Owns the settings window's single-instance and activation policy.
/// </summary>
internal sealed class SettingsWindowCoordinator(IServiceProvider services) : IDisposable
{
    private SettingsWindow? _window;
    private bool _disposed;

    internal SettingsWindow? Current => _window;

    internal void Show()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_window is null)
            {
                _window = services.GetRequiredService<SettingsWindow>();
                _window.Closed += Window_OnClosed;
                _window.Show();
            }
            else
            {
                if (_window.WindowState == WindowState.Minimized)
                {
                    _window.WindowState = WindowState.Normal;
                }

                _window.Show();
            }

            _window.Activate();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("open-settings-window", exception);
            if (_window is not null)
            {
                _window.Closed -= Window_OnClosed;
            }

            _window = null;
            MessageBox.Show(
                Loc.Get("Msg.OpenSettingsFailedBody", exception.Message),
                Loc.Get("Msg.OpenSettingsFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow window)
        {
            window.Closed -= Window_OnClosed;
        }

        _window = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window is not null)
        {
            _window.Closed -= Window_OnClosed;
        }
    }
}
