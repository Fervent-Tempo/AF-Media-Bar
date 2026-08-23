using Microsoft.UI.Xaml;

namespace AFMediaBar.WinUI.TaskbarHost;

public partial class App : Application
{
    private Window? _window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window ??= new MainWindow();
        _window.Activate();
    }
}
