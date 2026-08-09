using System.Threading;
using System.Windows;
using AFMediaBar.Services;

namespace AFMediaBar;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "AFMediaBar.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        try
        {
            StartupService.Migrate();
        }
        catch
        {
            // A locked Run key must not prevent the application from starting.
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
