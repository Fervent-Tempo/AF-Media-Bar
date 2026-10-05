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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using Wpf.Ui.Animations;
using AFMediaBar.Views.Pages;
using Image = System.Windows.Controls.Image;

namespace AFMediaBar.Views.Windows
{
    /// <summary>
    /// 显示应用设置页面并承载 WPF-UI 导航。
    /// Displays application settings pages and hosts WPF-UI navigation.
    /// </summary>
    public partial class SettingsWindow : INavigationWindow, INotifyPropertyChanged
    {
        private Type? _currentPageType;
        private ToggleButton? _indicatorTargetTab;
        private double _indicatorTargetLeft;
        private double _indicatorTargetWidth;
        private readonly List<Image> _outgoingSnapshots = [];

        private readonly AppIconService _appIconService;
        /// <summary>
        /// 搜索命中到页面类型的映射。搜索索引刻意不引用任何 View 类型，
        /// 因此这层映射留在视图侧，索引只使用与视图解耦的页面键。
        /// Maps search hits to page types. The search index deliberately references no view type, so this mapping
        /// lives on the view side and the index uses only view-independent page keys.
        /// </summary>
        private static readonly IReadOnlyDictionary<SettingsPageKey, System.Type> SearchPageTypes =
            new Dictionary<SettingsPageKey, System.Type>
            {
                [SettingsPageKey.DisplayModes] = typeof(DisplayModesPage),
                [SettingsPageKey.MediaAndNotifications] = typeof(ExtraFeaturesPage),
                [SettingsPageKey.Components] = typeof(ComponentsSettingsPage),
                [SettingsPageKey.Interaction] = typeof(InteractionPage),
                [SettingsPageKey.Lyrics] = typeof(LyricsPage),
                [SettingsPageKey.Appearance] = typeof(AppearancePage),
                [SettingsPageKey.Application] = typeof(ApplicationPage),
                [SettingsPageKey.About] = typeof(AboutPage),
                [SettingsPageKey.ReleaseHighlights] = typeof(ReleaseHighlightsPage),
            };

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
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService,
            WindowAppearanceService appearanceService,
            AppIconService appIconService
        )
        {
            ViewModel = viewModel;
            FocusSearchCommand = new RelayCommand(FocusSearch);
            _appIconService = appIconService;
            DataContext = this;

            InitializeComponent();
            SettingsResetDialog.SetHost(RootContentDialog);
            appearanceService.Attach(this);
            SetPageService(navigationViewPageProvider);

            // Page contents and group labels animate separately, so the library must not move their shared background.
            RootNavigation.Transition = Transition.None;
            RootNavigation.TransitionDuration = 0;

            navigationService.SetNavigationControl(RootNavigation);
            RootNavigation.Navigating += (_, args) => CaptureOutgoingPage(args.Page);
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
                new Action(() => Navigate(typeof(DisplayModesPage))));
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
            var hits = SettingsSearchPolicy.Search(args.Text, SettingsSearchIndex.Entries);
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
            if (args.SelectedItem is not SettingsSearchSuggestion suggestion ||
                !SearchPageTypes.TryGetValue(suggestion.Hit.Page, out var pageType))
            {
                return;
            }

