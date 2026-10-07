using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Components;
using AFMediaBar.Resources;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using Wpf.Ui.Animations;
using AFMediaBar.Views.Pages;

namespace AFMediaBar.Views.Windows
{
    /// <summary>
    /// 显示应用设置页面并承载 WPF-UI 导航。
    /// Displays application settings pages and hosts WPF-UI navigation.
    /// </summary>
    public partial class SettingsWindow : INavigationWindow, INotifyPropertyChanged
    {
        private Panel? _transitionContent;
        private readonly SettingsSidebarSelectionAnimator _sidebarSelection;
        private readonly AppIconService _appIconService;
        private readonly IServiceScope _sessionScope;
        private readonly SettingsContextService _contexts;
        private readonly SettingsPageProvider _pages;
        private readonly AppLogService _log;
        private SettingsContext _displayedContext = SettingsContext.Initial;
        private SettingsMode? _navigationMode;
        private SettingsPageKey _currentPage = SettingsPageKey.DisplayModes;
        private bool _updatingMonitorChoices;
        private bool _closed;

        public SettingsWindowViewModel ViewModel { get; }

        /// <summary>Focuses the search surface, at every window width.</summary>
        public ICommand FocusSearchCommand { get; }

        /// <summary>
        /// 标题栏图标，随主题在两套图形之间切换。
        /// The title bar icon, which swaps between the two artworks with the theme.
        /// </summary>
        public ImageSource? TitleBarIconSource => _appIconService.ResolveImageSource();

        /// <summary>属性变化通知：目前只有标题栏图标会在窗口存活期间改变。/ Property change notifications; only the title bar icon changes while the window lives.</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 创建设置窗口并连接导航和外观服务。
        /// Creates the settings window and connects navigation and appearance services.
        /// </summary>
        public SettingsWindow(
            SettingsWindowViewModel viewModel,
            IServiceScopeFactory scopeFactory,
            AppLogService log,
            WindowAppearanceService appearanceService,
            AppIconService appIconService
        )
        {
            ViewModel = viewModel;
            FocusSearchCommand = new RelayCommand(FocusSearch);
            _appIconService = appIconService;
            _log = log;
            _sessionScope = scopeFactory.CreateScope();
            _contexts = _sessionScope.ServiceProvider.GetRequiredService<SettingsContextService>();
            _pages = _sessionScope.ServiceProvider.GetRequiredService<SettingsPageProvider>();
            DataContext = this;

            InitializeComponent();
            _sidebarSelection = new SettingsSidebarSelectionAnimator(RootNavigation, SidebarSelectionOverlay);
            SettingsResetDialog.SetHost(RootContentDialog, _contexts.CancellationToken);
            appearanceService.Attach(this);
            SetPageService(_pages);
            RebuildNavigation();
            _contexts.Changing += OnContextChanging;
            _contexts.Changed += OnContextChanged;
            _contexts.RefreshFailed += OnContextRefreshFailed;

            // A single content transition is added separately from the sidebar layout.
            RootNavigation.Transition = Transition.None;
            RootNavigation.TransitionDuration = 0;

            AddHandler(ApplicationPage.OpenHighlightsEvent, new RoutedEventHandler(OnOpenHighlightsRequested));
            AddHandler(AppearancePage.OpenApplicationFontsEvent, new RoutedEventHandler(OnOpenApplicationFontsRequested));
            RootNavigation.Navigated += OnNavigated;

            // 落地页：窗口每次打开都直接落在「显示模式」。
            //
            // 不发起导航时内容区是空的：导航控件在收到第一个导航请求之前不会创建任何页面，用户打开设置看到的就是一片
            // 空白。这里挂在"窗口变为可见"而不是构造函数上——构造函数早于 Show，那时导航视图还没有内容宿主，立刻导航
            // 等于什么都没发生（MainWindow 里的更新跳转也踩过这一条）；再排到 Loaded 之后，等布局完成。
            // 由打开方发起的导航（例如更新提示要落在「更新亮点」）排在本次之后，因此仍然由它决定最终页面。
            // Landing page: the window lands straight on "display modes" every time it opens.
            //
            // Without a navigation request the content area is empty: the navigation control creates no page until the first request
            // arrives, so opening the settings shows nothing at all. This hangs off "window became visible" rather than the
            // constructor, because the constructor runs before Show, when the navigation view has no content host yet and navigating
            // does nothing (the update jump in MainWindow hit the same thing); the call is then queued behind Loaded so layout has
            // finished. A navigation issued by whoever opened the window — the update notice lands on "release highlights" — is queued after
            // this one and therefore still decides the final page.
            IsVisibleChanged += OnVisibilityChangedForLandingPage;

            // 更新提示的订阅与显示放在窗口构造的最后：它依赖 InitializeComponent 创建好的导航项。
            // The update notice subscribes and renders at the end of the constructor, because it depends on the
            // navigation item created by InitializeComponent.
            ViewModel.Subscribe();
            ViewModel.Refresh();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ApplyUpdateNotice();
            _appIconService.IconChanged += OnApplicationIconChanged;
            Closed += SettingsWindow_ClosedForUpdateNotice;
        }

