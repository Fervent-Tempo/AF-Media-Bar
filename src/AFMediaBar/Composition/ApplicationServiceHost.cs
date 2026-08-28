using System.Windows;
using AFMediaBar.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AFMediaBar.Composition;

/// <summary>Owns application-scoped services and their shared shutdown token.</summary>
internal sealed class ApplicationServiceHost : IDisposable
{
    internal SystemThemeService ThemeService { get; }
    internal UpdateService UpdateService { get; }
    internal SettingsCoordinator SettingsCoordinator { get; }
    internal ServiceProvider ServiceProvider { get; }
    internal SettingsWindowCoordinator SettingsWindowCoordinator { get; }
    internal ApplicationResourceCoordinator ResourceCoordinator { get; }
    internal StartupUpdateCoordinator StartupUpdateCoordinator { get; }
    internal MainWindowCoordinator MainWindowCoordinator { get; }
    internal CancellationToken ShutdownToken => _shutdownCancellation.Token;

    private readonly CancellationTokenSource _shutdownCancellation = new();
    private bool _disposed;

    internal ApplicationServiceHost(
        Application application,
        Func<bool>? isShutdown = null,
        Action<MainWindow>? windowCreated = null,
        Action<MainWindow>? windowClosed = null)
    {
        SettingsCoordinator = new SettingsCoordinator();
        ThemeService = new SystemThemeService(application);
        UpdateService = new UpdateService(Adapters.WpfStringLocalizer.Instance);
        ServiceProvider = ServiceRegistration.Build(SettingsCoordinator, UpdateService);
        SettingsWindowCoordinator = new SettingsWindowCoordinator(ServiceProvider);
        ResourceCoordinator = new ApplicationResourceCoordinator(
            application,
            SettingsCoordinator,
            ThemeService,
            () => application.MainWindow as MainWindow);
        StartupUpdateCoordinator = new StartupUpdateCoordinator(
            UpdateService,
            isShutdown ?? (() => application.Dispatcher.HasShutdownStarted),
            () => SettingsWindowCoordinator.Current is not null,
            () => SettingsWindowCoordinator.Current?.Activate(),
            ShutdownToken);
        MainWindowCoordinator = new MainWindowCoordinator(
            application,
            ServiceProvider,
            application.Dispatcher,
            isShutdown ?? (() => application.Dispatcher.HasShutdownStarted),
            ShutdownToken,
            windowCreated,
            windowClosed);
    }

    internal void CancelShutdown() => _shutdownCancellation.Cancel();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdownCancellation.Cancel();
        StartupUpdateCoordinator.Dispose();
        MainWindowCoordinator.Dispose();
        SettingsWindowCoordinator.Dispose();
        ResourceCoordinator.Dispose();
        UpdateService.Dispose();
        ServiceProvider.Dispose();
        ThemeService.Dispose();
        _shutdownCancellation.Dispose();
    }
}
