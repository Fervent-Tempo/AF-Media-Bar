// 页面拥有轮播时钟和窗口订阅；离页停止，数据读取由 ViewModel 和服务负责。
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;

/// <summary>原生首页布局与轮播呈现，不启动媒体或设备采样。</summary>
public partial class QuickStartPage : INavigableView<QuickStartViewModel>
{
    private readonly DispatcherTimer _rotation = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
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
        if (_window is not null) { _window.Activated += OnWindowStateChanged; _window.Deactivated += OnWindowStateChanged; }
        ViewModel.PropertyChanged += OnModelChanged;
        _scroll = PageLayout.ScrollViewer;
        if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;
        ApplyColumns(PageRoot.ActualWidth);
        await ViewModel.ActivateAsync();
        if (IsLoaded) UpdateRotation();
    }
    private void OnPageUnloaded(object sender, RoutedEventArgs args)
    {
        Detach(); ViewModel.Deactivate();
        FeatureBody.BeginAnimation(OpacityProperty, null);
    }
    private void Detach()
    {
        _rotation.Stop();
        ViewModel.PropertyChanged -= OnModelChanged;
        if (_window is not null) { _window.Activated -= OnWindowStateChanged; _window.Deactivated -= OnWindowStateChanged; _window = null; }
        if (_scroll is not null) { _scroll.ScrollChanged -= OnScrollChanged; _scroll = null; }
    }
    private void OnContentSizeChanged(object sender, SizeChangedEventArgs args) => ApplyColumns(args.NewSize.Width);
    private void ApplyColumns(double width)
    {
        var wide = width >= 1000;
        PlayerColumn.Width = new GridLength(wide ? Math.Clamp(width * .32, 320, 420) : 0);
        HeroGap.Width = new GridLength(wide ? 24 : 0);
        Grid.SetColumnSpan(Hero, wide ? 1 : 3);
        Grid.SetColumn(PlayerHost, wide ? 2 : 0);
        Grid.SetColumnSpan(PlayerHost, wide ? 1 : 3);
        Grid.SetRow(PlayerHost, wide ? 0 : 1);
        PlayerHost.Margin = wide ? new Thickness() : new Thickness(0, 20, 0, 0);
        QuickLinks.Columns = width >= 900 ? 3 : width >= 560 ? 2 : 1;
        StatisticsGrid.Columns = width >= 700 ? 4 : 2;
        var communityWide = width >= 800;
        CommunityGap.Width = new GridLength(communityWide ? 24 : 0);
        Grid.SetColumnSpan((UIElement)CommunityGrid.Children[0], communityWide ? 1 : 3);
        Grid.SetColumn(PullRequestsHost, communityWide ? 2 : 0);
        Grid.SetColumnSpan(PullRequestsHost, communityWide ? 1 : 3);
        Grid.SetRow(PullRequestsHost, communityWide ? 0 : 1);
        PullRequestsHost.Margin = communityWide ? new Thickness() : new Thickness(0, 16, 0, 0);
        UpdateRotation();
    }
    private void OnWindowStateChanged(object? sender, EventArgs args) => UpdateRotation();
    private void OnScrollChanged(object sender, ScrollChangedEventArgs args) => UpdateRotation();
    private void OnHeroPointerChanged(object sender, MouseEventArgs args) => UpdateRotation();
    private void OnHeroFocus(object sender, KeyboardFocusChangedEventArgs args) { ViewModel.IsRotationPaused = true; UpdateRotation(); }
    private void OnRotationClicked(object sender, RoutedEventArgs args) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(UpdateRotation));
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
            if (!_keyboardAction && motion.UseDecorativeEffects)
                FeatureBody.BeginAnimation(OpacityProperty, new DoubleAnimation(.65, 1, motion.StandardDuration) { FillBehavior = FillBehavior.Stop });
            else FeatureBody.BeginAnimation(OpacityProperty, null);
            _rotation.Stop();
        }
        UpdateRotation();
    }
    private void UpdateRotation()
    {
        if (!IsLoaded || _window?.IsActive != true || ViewModel.IsRotationPaused || Hero.IsMouseOver ||
            !ViewModel.HasMultipleFeatures || !MotionPolicy.ResolveCurrent().UseContinuousMotion || _scroll is null)
        { _rotation.Stop(); return; }
        var bounds = Hero.TransformToAncestor(_scroll).TransformBounds(new Rect(Hero.RenderSize));
        if (!bounds.IntersectsWith(new Rect(0, 0, _scroll.ActualWidth, _scroll.ActualHeight))) { _rotation.Stop(); return; }
        if (!_rotation.IsEnabled) _rotation.Start();
    }
    private void OnRotationTick(object? sender, EventArgs args) { UpdateRotation(); if (_rotation.IsEnabled) ViewModel.AdvanceAutomatically(); }
    private void OnTableMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (PageLayout.ScrollViewer is not { } scroll) return;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - args.Delta); args.Handled = true;
    }
}
