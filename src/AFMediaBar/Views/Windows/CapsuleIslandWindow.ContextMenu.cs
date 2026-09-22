using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Windows;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 胶囊岛的右键菜单：切换媒体源、重连 SMTC、打开完整层、设置与退出。
/// Context menu of the capsule island: switch media source, reconnect SMTC, open the full panel, settings, and exit.
///
/// 菜单用代码构造而不是写在 XAML 里，理由与任务栏托盘菜单一致：它没有 PlacementTarget（由 Shell 消息或窗口右键
/// 直接弹出），而且"切换媒体源"的子项要先按最新会话列表重建——这些都在代码里做最直接。
/// The menu is built in code rather than declared in XAML for the same reason the taskbar tray menu is: it has no
/// PlacementTarget (it is popped directly by a Shell message or a window right-click), and the "switch media source"
/// children have to be rebuilt from the latest session list first — both are most direct in code.
/// </summary>
public partial class CapsuleIslandWindow
{
    private ContextMenu? _islandMenu;
    private Wpf.Ui.Controls.MenuItem? _sessionsMenuItem;
    private IReadOnlyList<MediaSessionOption> _sessionOptions = [];
    private Wpf.Ui.Controls.MenuItem? _reconnectMenuItem;
    private Wpf.Ui.Controls.MenuItem? _openFullPanelMenuItem;
    private Wpf.Ui.Controls.MenuItem? _settingsMenuItem;
    private Wpf.Ui.Controls.MenuItem? _exitMenuItem;

    /// <summary>右键菜单请求打开完整层（<c>TaskbarFullPanelWindow</c>）。宿主负责给它锚点与目标显示器，见 MainWindow。
    /// The context menu asks for the full panel (<c>TaskbarFullPanelWindow</c>); the host supplies its anchor and target display, see MainWindow.</summary>
    public event EventHandler? OpenFullPanelRequested;

    /// <summary>岛所在显示器的稳定设备标识：完整层按它选择工作区与 DPI。/ Stable device identifier of the display the island sits on; the full panel uses it for its work area and DPI.</summary>
    public string TargetMonitorDeviceId =>
        MonitorUtil.GetMonitor(new WindowInteropHelper(this).Handle).deviceId;

    /// <summary>
    /// 岛窗口的屏幕 DIP 边界，供完整层定位；与 <c>TaskbarWindow.GetMediaBarScreenBounds</c> 同一套换算。
    /// The island window's screen DIP bounds, used to place the full panel; the same conversion
    /// <c>TaskbarWindow.GetMediaBarScreenBounds</c> performs.
    /// </summary>
    public Rect GetMediaBarScreenBounds()
    {
        var point = PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = double.IsNaN(ActualWidth) || ActualWidth <= 0 ? Width : ActualWidth;
        var height = double.IsNaN(ActualHeight) || ActualHeight <= 0 ? Height : ActualHeight;
        return new Rect(point.X / dpi.DpiScaleX, point.Y / dpi.DpiScaleY, width, height);
    }

    /// <summary>全局左键落在菜单及其已展开子菜单之外时关闭岛菜单。/ Closes the island menu when a global left click lands outside it and any open submenu.</summary>
    public void CloseIslandMenuIfOutside(int screenX, int screenY)
    {
        if (_islandMenu is { } menu)
            ContextMenuHelper.CloseIfOutside(menu, screenX, screenY);
    }

    /// <summary>关闭岛右键菜单（宿主退出时用，Popup 的 HWND 不在 Application.Windows 里，必须显式关）。
    /// Closes the island context menu (used during host shutdown: a Popup's HWND is not an Application.Windows entry and has to be closed explicitly).</summary>
    internal void CloseIslandMenu()
    {
        if (_islandMenu is { } menu)
            menu.IsOpen = false;
    }

    /// <summary>
    /// 用最新会话列表重建"切换媒体源"子菜单。
    /// Rebuilds the "switch media source" submenu from the latest session list.
    /// </summary>
    /// <param name="options">当前 SMTC 会话选项。/ Current SMTC session options.</param>
    public void ApplySessions(IReadOnlyList<MediaSessionOption> options)
    {
        _sessionOptions = options;
        RebuildSessionItems();
    }

