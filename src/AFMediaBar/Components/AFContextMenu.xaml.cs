using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Updates;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Input;

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
        [RelayCommand]
        private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>退出整个程序。/ Exits the application.</summary>
        [RelayCommand]
        private void ExitApplication() => Application.Current.Shutdown();

        /// <summary>右键菜单的更新入口。/ The context menu's update entry.</summary>
        [RelayCommand]
        private void UpdateMenu() => ExecuteUpdateAction();

        /// <summary>切换到指定媒体会话（参数为会话 Key）。/ Switches to the session identified by the parameter key.</summary>
        [RelayCommand]
        private void SelectMediaSession(string? key) =>
            _mediaSessionService.SelectSession(key ?? string.Empty);

        /// <summary>重新扫描 SMTC 会话并刷新。/ Re-scans SMTC sessions and refreshes.</summary>
        [RelayCommand]
        private async Task ReconnectMediaSession() =>
            await _mediaSessionService.ReconnectAsync();

        public AFContextMenu()
        {
            _mediaSessionService = App.Services.GetRequiredService<MediaSessionService>();
            _updateService = App.Services.GetRequiredService<UpdateService>();

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