            var hit = suggestion.Hit;
            if (!Navigate(pageType))
            {
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    // 显示模式页的分组序号只在该模式的分区内成立，因此必须先切到命中所属的模式，
                    // 再让标签条跳到该分区内的分组序号。模式选择是页面自己的状态，因此交给页面处理，
                    // 而不是在这里从容器里取视图模型。
                    // A display-mode group index only holds inside its own mode's section, so the hit's mode is
                    // selected first and only then does the strip jump to that index within the section. Mode
                    // selection is the page's own state, so the page handles it rather than this window reaching
                    // into the container for a view model.
                    FindDescendant<DisplayModesPage>(RootNavigation)?.SelectMode(hit.Mode);
                    FindDescendant<SettingsGroupStrip>(RootNavigation)?.RevealGroup(hit.GroupIndex);
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
                if (string.IsNullOrEmpty(e.PropertyName))
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(SyncNavigation));
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
            ClearOutgoingSnapshots();
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
            var succeeded = RootNavigation.Navigate(pageType);
            if (!succeeded)
            {
                ClearOutgoingSnapshots();
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(SyncNavigation));
            }
            return succeeded;
        }

        private void CaptureOutgoingPage(object? destination)
        {
            ClearOutgoingSnapshots();
            if (destination?.GetType() == _currentPageType ||
                MotionPolicy.ResolveCurrent().Mode != MotionMode.Full ||
                FindDescendant<Page>(RootNavigation) is not { } oldPage)
                return;

            if (oldPage.FindName("PageScroll") is ScrollViewer scroll)
                AddOutgoingSnapshot(scroll, scroll.ViewportWidth);

            if (oldPage.Content is Grid root)
            {
                var tabs = root.Children.OfType<SettingsGroupStrip>().FirstOrDefault()?.GetTabViewport();
                if (tabs is not null) AddOutgoingSnapshot(tabs);
            }
        }

        private void AddOutgoingSnapshot(FrameworkElement source, double? clipWidth = null)
        {
            if (source.ActualWidth < 1d || source.ActualHeight < 1d) return;
            var dpi = VisualTreeHelper.GetDpi(source);
            var pixelWidth = (int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX);
            var pixelHeight = (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY);
            if (pixelWidth <= 0 || pixelHeight <= 0 || (long)pixelWidth * pixelHeight > 8_000_000) return;

            // Render only the visible scroll viewports; keeping full pages alive would disturb cached page ownership.
            try
            {
                var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight,
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bitmap.Render(source);
                bitmap.Freeze();
                var image = new Image
                {
                    Source = bitmap,
                    Width = source.ActualWidth,
                    Height = source.ActualHeight,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false
                };
                if (clipWidth is > 0d && clipWidth < source.ActualWidth)
                    image.Clip = new RectangleGeometry(new Rect(0d, 0d, clipWidth.Value, source.ActualHeight));
                var position = source.TranslatePoint(new Point(), NavigationTransitionOverlay);
                Canvas.SetLeft(image, position.X);
                Canvas.SetTop(image, position.Y);
                NavigationTransitionOverlay.Children.Add(image);
                _outgoingSnapshots.Add(image);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                // A visual that cannot be rendered still leaves the incoming page available for navigation.
            }
        }

        private void ClearOutgoingSnapshots()
        {
            foreach (var image in _outgoingSnapshots)
                NavigationTransitionOverlay.Children.Remove(image);
            _outgoingSnapshots.Clear();
        }

        private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
        {
            if (args.Page is not Page page)
            {
                ClearOutgoingSnapshots();
                return;
            }
            var pageType = page.GetType();
            var previousIndex = GetTabIndex(_currentPageType);
            var nextIndex = GetTabIndex(pageType);
            var direction = previousIndex >= 0 && nextIndex >= 0 ? Math.Sign(nextIndex - previousIndex) : 0;
            var motion = MotionPolicy.ResolveCurrent();
            var duration = ResolveNavigationDuration(motion, Math.Abs(nextIndex - previousIndex));
            _currentPageType = pageType;
            SyncNavigation(direction != 0 && motion.Mode == MotionMode.Full, duration);
            if (direction == 0 || motion.Mode == MotionMode.Instant)
            {
                ClearOutgoingSnapshots();
                return;
            }

            // The destination page has already been constructed here; start its clock before deferred Loaded work can delay it.
            var content = (page.FindName("PageScroll") as ScrollViewer)?.Content as UIElement;
            if (content is null)
            {
                ClearOutgoingSnapshots();
                return;
            }
            if (motion.Mode == MotionMode.Full)
            {
                var spline = (KeySpline)FindResource("AfSplineEaseInOut");
                var travel = Math.Max(320d, NavigationTransitionOverlay.ActualWidth);
                SettingsNavigationAnimator.Slide(content, direction * travel, duration, spline);
                if (page.Content is Grid root)
                    root.Children.OfType<SettingsGroupStrip>().FirstOrDefault()?.SlideTabs(direction * travel, duration, spline);

                foreach (var snapshot in _outgoingSnapshots.ToArray())
                    SettingsNavigationAnimator.SlideOut(snapshot, -direction * travel, duration, spline, () =>
                    {
                        NavigationTransitionOverlay.Children.Remove(snapshot);
                        _outgoingSnapshots.Remove(snapshot);
                    });
            }
            else
            {
                ClearOutgoingSnapshots();
                SettingsNavigationAnimator.Fade(content, duration, (KeySpline)FindResource("AfSplineEaseOut"));
            }
        }

        private static TimeSpan ResolveNavigationDuration(MotionProfile motion, int tabDistance)
        {
            if (motion.Mode != MotionMode.Full) return motion.PositionDuration;
            // A remote tab needs time to cross the strip; the cap keeps the page itself responsive.
            var extra = Math.Min(100d, Math.Max(0, tabDistance - 1) * 20d);
            return motion.PositionDuration + TimeSpan.FromMilliseconds(160d + extra);
        }

        private void OnPrimaryNavigationClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton { Tag: Type pageType }) Navigate(pageType);
        }

        private int GetTabIndex(Type? pageType)
        {
            if (pageType is null) return -1;
            var index = 0;
            foreach (var tab in PrimaryTabs.Children.OfType<ToggleButton>())
            {
                if (Equals(tab.Tag, pageType)) return index;
                index++;
            }
            return -1;
        }

        private void SyncNavigation() => SyncNavigation(animateSelection: false, TimeSpan.Zero);

        private void SyncNavigation(bool animateSelection, TimeSpan duration)
        {
            var pageType = _currentPageType ?? FindDescendant<Page>(RootNavigation)?.GetType();
            ToggleButton? selectedTab = null;
            foreach (var tab in PrimaryTabs.Children.OfType<ToggleButton>())
            {
                tab.IsChecked = Equals(tab.Tag, pageType);
                if (tab.IsChecked == true) selectedTab = tab;
            }

            selectedTab?.BringIntoView();
            PrimaryTabsHost.UpdateLayout();
            MovePrimaryTabIndicator(selectedTab, animateSelection, duration);
        }

        private void MovePrimaryTabIndicator(ToggleButton? selectedTab, bool animate, TimeSpan duration)
        {
            if (selectedTab is null || selectedTab.ActualWidth <= 4)
            {
                PrimaryTabIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            var left = selectedTab.TranslatePoint(new Point(2, 0), PrimaryTabsHost).X;
            var width = selectedTab.ActualWidth - 4;
            if (ReferenceEquals(_indicatorTargetTab, selectedTab) &&
                Math.Abs(_indicatorTargetLeft - left) < 0.5 && Math.Abs(_indicatorTargetWidth - width) < 0.5)
                return;

            // Capture the visible surface before stopping its clocks so a rapid second click retargets smoothly.
            var previousLeft = Canvas.GetLeft(PrimaryTabIndicator) + PrimaryTabIndicatorOffset.X;
            var previousWidth = PrimaryTabIndicator.Width * PrimaryTabIndicatorScale.ScaleX;
            PrimaryTabIndicatorOffset.BeginAnimation(TranslateTransform.XProperty, null);
            PrimaryTabIndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);

            Canvas.SetLeft(PrimaryTabIndicator, left);
            PrimaryTabIndicator.Width = width;
            PrimaryTabIndicator.Visibility = Visibility.Visible;
            _indicatorTargetTab = selectedTab;
            _indicatorTargetLeft = left;
            _indicatorTargetWidth = width;

            if (!animate || duration <= TimeSpan.Zero || double.IsNaN(previousLeft) || previousWidth <= 0)
            {
                PrimaryTabIndicatorOffset.X = 0;
                PrimaryTabIndicatorScale.ScaleX = 1;
                return;
            }

            var spline = (KeySpline)FindResource("AfSplineEaseInOut");
            PrimaryTabIndicatorOffset.X = 0;
            PrimaryTabIndicatorScale.ScaleX = 1;
            PrimaryTabIndicatorOffset.BeginAnimation(TranslateTransform.XProperty,
                SettingsNavigationAnimator.CreateSpline(previousLeft - left, 0, duration, spline));
            PrimaryTabIndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                SettingsNavigationAnimator.CreateSpline(previousWidth / width, 1, duration, spline));
        }

        private void OnShellSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (SearchHost is null) return;
            SearchHost.Width = ActualWidth < 760 ? 220 : 260;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(SyncNavigation));
        }

        private void FocusSearch()
        {
            AutoSuggestBox.Focus();
            AutoSuggestBox.FocusCommand.Execute(null);
        }

        /// <summary>设置导航页面提供器。/ Sets the navigation page provider.</summary>
        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
            RootNavigation.SetPageProviderService(navigationViewPageProvider);

        /// <summary>显示设置窗口。/ Shows the settings window.</summary>
        public void ShowWindow() => Show();

        /// <summary>关闭设置窗口。/ Closes the settings window.</summary>
        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        INavigationView INavigationWindow.GetNavigation()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 为导航页提供应用组合根；该兼容入口只在设置窗口初始化期间调用。
        /// Supplies the application composition root to navigation pages; this compatibility entry point is used only during settings-window initialization.
        /// </summary>
        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }

        private void SettingsWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            _currentPageType = typeof(DisplayModesPage);
            RootNavigation.Navigate(_currentPageType);
            SyncNavigation();
        }
    }
}
