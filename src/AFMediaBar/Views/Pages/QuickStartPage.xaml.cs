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
    private readonly DispatcherTimer _rotation = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
    private readonly TranslateTransform _heroTranslate = new();
    private Window? _window;
    private ScrollViewer? _scroll;
    private bool _keyboardAction;
    private int _featureIndex;
    public QuickStartViewModel ViewModel { get; }
    public QuickStartPage(QuickStartViewModel viewModel)
    {
        ViewModel = viewModel; DataContext = this; InitializeComponent();
        HeroCopy.RenderTransform = _heroTranslate;
        _rotation.Tick += OnRotationTick;
    }
    private async void OnPageLoaded(object sender, RoutedEventArgs args)
    {
        Detach();
        _window = Window.GetWindow(this);
        if (_window is not null) { _window.Activated += OnWindowStateChanged; _window.Deactivated += OnWindowStateChanged; }
        ViewModel.PropertyChanged += OnModelChanged;
        _scroll = PageLayout.ScrollViewer;
        if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;
        _featureIndex = ViewModel.Features.IndexOf(ViewModel.SelectedFeature!);
        ApplyColumns(PageRoot.ActualWidth);
        await ViewModel.ActivateAsync();
        if (IsLoaded) UpdateRotation();
    }
    private void OnPageUnloaded(object sender, RoutedEventArgs args)
    {
        Detach(); ViewModel.Deactivate();
        HeroCopy.BeginAnimation(OpacityProperty, null);
        _heroTranslate.BeginAnimation(TranslateTransform.XProperty, null);
    }
    private void Detach()
    {
        _rotation.Stop();
        ViewModel.PropertyChanged -= OnModelChanged;
        if (_window is not null) { _window.Activated -= OnWindowStateChanged; _window.Deactivated -= OnWindowStateChanged; _window = null; }
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
        Grid.SetRow(PlayerActions, narrow ? 1 : 0);
        Grid.SetColumn(PlayerActions, narrow ? 0 : 1);
        Grid.SetColumnSpan(PlayerActions, narrow ? 2 : 1);
        PlayerActions.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        PlayerActions.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        PlayerActionDivider.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        QuickLinks.Columns = width >= 900d ? 3 : width >= 560d ? 2 : 1;
        TutorialsGrid.Columns = width >= 900d ? 2 : 1;
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
    private void OnWindowStateChanged(object? sender, EventArgs args) => UpdateRotation();
    private void OnScrollChanged(object sender, ScrollChangedEventArgs args) => UpdateRotation();
    private void OnHeroPointerChanged(object sender, MouseEventArgs args) => UpdateRotation();
    private void OnHeroFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        // 鼠标点击落在 Hero 内时焦点变化与悬停同时发生；这里不额外暂停，避免覆盖“暂停轮播”按钮的切换结果。
        if (HeroGrid.IsMouseOver) return;
        ViewModel.IsRotationPaused = true;
        UpdateRotation();
    }
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
            var index = ViewModel.Features.IndexOf(ViewModel.SelectedFeature!);
            var direction = index >= 0 && _featureIndex >= 0 && index < _featureIndex ? -1d : 1d;
            _featureIndex = index;
            if (!_keyboardAction && motion.UseDecorativeEffects)
            {
                // 淡入 + 12px 方向位移；动画结束回到本地值 0，不留下残留位移。
                _heroTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                _heroTranslate.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(12d * direction, 0d, motion.StandardDuration)
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                HeroCopy.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(.65d, 1d, motion.StandardDuration) { FillBehavior = FillBehavior.Stop });
            }
            else
            {
                _heroTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                HeroCopy.BeginAnimation(OpacityProperty, null);
            }
            _rotation.Stop();
        }
        UpdateRotation();
    }
    private void UpdateRotation()
    {
        if (!IsLoaded || _window?.IsActive != true || ViewModel.IsRotationPaused || HeroGrid.IsMouseOver ||
            !ViewModel.HasMultipleFeatures || !MotionPolicy.ResolveCurrent().UseContinuousMotion || _scroll is null)
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