    private void RebuildSessionItems()
    {
        if (_sessionsMenuItem is not { } sessions || _isClosing)
            return;

        sessions.Items.Clear();
        foreach (var option in _sessionOptions)
        {
            sessions.Items.Add(new Wpf.Ui.Controls.MenuItem
            {
                Header = option.DisplayName,
                IsCheckable = true,
                IsChecked = option.IsSelected,
                Command = _viewModel.SelectMediaSessionCommand,
                CommandParameter = option.Key,
            });
        }
    }

    /// <summary>
    /// 构造岛右键菜单并接到窗口上：材质/主题走 <see cref="WindowAppearanceService"/>（与任务栏菜单同一处接线），
    /// 外部左键关闭同时注册 WPF 原生回调与宿主的全局鼠标监听。
    /// Builds the island context menu and wires it to the window: material and theme go through
    /// <see cref="WindowAppearanceService"/> (the same wiring the taskbar menu uses), and outside-click dismissal is
    /// registered both natively and through the host's global mouse observation.
    /// </summary>
    private void InitializeIslandMenu(WindowAppearanceService appearanceService)
    {
        var menu = new ContextMenu
        {
            Width = 240,
            Padding = new Thickness(4),
            StaysOpen = false,
            BorderThickness = new Thickness(1),
            DataContext = _viewModel,
        };
        menu.SetResourceReference(Control.FontFamilyProperty, "AppTextFontFamily");
        menu.SetResourceReference(Control.FontWeightProperty, "AppTextFontWeight");
        menu.SetResourceReference(Control.BackgroundProperty, "AppMenuBackgroundBrush");
        menu.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        menu.SetResourceReference(Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        _sessionsMenuItem = new Wpf.Ui.Controls.MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.MusicNote216 },
        };
        _reconnectMenuItem = new Wpf.Ui.Controls.MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowSync24 },
            Command = _viewModel.ReconnectMediaSessionCommand,
        };
        _openFullPanelMenuItem = new Wpf.Ui.Controls.MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.FullScreenMaximize24 },
        };
        _openFullPanelMenuItem.Click += (_, _) =>
        {
            CloseIslandMenu();
            OpenFullPanelRequested?.Invoke(this, EventArgs.Empty);
        };
        _settingsMenuItem = new Wpf.Ui.Controls.MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
            Command = _viewModel.OpenSettingsCommand,
        };
        _exitMenuItem = new Wpf.Ui.Controls.MenuItem
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowExit20 },
            Command = _viewModel.ExitApplicationCommand,
        };

        menu.Items.Add(_sessionsMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_reconnectMenuItem);
        menu.Items.Add(_openFullPanelMenuItem);
        menu.Items.Add(_settingsMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_exitMenuItem);

        // 菜单每次打开前重取一次文案与子项：语言可以在两次打开之间被切换，会话列表也可能已经变了。
        // Text and children are re-read every time the menu opens: the language can change between two openings and the
        // session list may have moved on.
        menu.Opened += (_, _) =>
        {
            // 右键菜单与音频浮层不同时存在：两个 Popup 抢着置顶会互相盖住。
            // The context menu and the audio flyout never coexist: two topmost popups would cover each other.
            if (_compactFlyout?.IsVisible == true)
                _compactFlyout.Dismiss();

            ApplyMenuTexts();
            RebuildSessionItems();
        };

        ContextMenuHelper.AttachOutsideClickDismissal(menu);
        appearanceService.Attach(menu, this);
        _islandMenu = menu;
        ContextMenu = menu;
    }

    /// <summary>
    /// 重新取菜单文案。所有标题都是代码拼出来的（不是 XAML 里的动态资源），因此语言变化后必须重取一次。
    /// Re-reads the menu's texts. Every title is composed in code rather than being a dynamic XAML resource, so a language
    /// change has to be picked up explicitly.
    /// </summary>
    private void ApplyMenuTexts()
    {
        if (_sessionsMenuItem is { } sessions)
            sessions.Header = Translations.Get("Common.SwitchMediaSource");
        if (_reconnectMenuItem is { } reconnect)
            reconnect.Header = Translations.Get("Common.ReconnectSmtc");
        if (_openFullPanelMenuItem is { } fullPanel)
            fullPanel.Header = Translations.Get("Common.OpenFullPanel");
        if (_settingsMenuItem is { } settings)
            settings.Header = Translations.Get("Common.Settings");
        if (_exitMenuItem is { } exit)
            exit.Header = Translations.Get("Common.Exit");
    }
}
