using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Updates;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AFMediaBar.Components
{
    /// <summary>
    /// 托盘右键菜单：媒体会话、任务栏宿主、更新、设置与退出入口。
    /// Tray context menu with media sessions, taskbar host, update, settings, and exit entries.
    /// </summary>
    public partial class AFContextMenu : ContextMenu
    {
        private readonly MediaSessionService _mediaSessionService;
        private readonly UpdateService _updateService;

        /// <summary>打开设置窗口（已打开时激活到前台）。/ Opens the settings window, activating it when already open.</summary>
        public ICommand OpenSettingsCommand { get; }

        /// <summary>退出整个程序。/ Exits the application.</summary>
        public ICommand ExitApplicationCommand { get; }

        /// <summary>右键菜单的更新入口。/ The context menu's update entry.</summary>
        public ICommand UpdateMenuCommand { get; }

        /// <summary>切换到指定媒体会话（参数为会话 Key）。/ Switches to the session identified by the parameter key.</summary>
        public ICommand SelectMediaSessionCommand { get; }

        /// <summary>重新扫描 SMTC 会话并刷新。/ Re-scans SMTC sessions and refreshes.</summary>
        public ICommand ReconnectMediaSessionCommand { get; }

        public AFContextMenu()
        {
            _mediaSessionService = App.Services.GetRequiredService<MediaSessionService>();
            _updateService = App.Services.GetRequiredService<UpdateService>();

            SelectMediaSessionCommand = new RelayCommand<string>(
                key => _mediaSessionService.SelectSession(key ?? string.Empty));
            ReconnectMediaSessionCommand = new AsyncRelayCommand(_mediaSessionService.ReconnectAsync);
            OpenSettingsCommand = new RelayCommand(
                () => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
            ExitApplicationCommand = new RelayCommand(() => Application.Current.Shutdown());
            UpdateMenuCommand = new RelayCommand(ExecuteUpdateAction);

            InitializeComponent();
        }

        public event EventHandler? OpenSettingsRequested;

        public event EventHandler? OpenUpdateSettingsRequested;

        public event EventHandler? ReloadTaskbarHostRequested;

        public bool IsReloadTaskbarHostEnabled
        {
            get => ReloadTaskbarHostMenuItem.IsEnabled;
            set => ReloadTaskbarHostMenuItem.IsEnabled = value;
        }

        public void ApplySessions(IReadOnlyList<MediaSessionOption> options)
        {
            SessionsMenuItem.Items.Clear();
            foreach (var option in options)
            {
                SessionsMenuItem.Items.Add(new System.Windows.Controls.MenuItem
                {
                    Header = option.DisplayName,
                    IsCheckable = true,
                    IsChecked = option.IsSelected,
                    Command = SelectMediaSessionCommand,
                    CommandParameter = option.Key
                });
            }
        }

        private void ReloadTaskbarHostMenuItem_Click(object sender, RoutedEventArgs e) =>
            ReloadTaskbarHostRequested?.Invoke(this, EventArgs.Empty);

        private void ExecuteUpdateAction()
        {
            switch (UpdatePresentationPolicy.ResolveTrayAction(_updateService.CurrentState))
            {
                case UpdateTrayAction.Check:
                    _ = CheckForUpdatesAsync();
                    break;
                case UpdateTrayAction.Cancel:
                    _updateService.CancelDownload();
                    break;
                case UpdateTrayAction.InstallAndRestart:
                    _updateService.RequestInstallAndExit();
                    break;
                case UpdateTrayAction.OpenUpdatePage:
                    OpenUpdateSettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                await _updateService.CheckAsync(manual: true);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[Update] Manual check failed: {exception}");
            }
        }
    }
}