        #region Landing page

        /// <summary>
        /// 窗口从隐藏变为可见时导航到落地页；排到 Loaded 优先级等待导航视图的内容宿主就绪。
        /// Navigates to the landing page when the window becomes visible, queued at Loaded priority so the navigation view's content
        /// host exists first.
        /// </summary>
        private void OnVisibilityChangedForLandingPage(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => { if (!_closed) Navigate(typeof(DisplayModesPage)); }));
        }

        #endregion Landing page

        #region Window icon

        /// <summary>
        /// 主题变化后重新发布标题栏图标；窗口图标本身由外观服务写到 <c>Window.Icon</c> 上。
        /// Republishes the title bar icon after a theme change; the window icon itself is written to <c>Window.Icon</c> by the
        /// appearance service.
        /// </summary>
        private void OnApplicationIconChanged() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TitleBarIconSource)));

        #endregion Window icon

        #region Search

        /// <summary>
        /// 按当前输入过滤候选并直接写入 ItemsSource。
        ///
        /// 上一版把候选写进 <c>OriginalItemsSource</c>，让控件自己再按文本过滤一遍；那层过滤只比较候选项
        /// 自身的文本，于是所有靠关键词命中的条目都会被它丢掉，当时只能把关键词拼进候选文案里绕开。
        /// 自己过滤、自己控制下拉框开合之后，候选文案就只是一句人话。
        /// Filters the suggestions for the current input and writes ItemsSource directly.
        ///
        /// The previous version wrote them into <c>OriginalItemsSource</c> and let the control filter again; that
        /// filter only compares an item's own text, so every keyword hit was dropped and the keyword had to be
        /// spliced into the label to survive. Filtering here keeps a label a plain phrase.
        /// </summary>
        private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            // The control filters OriginalItemsSource after raising this event unless it is handled.
            args.Handled = true;
            var hits = SettingsSearchPolicy.Search(args.Text, SettingsSearchIndex.ForContext(_contexts.Current));
            var suggestions = new List<SettingsSearchSuggestion>(hits.Count);
            foreach (var hit in hits)
            {
                suggestions.Add(new SettingsSearchSuggestion(hit));
            }

            sender.ItemsSource = suggestions;
            sender.IsSuggestionListOpen = suggestions.Count > 0;
        }

        /// <summary>
        /// 选中候选后先导航到目标页面，再让该页的分组标签条滚到命中的分组并脉冲一次。
        /// 跳转必须等到新页面完成布局，否则分组位置还是上一次的。
        /// Selecting a suggestion navigates to the page and then asks that page's group strip to scroll to the
        /// matching group and pulse it. The jump waits for the new page to finish layout, because group positions
        /// would otherwise still belong to the previous page.
        /// </summary>
        private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is SettingsSearchSuggestion suggestion)
                NavigateToGroup(suggestion.Hit.Page, suggestion.Hit.GroupId);
        }

        private void NavigateToGroup(SettingsPageKey page, string groupId)
        {
            if (!SettingsPageProvider.PageTypes.TryGetValue(page, out var pageType) || !Navigate(pageType)) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_closed || _currentPage != page) return;
                FindDescendant<SettingsGroupStrip>(RootNavigation)?.RevealGroup(groupId);
            }));
        }

        /// <summary>
        /// 搜索候选项：主文案是「页面 › 分组」，副文案说明该分组能改什么。
        ///
        /// 主文案的拼装样式（分隔符与两侧顺序）取自语言文件，两个名称则已经分别是当前
        /// 语言的页面名与分组名，因此候选行不会拼出半句旧语言。
        /// Search suggestion: the primary line is "page › group" and the secondary line says what it changes.
        ///
        /// The pattern of the primary line, its separator and the order of its two sides, comes from the language file,
        /// while each name is already a page name and a group name in the active language, so a suggestion line can
        /// never be composed half in the old language.
        /// </summary>
        private sealed class SettingsSearchSuggestion
        {
            public SettingsSearchSuggestion(SettingsSearchHit hit) => Hit = hit;

            /// <summary>命中的分组。/ The matched group.</summary>
            public SettingsSearchHit Hit { get; }

            /// <summary>主文案。/ Primary line.</summary>
            public string Title => Translations.Format("Settings.Search.Suggestion.Title", Hit.PageTitle, Hit.Title);

            /// <summary>副文案。/ Secondary line.</summary>
            public string Subtitle => Hit.Description;

            /// <summary>Uses the localized label when the search control copies a chosen suggestion into its input.</summary>
            public override string ToString() => Title;
        }

        /// <summary>按深度优先在可视树中查找第一个指定类型后代。/ Finds the first descendant of a type, depth first.</summary>
        private static T? FindDescendant<T>(DependencyObject root)
            where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match)
                {
                    return match;
                }

                if (FindDescendant<T>(child) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }

        #endregion Search

        #region Update notice

        /// <summary>
        /// 亮点入口始终可用；有新版本时使用视图模型发布的版本提示。
        ///
        /// 视图模型是单例、窗口是 Transient，因此订阅必须在关闭时退订，否则反复开关设置窗口会累积处理器。
        /// The highlights entry is always available; a newer version changes its notice.
        ///
        /// The view model is a singleton and the window is transient, so the subscription must be released on close;
        /// otherwise opening the settings repeatedly would accumulate handlers.
        /// </summary>
        private void OnOpenApplicationFontsRequested(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            NavigateToGroup(SettingsPageKey.ApplicationAppearance, "Common.Group.Fonts");
        }

        private void OnOpenHighlightsRequested(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Navigate(typeof(ReleaseHighlightsPage));
        }

        private void UpdateNoticeNavItem_Click(object sender, RoutedEventArgs e)
        {
            // 更新入口只展示版本亮点；下载和安装仍由应用页负责。
            Navigate(typeof(ReleaseHighlightsPage));
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(SettingsWindowViewModel.HasUpdateAvailable) or nameof(SettingsWindowViewModel.UpdateNoticeTitle) or nameof(SettingsWindowViewModel.UpdateNoticeText))
            {
                ApplyUpdateNotice();
            }
        }

        private void ApplyUpdateNotice()
        {
            UpdateNoticeDot.Visibility = ViewModel.HasUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
            UpdateNoticeNavItem.ToolTip = ViewModel.HasUpdateAvailable
                ? ViewModel.UpdateNoticeText
                : Translations.Get("ReleaseHighlights.Header.Subtitle");
        }

        private void SettingsWindow_ClosedForUpdateNotice(object? sender, EventArgs e)
        {
            _closed = true;
            CommitCurrentEdits();
            _contexts.Changing -= OnContextChanging;
            _contexts.Changed -= OnContextChanged;
            _contexts.RefreshFailed -= OnContextRefreshFailed;
            SettingsResetDialog.ClearHost(RootContentDialog);
            _sessionScope.Dispose();
            _sidebarSelection.Dispose();
            SettingsRevealAnimator.Cancel(_transitionContent);
            _transitionContent = null;
            RootNavigation.Navigated -= OnNavigated;
            ViewModel.Unsubscribe();
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _appIconService.IconChanged -= OnApplicationIconChanged;
            Closed -= SettingsWindow_ClosedForUpdateNotice;
        }

        #endregion Update notice

        #region INavigationWindow methods

        /// <summary>返回设置窗口导航控件。/ Returns the settings navigation control.</summary>
        public INavigationView GetNavigation() => RootNavigation;

        /// <summary>导航到指定页面类型。/ Navigates to the specified page type.</summary>
        public bool Navigate(Type pageType)
        {
            return RootNavigation.Navigate(pageType);
        }

        private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
        {
            if (args.Page is Page destination) _currentPage = SettingsPageProvider.PageTypes.First(pair => pair.Value == destination.GetType()).Key;
            UpdateContextHeader();
            SettingsRevealAnimator.Cancel(_transitionContent);
            _transitionContent = null;
            if (args.Page is Page page && page.FindName("PageScroll") is ScrollViewer { Content: Panel content })
            {
                _transitionContent = content;
                SettingsRevealAnimator.Replay(content);
            }
        }

        private void FocusSearch()
        {
            RootNavigation.IsPaneOpen = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsVisible) AutoSuggestBox.FocusCommand.Execute(null);
            }));
        }

        /// <summary>设置导航页面提供器。/ Sets the navigation page provider.</summary>
        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
            RootNavigation.SetPageProviderService(navigationViewPageProvider);

        /// <summary>显示设置窗口。/ Shows the settings window.</summary>
        public void ShowWindow() => Show();

        /// <summary>关闭设置窗口。/ Closes the settings window.</summary>
        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        INavigationView INavigationWindow.GetNavigation() => RootNavigation;

        /// <summary>
        /// 为导航页提供应用组合根；该兼容入口只在设置窗口初始化期间调用。
        /// Supplies the application composition root to navigation pages; this compatibility entry point is used only during settings-window initialization.
        /// </summary>
        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }

        private async void SettingsWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            RootNavigation.Navigate(typeof(DisplayModesPage));
            await _contexts.StartAsync();
        }

        private void RebuildNavigation()
        {
            if (_navigationMode == _contexts.Current.Mode) return;
            _navigationMode = _contexts.Current.Mode;
            RootNavigation.MenuItems.Clear();
            RootNavigation.FooterMenuItems.Clear();
            foreach (var definition in SettingsPageCatalog.ForMode(_contexts.Current.Mode))
            {
                var item = definition.Key == SettingsPageKey.ReleaseHighlights ? UpdateNoticeNavItem : new NavigationViewItem
                {
                    TargetPageType = SettingsPageProvider.PageTypes[definition.Key],
                    Icon = new SymbolIcon(Enum.Parse<SymbolRegular>(definition.IconName))
                };
                if (definition.Key != SettingsPageKey.ReleaseHighlights) item.SetResourceReference(ContentControl.ContentProperty, "Loc." + definition.TitleKey);
                item.NavigationCacheMode = NavigationCacheMode.Disabled;
                if (definition.IsFooter) RootNavigation.FooterMenuItems.Add(item);
                else RootNavigation.MenuItems.Add(item);
            }
        }

        private void OnContextChanging(object? sender, EventArgs e)
        {
            CommitCurrentEdits();
            SettingsRevealAnimator.Cancel(_transitionContent);
            _pages.DeactivateContext();
        }
        private void CommitCurrentEdits()
        {
            foreach (var editor in Descendants(RootNavigation).OfType<SettingsNumericEditor>()) editor.TryCommit();
            Keyboard.ClearFocus();
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
        private void OnContextChanged(object? sender, EventArgs e)
        {
            if (_closed) return;
            var contextChanged = _displayedContext != _contexts.Current;
            _pages.SetContext(_contexts.Current);
            RebuildNavigation();
            _updatingMonitorChoices = true;
            try
            {
                EditingMonitorPicker.ItemsSource = _contexts.Monitors;
                EditingMonitorPicker.SelectedValue = _contexts.Current.MonitorDeviceId;
                EditingMonitorPanel.Visibility = _contexts.Monitors.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { _updatingMonitorChoices = false; }
            var definition = SettingsPageCatalog.Find(_currentPage, _contexts.Current.Mode);
            if (definition is null) Navigate(typeof(DisplayModesPage));
            else if (contextChanged && (!definition.IsGlobal || _currentPage == SettingsPageKey.DisplayModes))
            {
                RootNavigation.ReplaceContent((UIElement)_pages.GetPage(SettingsPageProvider.PageTypes[_currentPage])!);
            }
            _displayedContext = _contexts.Current;
            UpdateContextHeader();
            AutoSuggestBox.IsSuggestionListOpen = false;
            AutoSuggestBox.ItemsSource = null;
        }
        private void UpdateContextHeader()
        {
            var definition = SettingsPageCatalog.Find(_currentPage, _contexts.Current.Mode);
            RootNavigation.HeaderVisibility = _contexts.Monitors.Count > 1 &&
                (definition is { IsGlobal: false } || _currentPage == SettingsPageKey.DisplayModes) ? Visibility.Visible : Visibility.Collapsed;
        }
        private void OnEditingMonitorChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updatingMonitorChoices && EditingMonitorPicker.SelectedValue is string deviceId) _contexts.SelectMonitor(deviceId);
        }
        private void OnContextRefreshFailed(object? sender, Exception exception) => _log.Error("Settings", "读取设置页任务栏环境失败，保留上次有效上下文", exception);
    }
}
