using AFMediaBar.Classes.Models;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.ViewModels.Components;

namespace AFMediaBar.Components
{
    /// <summary>
    /// 托盘右键菜单：媒体会话、任务栏宿主、更新、设置与退出入口。
    /// Tray context menu with media sessions, taskbar host, update, settings, and exit entries.
    /// </summary>
    public partial class AFContextMenu : ContextMenu
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel), typeof(AFContextMenuViewModel), typeof(AFContextMenu));

        public AFContextMenuViewModel? ViewModel
        {
            get => (AFContextMenuViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        /// <summary>打开设置窗口（已打开时激活到前台）。/ Opens the settings window, activating it when already open.</summary>
        [RelayCommand]
        private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>退出整个程序。/ Exits the application.</summary>
        [RelayCommand]
        private void ExitApplication() => ExitRequested?.Invoke(this, EventArgs.Empty);

        public AFContextMenu() => InitializeComponent();

        public event EventHandler? OpenSettingsRequested;

        /// <summary>请求宿主通过统一退出入口关闭程序。</summary>
        public event EventHandler? ExitRequested;

        public event EventHandler? OpenUpdateSettingsRequested;

        public event EventHandler? ReloadTaskbarHostRequested;

        public event Action<QuickLaunchEntry>? QuickLaunchRequested;

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
                    Command = ViewModel?.SelectMediaSessionCommand,
                    CommandParameter = option.Key
                });
            }
        }

        public void ApplyQuickLaunchEntries(IReadOnlyList<QuickLaunchEntry> entries)
        {
            QuickLaunchMenuItem.Items.Clear();
            if (entries.Count == 0)
            {
                QuickLaunchMenuItem.Items.Add(new System.Windows.Controls.MenuItem
                {
                    Header = Translations.Get("Panel.QuickLaunch.Empty"),
                    IsEnabled = false
                });
                return;
            }

            foreach (var entry in entries)
            {
                var item = new System.Windows.Controls.MenuItem { Header = entry.DisplayName };
                item.Click += (_, _) => QuickLaunchRequested?.Invoke(entry);
                QuickLaunchMenuItem.Items.Add(item);
            }
        }

        private void UpdateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.IsUpdateMenuVisible == true)
                OpenUpdateSettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ReloadTaskbarHostMenuItem_Click(object sender, RoutedEventArgs e) =>
            ReloadTaskbarHostRequested?.Invoke(this, EventArgs.Empty);
    }
}
