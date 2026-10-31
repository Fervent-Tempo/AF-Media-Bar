// 页面拥有轮播时钟和窗口订阅；离页停止，数据读取由 ViewModel 和服务负责。
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;

/// <summary>原生首页布局与轮播呈现，不启动媒体或设备采样。</summary>
public partial class QuickStartPage : INavigableView<QuickStartViewModel>
{
    private readonly DispatcherTimer _rotation = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
    private Window? _window;
    private ScrollViewer? _scroll;
    private bool _keyboardAction;
    public QuickStartViewModel ViewModel { get; }
    public QuickStartPage(QuickStartViewModel viewModel)
    {
        ViewModel = viewModel; DataContext = this; InitializeComponent();
        _rotation.Tick += OnRotationTick;
    }
    private async void OnPageLoaded(object sender, RoutedEventArgs args)
    {
        Detach();
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.Activated += OnWindowStateChanged;
            _window.Deactivated += OnWindowStateChanged;
            _window.StateChanged += OnWindowStateChanged;
        }
        SystemParameters.StaticPropertyChanged += OnMotionEnvironmentChanged;
        RenderCapability.TierChanged += OnWindowStateChanged;
        ViewModel.PropertyChanged += OnModelChanged;
        _scroll = PageLayout.ScrollViewer;
        if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;
        ApplyColumns(PageRoot.ActualWidth);
        UpdateFeatureIndicator(animate: false);
        if (!await SettingsPageActivation.WaitForPresentationAsync(this)) return;
        await ViewModel.ActivateAsync();
        if (IsLoaded) UpdateRotation();
    }
    private void OnPageUnloaded(object sender, RoutedEventArgs args)
    {
        Detach(); ViewModel.Deactivate();
        HeroText.BeginAnimation(OpacityProperty, null);
        FeatureIndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
    }
    private void Detach()
    {
        _rotation.Stop();
        ViewModel.PropertyChanged -= OnModelChanged;
        SystemParameters.StaticPropertyChanged -= OnMotionEnvironmentChanged;
        RenderCapability.TierChanged -= OnWindowStateChanged;
        if (_window is not null)
        {
            _window.Activated -= OnWindowStateChanged;
            _window.Deactivated -= OnWindowStateChanged;
            _window.StateChanged -= OnWindowStateChanged;
            _window = null;
        }
        if (_scroll is not null) { _scroll.ScrollChanged -= OnScrollChanged; _scroll = null; }
    }
    private void OnContentSizeChanged(object sender, SizeChangedEventArgs args) => ApplyColumns(args.NewSize.Width);
    private void OnHeroVisualSizeChanged(object sender, SizeChangedEventArgs args) => SizeHeroScreen();
    /// <summary>外层中性画框内保持 16:9 的深色屏幕；可用宽度不足时按宽度收缩，保证完整显示。</summary>
    private void SizeHeroScreen()
    {
        var frame = HeroVisual;
        var availableWidth = frame.ActualWidth - frame.Padding.Left - frame.Padding.Right - frame.BorderThickness.Left - frame.BorderThickness.Right;
        var availableHeight = frame.ActualHeight - frame.Padding.Top - frame.Padding.Bottom - frame.BorderThickness.Top - frame.BorderThickness.Bottom;
        if (availableWidth <= 0d || availableHeight <= 0d) return;
        const double aspect = 16d / 9d;
        if (availableWidth / availableHeight > aspect)
        {
            HeroScreen.Width = availableHeight * aspect;
            HeroScreen.Height = availableHeight;
        }
        else
        {
            HeroScreen.Width = availableWidth;
            HeroScreen.Height = availableWidth / aspect;
        }
    }
    /// <summary>按页面内容宽度切换 Hero、状态条动作、磁贴/教程/帮助/项目动态的列数与行位置。</summary>
    private void ApplyColumns(double width)
    {
        var wide = width >= 1000d;
        var narrow = width < 700d;
        if (narrow)
        {
            Grid.SetColumn(HeroCopy, 0); Grid.SetColumnSpan(HeroCopy, 3); Grid.SetRow(HeroCopy, 0);
            Grid.SetColumn(HeroVisual, 0); Grid.SetColumnSpan(HeroVisual, 3); Grid.SetRow(HeroVisual, 1);
            HeroVisual.Margin = new Thickness(0, 20, 0, 0);
            HeroVisual.Height = 220d;
            HeroTitle.FontSize = 22d; HeroTitle.LineHeight = 28d; HeroTitle.MaxHeight = 84d;
        }
        else
        {
            Grid.SetColumn(HeroCopy, 0); Grid.SetColumnSpan(HeroCopy, 1); Grid.SetRow(HeroCopy, 0);
            Grid.SetColumn(HeroVisual, 2); Grid.SetColumnSpan(HeroVisual, 1); Grid.SetRow(HeroVisual, 0);
            HeroVisual.Margin = new Thickness(0);
            // 默认（1320 窗，内容宽约 1024）350，宽窗（1560 窗，内容宽约 1264）380，让图片显示更大。
            HeroVisual.Height = width >= 1100d ? 380d : 350d;
            HeroTitle.FontSize = wide ? 26d : 24d;
            HeroTitle.LineHeight = wide ? 34d : 30d;
            HeroTitle.MaxHeight = wide ? 102d : 90d;
        }
        // 文案行数变化只影响正文区域，位置胶囊始终贴齐固定区域底部。
        HeroCopy.Height = narrow ? 340d : HeroVisual.Height;
        Grid.SetRow(PlayerActions, narrow ? 1 : 0);
        Grid.SetColumn(PlayerActions, narrow ? 0 : 1);
        Grid.SetColumnSpan(PlayerActions, narrow ? 2 : 1);
        PlayerActions.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        PlayerActions.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        PlayerActionDivider.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        QuickLinks.Columns = width >= 900d ? 3 : width >= 560d ? 2 : 1;
        var tutorialColumns = width >= 900d ? 2 : 1;
        TutorialsGrid.Columns = tutorialColumns;
        for (var index = 0; index < TutorialsGrid.Children.Count; index++)
        {
            var card = (FrameworkElement)TutorialsGrid.Children[index];
            card.Margin = new Thickness(0, 0, tutorialColumns == 2 && index % 2 == 0 ? 12d : 0d, 8d);
        }
        var helpWide = width >= 900d;
        HelpGap.Width = new GridLength(helpWide ? 28d : 0d);
        Grid.SetColumn(RuntimeHost, helpWide ? 2 : 0);
        Grid.SetColumnSpan(RuntimeHost, helpWide ? 1 : 3);
        Grid.SetRow(RuntimeHost, helpWide ? 0 : 1);
        RuntimeHost.Margin = helpWide ? new Thickness(0) : new Thickness(0, 20, 0, 0);
        var communityWide = width >= 800d;
        CommunityGap.Width = new GridLength(communityWide ? 28d : 0d);
        Grid.SetColumnSpan(IssueHost, communityWide ? 1 : 3);
        Grid.SetColumn(PullRequestsHost, communityWide ? 2 : 0);
        Grid.SetColumnSpan(PullRequestsHost, communityWide ? 1 : 3);
        Grid.SetRow(PullRequestsHost, communityWide ? 0 : 1);
        PullRequestsHost.Margin = communityWide ? new Thickness(0) : new Thickness(0, 20, 0, 0);
        UpdateRotation();
    }
    private void OnWindowStateChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.CheckAccess()) UpdateRotation();
        else Dispatcher.InvokeAsync(UpdateRotation);
    }
    private void OnMotionEnvironmentChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnWindowStateChanged(sender, args);
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded) return;
            HeroText.BeginAnimation(OpacityProperty, null);
            UpdateFeatureIndicator(animate: false);
        });
    }
    private void OnScrollChanged(object sender, ScrollChangedEventArgs args) => UpdateRotation();
    private void OnHeroPointerChanged(object sender, MouseEventArgs args) => UpdateRotation();
    private void OnRotationVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdateRotation();
    private void OnHeroKeyDown(object sender, KeyEventArgs args)
    {
        _keyboardAction = true;
        if (args.Key == Key.Left) { ViewModel.PreviousFeatureCommand.Execute(null); args.Handled = true; }
        else if (args.Key == Key.Right) { ViewModel.NextFeatureCommand.Execute(null); args.Handled = true; }
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _keyboardAction = false));
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModel.SelectedFeature))
        {
            var motion = MotionPolicy.ResolveCurrent();
            if (!_keyboardAction && motion.UseTransitions)
            {
                // 原位淡入保留文字的完整边界；圆点不参与正文动画。
                var opacity = HeroText.HasAnimatedProperties ? HeroText.Opacity : .65d;
                HeroText.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(opacity, 1d, motion.StandardDuration)
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
            }
            else
            {
                HeroText.BeginAnimation(OpacityProperty, null);
            }
            UpdateFeatureIndicator(animate: !_keyboardAction);
            _rotation.Stop();
        }
        UpdateRotation();
    }
    private void OnFeatureDotsSizeChanged(object sender, SizeChangedEventArgs args) => UpdateFeatureIndicator(animate: false);
    private void UpdateFeatureIndicator(bool animate)
    {
        if (!IsLoaded) return;
        var index = ViewModel.Features.IndexOf(ViewModel.SelectedFeature!);
        var count = ViewModel.FeatureTabs.Count;
        FeatureIndicator.Visibility = index >= 0 && count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (index < 0 || count == 0 || FeatureDots.ActualWidth <= 0d) return;

        // 固定按钮槽位中居中胶囊；集合刷新不重建这条过渡，连续切换从当前帧接续。
        var step = FeatureDots.ActualWidth / count;
        var target = index * step + (step - 4d - FeatureIndicator.Width) / 2d;
        var current = FeatureIndicatorTranslate.X;
        FeatureIndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        FeatureIndicatorTranslate.X = target;
        var motion = MotionPolicy.ResolveCurrent();
        if (animate && motion.UseDecorativeEffects)
            FeatureIndicatorTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(current, target, motion.StandardDuration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop });
    }
    private void UpdateRotation()
    {
        if (!IsLoaded || !IsVisible || _window?.IsActive != true || _window.WindowState == WindowState.Minimized ||
            HeroGrid.IsMouseOver || HeroGrid.IsKeyboardFocusWithin || !ViewModel.HasMultipleFeatures ||
            !MotionPolicy.ResolveCurrent().UseContinuousMotion || _scroll is null || !_scroll.IsAncestorOf(HeroGrid))
        { _rotation.Stop(); return; }
        var bounds = HeroGrid.TransformToAncestor(_scroll).TransformBounds(new Rect(HeroGrid.RenderSize));
        if (!bounds.IntersectsWith(new Rect(0, 0, _scroll.ActualWidth, _scroll.ActualHeight))) { _rotation.Stop(); return; }
        if (!_rotation.IsEnabled) _rotation.Start();
    }
    private void OnRotationTick(object? sender, EventArgs args) { UpdateRotation(); if (_rotation.IsEnabled) ViewModel.AdvanceAutomatically(); }
    private void OnReadMoreClick(object sender, RoutedEventArgs args) => ExternalLinkLauncher.TryOpen(ViewModel.SelectedFeature?.Url);
    private async void OnPlayerSupportClick(object sender, RoutedEventArgs args)
    {
        await PlayerSupportDialog.ShowAsync(ViewModel);
        if (IsLoaded) SupportTrigger.Focus();
    }
    private void OnThreadClick(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: HomeThreadCard thread }) ExternalLinkLauncher.TryOpen(thread.Url);
    }
}
