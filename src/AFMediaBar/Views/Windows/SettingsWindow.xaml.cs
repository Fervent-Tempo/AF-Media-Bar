using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
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
        private SettingsPageKey _currentPage = SettingsPageKey.QuickStart;
        private readonly SettingsCategoryPage _categoryPage;
        private string? _currentSectionId;
        private readonly Dictionary<string, NavigationViewItem> _sectionItems = new();
        private bool _closed;
        private bool _returning;
        private readonly Stack<NavigationDestination> _history = new();
        private sealed record NavigationDestination(SettingsPageKey? Page, string? SectionId);
        private NavigationDestination CurrentDestination => new(_currentSectionId is null ? _currentPage : null, _currentSectionId);
        public RelayCommand GoBackCommand { get; }
        public RelayCommand GoToParentCommand { get; }

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
            GoBackCommand = new RelayCommand(GoBack, () => _history.Count > 0);
            GoToParentCommand = new RelayCommand(() =>
            {
                if (SettingsNavigationCatalog.SectionFor(_currentPage) is { } section) NavigateSection(section.Id);
            });
            _appIconService = appIconService;
            _log = log;
            _sessionScope = scopeFactory.CreateScope();
            _contexts = _sessionScope.ServiceProvider.GetRequiredService<SettingsContextService>();
            _pages = _sessionScope.ServiceProvider.GetRequiredService<SettingsPageProvider>();
            _categoryPage = _sessionScope.ServiceProvider.GetRequiredService<SettingsCategoryPage>();
            DataContext = this;

            InitializeComponent();
            SourceInitialized += OnInitialWindowSize;
            _sidebarSelection = new SettingsSidebarSelectionAnimator(RootNavigation, SidebarSelectionOverlay);
            SettingsResetDialog.SetHost(RootContentDialog, _contexts.CancellationToken);
            ArtistSeparatorsEditor.SetHost(RootContentDialog, _contexts.CancellationToken);
            PlayerSupportDialog.SetHost(RootContentDialog, _contexts.CancellationToken);
            appearanceService.Attach(this);
            SetPageService(_pages);
            RebuildNavigation();
            _contexts.Changing += OnContextChanging;
            _contexts.Changed += OnContextChanged;
            _contexts.RefreshFailed += OnContextRefreshFailed;

            // A single content transition is added separately from the sidebar layout.
            RootNavigation.Transition = Transition.None;
            RootNavigation.TransitionDuration = 0;

            AddHandler(AppearancePage.OpenApplicationFontsEvent, new RoutedEventHandler(OnOpenApplicationFontsRequested));
            AddHandler(AFMediaBar.Components.SettingsNavigationButton.NavigateEvent, new RoutedEventHandler(OnRelatedSettingsRequested));
            RootNavigation.Navigated += OnNavigated;

            // 默认首页等待内容宿主就绪；调用方排在其后的显式导航仍然优先。
            IsVisibleChanged += OnVisibilityChangedForLandingPage;

            // 更新提示的订阅与显示放在窗口构造的最后：它依赖 InitializeComponent 创建好的导航项。
            // The update notice subscribes and renders at the end of the constructor, because it depends on the
            // navigation item created by InitializeComponent.
            ViewModel.Subscribe();
            ViewModel.Refresh();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ApplyUpdateNotice();
            _appIconService.IconChanged += OnApplicationIconChanged;
            Translations.LanguageChanged += OnHeaderLanguageChanged;
            Closed += SettingsWindow_ClosedForUpdateNotice;
        }

        #region Landing page

        private void OnInitialWindowSize(object? sender, EventArgs args)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var info = new AFMediaBar.Classes.Interop.NativeMethods.MONITORINFOEX();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<AFMediaBar.Classes.Interop.NativeMethods.MONITORINFOEX>();
            var monitor = AFMediaBar.Classes.Interop.NativeMethods.MonitorFromWindow(handle, 2);
            if (!AFMediaBar.Classes.Interop.NativeMethods.GetMonitorInfo(monitor, ref info)) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            var availableWidth = Math.Max(1, (info.rcWork.Right - info.rcWork.Left) / dpi.DpiScaleX - 32);
            var availableHeight = Math.Max(1, (info.rcWork.Bottom - info.rcWork.Top) / dpi.DpiScaleY - 32);
            MinWidth = Math.Min(MinWidth, availableWidth);
            MinHeight = Math.Min(MinHeight, availableHeight);
            Width = Math.Clamp(Width, MinWidth, availableWidth);
            Height = Math.Clamp(Height, MinHeight, availableHeight);
        }

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
                new Action(() => { if (!_closed) Navigate(typeof(QuickStartPage)); }));
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
        /// 必须设置 <c>args.Handled = true</c>：否则 WPF-UI 的 AutoSuggestBox 在事件返回后会继续执行内部的
        /// DefaultFiltering，而本项目未填 OriginalItemsSource，内部过滤会立刻把 ItemsSource 覆写为空列表。
        /// Filters the suggestions for the current input and writes ItemsSource directly.
        ///
        /// The previous version wrote them into <c>OriginalItemsSource</c> and let the control filter again; that
        /// filter only compares an item's own text, so every keyword hit was dropped and the keyword had to be
        /// spliced into the label to survive. Filtering here keeps a label a plain phrase.
        /// Setting <c>args.Handled = true</c> is required: otherwise WPF-UI's AutoSuggestBox continues into its
        /// internal DefaultFiltering after the event, which reads empty OriginalItemsSource and clears ItemsSource.
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
            args.Handled = true;
        }

        /// <summary>导航到命中的唯一编辑位置，完成布局后展开分组并聚焦具体选项。</summary>
        private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is SettingsSearchSuggestion suggestion)
                NavigateToGroup(suggestion.Hit.Page, suggestion.Hit.GroupId, suggestion.Hit.OptionId);
        }

        /// <summary>通过稳定页面与分组标识定位设置，供搜索、快捷入口和系统通知复用。</summary>
        public void NavigateToGroup(SettingsPageKey page, string groupId, string? optionId = null)
        {
            if (_closed) return;
            (page, groupId) = SettingsPageCatalog.ResolveDestination(page, groupId);
            if (!SettingsPageProvider.PageTypes.TryGetValue(page, out var pageType) || !Navigate(pageType)) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_closed || _currentPage != page) return;
                FindDescendant<SettingsGroupStrip>(RootNavigation)?.RevealGroup(groupId, optionId);
            }));
        }

        /// <summary>使用当前语言呈现页面路径、选项名称与说明。</summary>
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
            ArtistSeparatorsEditor.ClearHost(RootContentDialog);
            PlayerSupportDialog.ClearHost(RootContentDialog);
            _sessionScope.Dispose();
            _sidebarSelection.Dispose();
            SettingsRevealAnimator.Cancel(_transitionContent);
            _transitionContent = null;
            RootNavigation.Navigated -= OnNavigated;
            ViewModel.Unsubscribe();
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _appIconService.IconChanged -= OnApplicationIconChanged;
            Translations.LanguageChanged -= OnHeaderLanguageChanged;
            Closed -= SettingsWindow_ClosedForUpdateNotice;
        }

        #endregion Update notice

        #region INavigationWindow methods

        /// <summary>返回设置窗口导航控件。/ Returns the settings navigation control.</summary>
        public INavigationView GetNavigation() => RootNavigation;

        /// <summary>导航到指定页面类型。/ Navigates to the specified page type.</summary>
        public bool Navigate(Type pageType)
        {
            var resolved = SettingsPageProvider.ResolvePageType(pageType);
            if (!RootNavigation.Navigate(resolved)) return false;
            HighlightSidebarItem(resolved);
            return true;
        }

        /// <summary>
        /// 程序化导航之后把左侧高亮挪到对应菜单项。NavigationView 的 SelectedItem 只在用户点击时更新，
        /// 快捷跳转、搜索跳转与系统通知跳转走的都是程序化导航，不补这一下，高亮与强调色会留在原来那一页。
        /// Moves the sidebar highlight onto the matching menu item after a programmatic navigation. NavigationView
        /// only updates SelectedItem when the user clicks, so shortcut, search and notification jumps would leave
        /// the highlight and accent colours on the previous page.
        /// </summary>
        private void HighlightSidebarItem(Type pageType)
        {
            var item = EnumerateNavigationItems(RootNavigation.MenuItems.OfType<NavigationViewItem>())
                .Concat(RootNavigation.FooterMenuItems.OfType<NavigationViewItem>())
                .FirstOrDefault(candidate => candidate.TargetPageType == pageType);
            _sidebarSelection.Highlight(item);
        }

        private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
        {
            if (args.Page is SettingsCategoryPage category)
                SetDestination(new(null, category.SectionId));
            else if (args.Page is Page destination)
            {
                var next = SettingsPageProvider.PageTypes.First(pair => pair.Value == destination.GetType()).Key;
                SetDestination(new(next, null));
                HighlightSidebarItem(destination.GetType());
            }
            ReplayPageReveal(args.Page as Page);
        }

        private void SetDestination(NavigationDestination next)
        {
            if (next != CurrentDestination && !_returning) _history.Push(CurrentDestination);
            if (next.Page is { } page) _currentPage = page;
            _currentSectionId = next.SectionId;
            GoBackCommand.NotifyCanExecuteChanged();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdatePageHeader));
            ExpandCurrentSection();
        }

        private void NavigateSection(string sectionId)
        {
            var section = SettingsNavigationCatalog.Sections.FirstOrDefault(section => section.Id == sectionId);
            if (_closed || section is null) return;
            if (_currentSectionId == sectionId)
            {
                ExpandCurrentSection();
                return;
            }
            _categoryPage.ShowSection(section, _contexts.Current.Mode);
            if (!RootNavigation.Navigate(typeof(SettingsCategoryPage))) RootNavigation.ReplaceContent(_categoryPage);
            SetDestination(new(null, section.Id));
            if (_sectionItems.TryGetValue(section.Id, out var item)) _sidebarSelection.Highlight(item);
            ReplayPageReveal(_categoryPage);
        }

        private void ReplayPageReveal(Page? page)
        {
            SettingsRevealAnimator.Cancel(_transitionContent);
            _transitionContent = null;
            if (page is not null &&
                (page.FindName("PageScroll") as ScrollViewer ?? (page.FindName("PageLayout") as SettingsPageLayout)?.ScrollViewer) is { Content: Panel content })
            {
                _transitionContent = content;
                SettingsRevealAnimator.Replay(content);
            }
        }

        private void GoBack()
        {
            while (_history.TryPop(out var destination))
            {
                if (destination.Page is { } page && SettingsPageCatalog.Find(page, _contexts.Current.Mode) is null) continue;
                _returning = true;
                try
                {
                    if (destination.SectionId is { } sectionId) NavigateSection(sectionId);
                    else if (destination.Page is { } key) Navigate(SettingsPageProvider.PageTypes[key]);
                }
                finally { _returning = false; GoBackCommand.NotifyCanExecuteChanged(); }
                return;
            }
            GoBackCommand.NotifyCanExecuteChanged();
        }

        private void OnHeaderLanguageChanged(object? sender, EventArgs args)
        {
            UpdatePageHeader();
            AutoSuggestBox.IsSuggestionListOpen = false;
            AutoSuggestBox.ItemsSource = null;
        }
        private void UpdatePageHeader()
        {
            if (_closed || FindDescendant<SettingsGroupStrip>(RootNavigation) is not { } strip) return;
            strip.BackCommand = GoBackCommand;
            if (_currentSectionId is not null)
            {
                strip.ParentTitle = string.Empty;
                strip.ParentCommand = null;
                strip.StatusText = string.Empty;
                return;
            }
            var definition = SettingsPageCatalog.Find(_currentPage, _contexts.Current.Mode);
            if (definition is null) return;
            var section = SettingsNavigationCatalog.SectionFor(_currentPage);
            strip.ParentTitle = section is null || section.Pages.Count == 1 ? string.Empty : Translations.Get(section.TitleKey);
            strip.ParentCommand = GoToParentCommand;
            strip.StatusText = _currentPage == SettingsPageKey.QuickStart || definition.IsFooter ? string.Empty :
                Translations.Get(definition.IsGlobal && !definition.HasModeContent ? "Integration.Scope.Shared" :
                    _contexts.Current.Mode == SettingsMode.FloatingBar ? "Integration.Mode.Floating" : "Integration.Mode.Taskbar");
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
            if (!await SettingsPageActivation.WaitForPresentationAsync(this, _contexts.CancellationToken)) return;
            await _contexts.StartAsync();
        }

        private void RebuildNavigation()
        {
            if (_navigationMode == _contexts.Current.Mode) return;
            _navigationMode = _contexts.Current.Mode;
            RootNavigation.MenuItems.Clear();
            RootNavigation.FooterMenuItems.Clear();
            // 菜单项是重新建的，之前程序化指定的那一个已经不在树里，高亮交回给 SelectedItem。
            // The items are rebuilt, so the one named by an earlier programmatic navigation is gone; hand the
            // highlight back to SelectedItem.
            _sidebarSelection.Highlight(null);
            _sectionItems.Clear();
            var definitions = SettingsPageCatalog.ForMode(_contexts.Current.Mode);
            foreach (var section in SettingsNavigationCatalog.Sections)
            {
                var pages = section.Pages.Select(key => definitions.FirstOrDefault(page => page.Key == key))
                    .OfType<SettingsPageDefinition>().ToArray();
                if (pages.Length == 0) continue;
                if (pages.Length == 1)
                {
                    RootNavigation.MenuItems.Add(CreateNavigationItem(pages[0]));
                    continue;
                }
                var sectionItem = new NavigationViewItem
                {
                    Icon = new SymbolIcon(Enum.Parse<SymbolRegular>(section.IconName)),
                    FocusVisualStyle = null,
                    Command = new RelayCommand(() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        if (!_closed) NavigateSection(section.Id);
                    })))
                };
                sectionItem.PreviewMouseLeftButtonDown += OnSectionPreviewMouseLeftButtonDown;
                sectionItem.SetResourceReference(ContentControl.ContentProperty, "Loc." + section.TitleKey);
                foreach (var page in pages) sectionItem.MenuItems.Add(CreateNavigationItem(page, includeIcon: false));
                _sectionItems[section.Id] = sectionItem;
                RootNavigation.MenuItems.Add(sectionItem);
            }
            foreach (var definition in definitions.Where(page => page.IsFooter))
                RootNavigation.FooterMenuItems.Add(definition.Key == SettingsPageKey.ReleaseHighlights
                    ? UpdateNoticeNavItem : CreateNavigationItem(definition));
            ExpandCurrentSection();
            if (_currentSectionId is { } sectionId && _sectionItems.TryGetValue(sectionId, out var selectedSection))
                _sidebarSelection.Highlight(selectedSection);
            else HighlightSidebarItem(SettingsPageProvider.PageTypes[_currentPage]);
        }

        private static NavigationViewItem CreateNavigationItem(SettingsPageDefinition definition, bool includeIcon = true)
        {
            var item = new NavigationViewItem
            {
                TargetPageType = SettingsPageProvider.PageTypes[definition.Key],
                NavigationCacheMode = NavigationCacheMode.Disabled,
                FocusVisualStyle = null
            };
            if (includeIcon) item.Icon = new SymbolIcon(Enum.Parse<SymbolRegular>(definition.IconName));
            item.SetResourceReference(ContentControl.ContentProperty, "Loc." + definition.TitleKey);
            return item;
        }

        private void OnSectionPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
        {
            if (_currentSectionId is not { } sectionId || !_sectionItems.TryGetValue(sectionId, out var current) ||
                !ReferenceEquals(sender, current) ||
                current.Template.FindName("MainBorder", current) is not FrameworkElement { IsMouseOver: true } ||
                current.Template.FindName("PART_ChevronGrid", current) is not FrameworkElement chevron ||
                args.GetPosition(current).X >= current.ActualWidth - chevron.ActualWidth) return;

            // 当前分类标题保持不动；右侧箭头仍可独立收起或展开子项。
            current.Focus();
            args.Handled = true;
        }

        private void ExpandCurrentSection()
        {
            var current = _currentSectionId ?? SettingsNavigationCatalog.SectionFor(_currentPage)?.Id;
            foreach (var (id, item) in _sectionItems) item.IsExpanded = id == current;
        }

        private static IEnumerable<NavigationViewItem> EnumerateNavigationItems(IEnumerable<NavigationViewItem> items)
        {
            foreach (var item in items)
            {
                yield return item;
                foreach (var child in EnumerateNavigationItems(item.MenuItems.OfType<NavigationViewItem>())) yield return child;
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
            var definition = SettingsPageCatalog.Find(_currentPage, _contexts.Current.Mode);
            if (_currentSectionId is { } sectionId)
            {
                var section = SettingsNavigationCatalog.Sections.First(section => section.Id == sectionId);
                _categoryPage.ShowSection(section, _contexts.Current.Mode);
            }
            else if (definition is null) Navigate(typeof(ScreenAndPlacementPage));
            else if (contextChanged && (!definition.IsGlobal || definition.HasModeContent))
            {
                var content = (UIElement)_pages.GetPage(SettingsPageProvider.PageTypes[_currentPage])!;
                if (!definition.IsGlobal || _currentPage == SettingsPageKey.ScreenAndPlacement) RootNavigation.ReplaceContent(content);
            }
            _displayedContext = _contexts.Current;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdatePageHeader));
            AutoSuggestBox.IsSuggestionListOpen = false;
            AutoSuggestBox.ItemsSource = null;
        }
        private void OnRelatedSettingsRequested(object sender, RoutedEventArgs args)
        {
            if (args is not AFMediaBar.Components.SettingsNavigationEventArgs request) return;
            args.Handled = true;
            var page = request.Destination;
            var group = request.GroupId;
            var experiments = SettingsManager.Current.Experiments;
            if (page == SettingsPageKey.Application && group == "Memory.Title" && !experiments.MemoryTools)
                page = SettingsPageKey.Experimental;
            else if ((page is SettingsPageKey.DeviceManagement or SettingsPageKey.Widgets) && group == "Battery.Title" && !experiments.BluetoothBattery)
                page = SettingsPageKey.Experimental;
            if (!string.IsNullOrEmpty(group)) NavigateToGroup(page, group);
            else Navigate(SettingsPageProvider.PageTypes[page]);
        }
        private void OnContextRefreshFailed(object? sender, Exception exception) => _log.Error("Settings", "读取设置页任务栏环境失败，保留上次有效上下文", exception);
    }
}
