using AFMediaBar.Models;
using AFMediaBar.Services;
using Microsoft.UI.Xaml;

namespace AFMediaBar.WinUI;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private bool _shutdown;

    internal SettingsCoordinator SettingsCoordinator { get; private set; } = null!;

    internal WinUiStringLocalizer Localizer { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += App_OnUnhandledException;
        TaskScheduler.UnobservedTaskException += App_OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += AppDomain_OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool isFirstInstance;
        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: "AFMediaBar.SingleInstance",
                createdNew: out isFirstInstance);
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("winui-shell-single-instance", exception);
            Environment.Exit(1);
            return;
        }

        if (!isFirstInstance)
        {
            DiagnosticsLogService.Write("winui-shell-second-instance");
            _shutdown = true;
            Environment.Exit(0);
            return;
        }

        try
        {
            DiagnosticsLogService.EnsureLogFile();
            SettingsCoordinator = new SettingsCoordinator();
            Localizer = new WinUiStringLocalizer(SettingsCoordinator.Current.Language);
            SettingsCoordinator.Changed += SettingsCoordinator_OnChanged;
            _mainWindow = new MainWindow(this);
            _mainWindow.Closed += MainWindow_OnClosed;
            _mainWindow.Activate();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("winui-shell-startup", exception);
            _shutdown = true;
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Environment.Exit(1);
        }
    }

    internal void ApplyTheme(ThemeSettings settings)
    {
        _mainWindow?.ApplyTheme(settings);
    }

    internal void RequestShutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _mainWindow?.Close();
    }

    private void SettingsCoordinator_OnChanged(
        object? sender,
        SettingsChangedEventArgs args)
    {
        if (_shutdown || _mainWindow is null)
        {
            return;
        }

        if (args.Sections.HasFlag(SettingsSection.Language))
        {
            Localizer.Language = args.Settings.Language;
        }

        _mainWindow.ApplySettings(args.Settings, args.Sections);
    }

    private void MainWindow_OnClosed(object sender, WindowEventArgs args)
    {
        if (sender is MainWindow window)
        {
            window.Closed -= MainWindow_OnClosed;
            window.DisposeShellResources();
        }

        _mainWindow = null;
        SettingsCoordinator.Changed -= SettingsCoordinator_OnChanged;
        _shutdown = true;
        TaskScheduler.UnobservedTaskException -= App_OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= AppDomain_OnUnhandledException;
        UnhandledException -= App_OnUnhandledException;
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }

    private void App_OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        DiagnosticsLogService.Write("winui-dispatcher-unhandled", args.Exception);
        if (args.Exception is OutOfMemoryException or
            StackOverflowException or
            AccessViolationException)
        {
            return;
        }

        args.Handled = true;
    }

    private static void App_OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        DiagnosticsLogService.Write("winui-task-unobserved", args.Exception);
        args.SetObserved();
    }

    private static void AppDomain_OnUnhandledException(
        object sender,
        System.UnhandledExceptionEventArgs args)
    {
        DiagnosticsLogService.Write(
            "winui-appdomain-unhandled",
            args.ExceptionObject as Exception,
            $"Terminating={args.IsTerminating}");
    }

}
