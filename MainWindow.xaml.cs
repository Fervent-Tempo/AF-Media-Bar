using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarPlayer.Interop;
using TaskbarPlayer.Models;
using TaskbarPlayer.Services;

namespace TaskbarPlayer;

public partial class MainWindow : Window
{
    private const int PlayerWidthAt96Dpi = 348;
    private const int HorizontalMarginAt96Dpi = 8;
    private const int VerticalMarginAt96Dpi = 4;

    private readonly MediaSessionService _mediaSessionService = new();
    private readonly DispatcherTimer _positionTimer;
    private nint _windowHandle;
    private bool _isConnected;

    public MainWindow()
    {
        InitializeComponent();
        _mediaSessionService.SnapshotChanged += OnSnapshotChanged;
        _positionTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, OnPositionTimerTick, Dispatcher);
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, new nint(extendedStyle));
        PositionOverTaskbar();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _positionTimer.Start();
        try
        {
            await _mediaSessionService.InitializeAsync();
        }
        catch (Exception exception)
        {
            ShowDisconnectedState("无法访问系统媒体会话", exception.Message);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _positionTimer.Stop();
        _mediaSessionService.Dispose();
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        PositionOverTaskbar();
    }

    private void PositionOverTaskbar()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        if (NativeMethods.ShouldHideForFullScreenApp())
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
        {
            return;
        }

        var isHorizontal = taskbarRect.Width >= taskbarRect.Height;
        if (!isHorizontal)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        var dpi = NativeMethods.GetDpiForWindow(taskbar);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var marginX = (int)Math.Round(HorizontalMarginAt96Dpi * scale);
        var marginY = (int)Math.Round(VerticalMarginAt96Dpi * scale);
        var desiredWidth = (int)Math.Round(PlayerWidthAt96Dpi * scale);
        var width = Math.Min(desiredWidth, Math.Max(0, taskbarRect.Width - (marginX * 2)));
        var height = Math.Max(1, taskbarRect.Height - (marginY * 2));

        Width = width / scale;
        Height = height / scale;

        NativeMethods.SetWindowPos(
            _windowHandle,
            NativeMethods.HwndTopmost,
            taskbarRect.Left + marginX,
            taskbarRect.Top + marginY,
            width,
            height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        Dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(MediaSnapshot snapshot)
    {
        _isConnected = snapshot.IsConnected;
        TitleText.Text = snapshot.Title;
        TitleText.ToolTip = snapshot.Title;
        ArtistText.Text = snapshot.Artist;
        ArtistText.ToolTip = snapshot.Artist;
        ArtworkImage.Source = snapshot.Artwork;
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null ? Visibility.Visible : Visibility.Collapsed;

        PreviousButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipPrevious;
        PlayPauseButton.IsEnabled = snapshot.IsConnected && snapshot.CanPlayPause;
        NextButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipNext;
        PlayPauseGlyph.Text = snapshot.IsPlaying ? "\uE769" : "\uE768";
        PlayPauseButton.ToolTip = snapshot.IsPlaying ? "暂停" : "播放";

        ConnectionMenuItem.Header = snapshot.IsConnected
            ? $"已连接：{snapshot.Title}"
            : "等待网易云音乐";
        PlayerRoot.ToolTip = snapshot.IsConnected
            ? $"网易云音乐媒体会话\n{snapshot.SourceId}"
            : "未发现网易云音乐的系统媒体会话。开始播放歌曲后会自动连接。";
    }

    private void ShowDisconnectedState(string title, string detail)
    {
        ApplySnapshot(MediaSnapshot.Disconnected with { Title = title, Artist = detail });
    }

    private async void Previous_OnClick(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(_mediaSessionService.SkipPreviousAsync);
    }

    private async void PlayPause_OnClick(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(_mediaSessionService.TogglePlayPauseAsync);
    }

    private async void Next_OnClick(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(_mediaSessionService.SkipNextAsync);
    }

    private async void Reconnect_OnClick(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(_mediaSessionService.ReconnectAsync);
    }

    private async Task RunMediaCommandAsync(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception exception)
        {
            ShowDisconnectedState("媒体控制失败", exception.Message);
        }
    }

    private void PlayerMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        StartupMenuItem.IsChecked = StartupService.IsEnabled;
        ConnectionMenuItem.Header = _isConnected ? ConnectionMenuItem.Header : "等待网易云音乐";
    }

    private void Startup_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupService.SetEnabled(StartupMenuItem.IsChecked);
        }
        catch (Exception exception)
        {
            StartupMenuItem.IsChecked = StartupService.IsEnabled;
            MessageBox.Show(exception.Message, "无法修改开机启动", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowCloudMusic_OnClick(object sender, RoutedEventArgs e)
    {
        ShowCloudMusic();
    }

    private void ShowCloudMusic_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowCloudMusic();
    }

    private static void ShowCloudMusic()
    {
        var process = Process.GetProcessesByName("cloudmusic")
            .FirstOrDefault(candidate => candidate.MainWindowHandle != nint.Zero);

        if (process is not null)
        {
            NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(process.MainWindowHandle);
            return;
        }

        var knownPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NetEase", "CloudMusic", "cloudmusic.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NetEase", "CloudMusic", "cloudmusic.exe")
        };

        var executable = knownPaths.FirstOrDefault(File.Exists);
        if (executable is not null)
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }
    }

    private void Exit_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
