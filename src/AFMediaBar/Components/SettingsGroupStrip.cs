using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;
using AFMediaBar.Resources;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components;

/// <summary>
/// 设置页固定页头：页面标识、标题、副标题、分组标签条和滚动进度线。
///
/// 分组标签不是一份独立维护的清单，而是按声明顺序从 <see cref="Target"/> 滚动内容里的
/// <see cref="SettingsGroup"/> 读取标题生成的。数量对不上时标签条只报告而不跳转，
/// 而不是高亮到一个错误的分组。
/// A pinned settings page header: page identity, title, subtitle, the group tab strip, and a scroll progress
/// line.
///
/// The tabs are not a separately maintained list; they are built by reading the headers of the
/// <see cref="SettingsGroup"/> containers inside the <see cref="Target"/> scroll content, in declaration
/// order. When the counts disagree the strip only reports and does not jump, rather than highlighting the
/// wrong group.
///
/// 跳转是可中断的：滚轮或按住滚动条拖动会立刻停止正在播放的跳转动画，因为用户的手比动画更有权威。
/// Jumps are interruptible: a wheel event or a drag on the scroll bar stops the running jump immediately,
/// because the user's hand outranks the animation.
/// </summary>
public class SettingsGroupStrip : Control
{
    /// <summary>页面标题。/ Page title.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SettingsGroupStrip),
        new PropertyMetadata(string.Empty));

    /// <summary>页面副标题，用一句话说明这一页能改什么。/ Page subtitle: one line on what this page can change.</summary>
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(SettingsGroupStrip),
        new PropertyMetadata(string.Empty));

    /// <summary>页面图标。/ Page icon.</summary>
    public static readonly DependencyProperty PageIconProperty = DependencyProperty.Register(
        nameof(PageIcon),
        typeof(IconElement),
        typeof(SettingsGroupStrip),
        new PropertyMetadata(null));

    /// <summary>可选状态芯片文本，例如当前承载模式；为空时不显示。/ Optional status chip text, such as the active hosting mode; empty hides it.</summary>
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(SettingsGroupStrip),
        new PropertyMetadata(string.Empty));

    /// <summary>状态芯片语气。/ Tone of the status chip.</summary>
    public static readonly DependencyProperty StatusToneProperty = DependencyProperty.Register(
        nameof(StatusTone),
        typeof(SettingsChipTone),
        typeof(SettingsGroupStrip),
        new PropertyMetadata(SettingsChipTone.Accent));

    /// <summary>承载页面内容的滚动容器，仅支持垂直滚动。/ The scroll container holding the page content; vertical scrolling only.</summary>
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target),
        typeof(ScrollViewer),
        typeof(SettingsGroupStrip),
        new PropertyMetadata(null, OnTargetChanged));

    /// <summary>跳转动画的驱动值；它本身不是滚动位置，只是把动画值转发给 <see cref="ScrollViewer.ScrollToVerticalOffset"/>。/ Drives the jump animation; it is not the scroll position itself, only a value forwarded to <see cref="ScrollViewer.ScrollToVerticalOffset"/>.</summary>
    private static readonly DependencyProperty JumpOffsetProperty = DependencyProperty.Register(
        nameof(JumpOffset),
        typeof(double),
        typeof(SettingsGroupStrip),
        new PropertyMetadata(0d, OnJumpOffsetChanged));

    /// <summary>分组顶端到视口顶端保留的间距，避免分组标题紧贴窗口上沿。/ Inset kept between a group top and the viewport top so the header does not touch the window edge.</summary>
    private const double JumpTopInsetDip = 8d;

    /// <summary>跳转时长。滚动是移动，因此用 ease-in-out 而不是 ease-out。/ Jump duration. Scrolling is on-screen movement, so it eases in and out rather than out.</summary>
    private static readonly Duration JumpDuration = new(TimeSpan.FromMilliseconds(260));

    /// <summary>滚动多少 DIP 之后分隔线完全淡入。取 24 是因为它约等于标题行高，用户看到内容明显移动即可确认“上面还有内容”。/ How far the content scrolls, in DIP, before the divider is fully faded in; 24 is about one title line, enough for the user to see that content really moved.</summary>
    private const double DividerFadeDistanceDip = 24d;

    /// <summary>分隔线淡入淡出的时长。它是状态提示，取 UI 动效的下限量级。/ The divider's fade duration; it signals state, so it sits at the low end of the UI motion range.</summary>
    private static readonly Duration DividerFadeDuration = new(TimeSpan.FromMilliseconds(120));

    private static readonly DependencyPropertyKey GroupsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Groups),
        typeof(ObservableCollection<SettingsGroupItem>),
        typeof(SettingsGroupStrip),
        new PropertyMetadata(null));

    /// <summary>只读的分组标签集合；它由页面内容解析而来，不接受外部赋值。/ Read-only group tabs resolved from the page content; not assignable from outside.</summary>
    public static readonly DependencyProperty GroupsProperty = GroupsPropertyKey.DependencyProperty;

    private readonly ObservableCollection<SettingsGroupItem> _groups = [];
    private ItemsControl? _tabs;
    private FrameworkElement? _indicator;
    private FrameworkElement? _progress;
    private FrameworkElement? _divider;
    private TranslateTransform? _indicatorTranslate;
    private ScaleTransform? _progressScale;
    private bool _isJumping;
    private int _visibleGroupIndex = -1;
    private double _dividerOpacity = -1d;

    /// <summary>创建分组标签条并初始化跳转命令。/ Creates the group strip and its jump command.</summary>
    public SettingsGroupStrip()
    {
        JumpCommand = new JumpToGroupCommand(this);
        SetValue(GroupsPropertyKey, _groups);

        // 标签文本是分组标题的快照（SettingsGroupItem.Name 是只读字符串），因此切换界面语言后必须重建一次：
        // 页面的分组标题走动态资源会自动换成新语言，而已经建好的标签不会。控件拿不到依赖注入，所以这里订阅的是
        // 文案表自己的语言变化事件（它的唯一发布方是 LocalizationService），并且只在该条子在场时订阅。
        // The tab text is a snapshot of the group headers — SettingsGroupItem.Name is a read-only string — so a language switch
        // has to rebuild it once: the pages' headers follow the dynamic resources into the new language while the tabs already
        // built do not. A control cannot receive dependency injection, so it subscribes to the text table's own language-change
        // event, whose only publisher is LocalizationService, and only while the strip is in the tree.
        Loaded += OnStripLoaded;
        Unloaded += OnStripUnloaded;
    }

    private void OnStripLoaded(object sender, RoutedEventArgs e)
    {
        // 先减后加：同一个控件重新进入可视树时不会累积第二次订阅。
        // Subtract before adding, so re-entering the visual tree cannot accumulate a second subscription.
        Translations.LanguageChanged -= OnLanguageChanged;
        Translations.LanguageChanged += OnLanguageChanged;
    }

    private void OnStripUnloaded(object sender, RoutedEventArgs e) => Translations.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>分组标签集合。/ The group tab collection.</summary>
    public ObservableCollection<SettingsGroupItem> Groups => _groups;

    /// <summary>点击任意标签时执行的跳转命令，参数为 <see cref="SettingsGroupItem"/>。/ Jump command for any tab; the parameter is a <see cref="SettingsGroupItem"/>.</summary>
    public ICommand JumpCommand { get; }

    /// <summary>页面标题。/ Page title.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>页面副标题。/ Page subtitle.</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>页面图标。/ Page icon.</summary>
    public IconElement? PageIcon
    {
        get => (IconElement?)GetValue(PageIconProperty);
        set => SetValue(PageIconProperty, value);
    }

    /// <summary>可选状态芯片文本。/ Optional status chip text.</summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    /// <summary>状态芯片语气。/ Tone of the status chip.</summary>
    public SettingsChipTone StatusTone
    {
        get => (SettingsChipTone)GetValue(StatusToneProperty);
        set => SetValue(StatusToneProperty, value);
    }

    /// <summary>承载页面内容的滚动容器。/ The scroll container holding the page content.</summary>
    public ScrollViewer? Target
    {
        get => (ScrollViewer?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    private double JumpOffset
    {
        get => (double)GetValue(JumpOffsetProperty);
        set => SetValue(JumpOffsetProperty, value);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_tabs is not null) _tabs.SizeChanged -= OnTabsSizeChanged;
        _tabs = GetTemplateChild("PART_Tabs") as ItemsControl;
        if (_tabs is not null) _tabs.SizeChanged += OnTabsSizeChanged;
        _indicator = GetTemplateChild("PART_Indicator") as FrameworkElement;
        _progress = GetTemplateChild("PART_Progress") as FrameworkElement;
        _divider = GetTemplateChild("PART_Divider") as FrameworkElement;
        _dividerOpacity = -1d;

        // 模板里的 Freezable 会被 BAML 冻结，直接改它的属性会抛“对象处于只读状态”。
        // 因此这里换成代码创建的可写变换，而不是去改模板自带的那个。
        // Freezables declared in a template arrive frozen from BAML, and writing to one throws
        // "object is read-only". Replace them with writable instances created here instead of mutating the
        // template's own transform.
        if (_indicator is not null)
        {
            _indicatorTranslate = new TranslateTransform();
            _indicator.RenderTransform = _indicatorTranslate;
        }

        if (_progress is not null)
        {
            _progressScale = new ScaleTransform(0d, 1d);
            _progress.RenderTransformOrigin = new Point(0d, 0.5d);
            _progress.RenderTransform = _progressScale;
        }

        UpdateIndicator();
    }

    /// <summary>
    /// 把指定序号的滚动到视口顶端。搜索命中后由设置窗口调用，用来把用户直接带到命中的分组。
    /// 序号越界或标签数量与分组数量不一致时不做任何事，而不是跳到错误的位置。
    /// Brings the group at the given index to the top of the viewport. The settings window calls this after a
    /// search hit so the user lands on the matching group. An out-of-range index, or a mismatch between the tab
    /// and group counts, does nothing rather than jumping to the wrong place.
    /// </summary>
    /// <param name="groupIndex">目标分组序号，与页面滚动内容里的声明顺序一致。/ Target group index, matching declaration order in the page's scroll content.</param>
    public void JumpToGroup(int groupIndex) => JumpTo(groupIndex);

    /// <summary>
    /// 滚动到目标分组并让它脉冲一次，用于搜索命中后的定位。
    /// 脉冲只写 Opacity，且结束时先清除动画再落回终值，避免元素停在中间透明度上。
    /// Scrolls to a group and pulses it once, for landing on a search hit.
    /// The pulse writes only Opacity and clears the animation before landing the final value, so the group can
    /// never be left sitting at an intermediate opacity.
    /// </summary>
    /// <param name="groupIndex">目标分组序号。/ Target group index.</param>
    public void RevealGroup(int groupIndex)
    {
        JumpTo(groupIndex);

        var groups = ResolveGroups();
        if (groupIndex < 0 || groupIndex >= groups.Count)
        {
            return;
        }

        var group = groups[groupIndex];
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.Zero,
            Duration = new Duration(TimeSpan.FromMilliseconds(440)),
            FillBehavior = FillBehavior.HoldEnd
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.55d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(
            1d,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(440)),
            ResolveEaseOut()));
        animation.Completed += (_, _) =>
        {
            group.BeginAnimation(UIElement.OpacityProperty, null);
            group.Opacity = 1d;
        };

        group.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>取与设置页外观一致的强 ease-out 曲线；资源缺失时用同一曲线的兜底实例。/ The strong ease-out curve shared with settings appearance, with a fallback of the same shape.</summary>
    private static KeySpline ResolveEaseOut() =>
        Application.Current?.TryFindResource("AfSplineEaseOut") as KeySpline ?? new KeySpline(0.23d, 1d, 0.32d, 1d);

    /// <summary>
    /// 重新解析分组并重建标签。页面在内容变化后可以调用它；正常生命周期里
    /// <see cref="FrameworkElement.Loaded"/> 会自动调用一次。
    /// Re-resolves the groups and rebuilds the tabs. Pages may call this after their content changes; the
    /// normal lifetime calls it once from <see cref="FrameworkElement.Loaded"/>.
    /// </summary>
    public void Rebuild()
    {
        // Generated tab containers are measured after the current layout pass.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsLoaded) UpdateIndicator();
        }));
        var groups = ResolveGroups();
        if (groups.Count == _groups.Count &&
            groups.Select(group => group.Header?.ToString() ?? string.Empty).SequenceEqual(_groups.Select(item => item.Name)))
        {
            UpdateState(forceRebuild: false);
            return;
        }

        _visibleGroupIndex = -1;
        _groups.Clear();
        for (var index = 0; index < groups.Count; index++)
        {
            _groups.Add(new SettingsGroupItem(index, groups[index].Header?.ToString() ?? string.Empty, JumpCommand));
        }

        UpdateState(forceRebuild: true);
    }

    /// <summary>
    /// 解析滚动内容里**当前可见**的分组容器，按声明顺序返回。
    ///
    /// 显示模式页把每个模式的设置放在各自的容器里并互斥显示，因此这里必须跳过隐藏的容器：
    /// 否则切到灵动岛模式后，标签条仍然会列出任务栏模式的分组，点一下就跳到看不见的内容。
    /// 直接子级本身就是分组时也算，因此没有分区的页面不受影响。
    /// Resolves the currently visible group containers in the scroll content, in declaration order.
    ///
    /// The display-mode page keeps each mode's settings in its own mutually exclusive container, so hidden
    /// containers must be skipped: otherwise switching to the island would leave the tabs listing taskbar groups
    /// and jumping to content that is not on screen. A direct child that is itself a group still counts, so pages
    /// without mode sections are unaffected.
    /// </summary>
    private IReadOnlyList<SettingsGroup> ResolveGroups()
    {
        if (Target?.Content is not Panel content)
        {
            return [];
        }

        var groups = new List<SettingsGroup>();
        foreach (var child in content.Children)
        {
            switch (child)
            {
                case SettingsGroup group:
                    groups.Add(group);
                    break;
                case Panel section when section.Visibility == Visibility.Visible:
                    groups.AddRange(section.Children.OfType<SettingsGroup>());
                    break;
            }
        }

        return groups;
    }

    private static void OnTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not SettingsGroupStrip strip)
        {
            return;
        }

        if (args.OldValue is ScrollViewer previous)
        {
            previous.ScrollChanged -= strip.OnScrollChanged;
            previous.PreviewMouseWheel -= strip.OnUserScrollInput;
            previous.PreviewMouseLeftButtonDown -= strip.OnUserScrollInput;
        }

        if (args.NewValue is ScrollViewer current)
        {
            current.ScrollChanged += strip.OnScrollChanged;
            current.PreviewMouseWheel += strip.OnUserScrollInput;
            current.PreviewMouseLeftButtonDown += strip.OnUserScrollInput;
            current.Loaded += strip.OnTargetLoaded;
        }
    }

    private void OnTargetLoaded(object sender, RoutedEventArgs e) => Rebuild();

    private static void OnJumpOffsetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is SettingsGroupStrip strip && strip.Target is { } target)
        {
            target.ScrollToVerticalOffset((double)args.NewValue);
        }
    }

    private void OnUserScrollInput(object sender, InputEventArgs e) => StopJump();

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 用户自己滚动（滚轮、拖动、键盘）时放弃正在进行的手动跳转，避免动画与手互相拉扯。
        // Abandon a running jump as soon as the user scrolls by hand, so the animation and the hand never
        // fight over the same offset.
        if (!_isJumping && Math.Abs(e.VerticalChange) > 0.5d)
        {
            StopJump();
        }

        UpdateState(forceRebuild: false);
    }

    private void StopJump()
    {
        if (!_isJumping)
        {
            return;
        }

        _isJumping = false;
        BeginAnimation(JumpOffsetProperty, null);

        // 清除动画之后必须把本地值对齐到真实位置，否则下一次跳转仍从旧的动画值开始。
        // After clearing the animation the local value must follow the real offset, or the next jump starts
        // from a stale animated value.
        JumpOffset = Target?.VerticalOffset ?? 0d;
    }

    /// <summary>
    /// 解析当前激活分组、指示条位置和进度，并写回标签状态。标签数量与分组数量不一致时只更新进度，
    /// 不做任何跳转或高亮，因为那说明序号已经不再对应。
    /// Resolves the active group, the indicator position, and the progress, then writes the tab state back.
    /// When the tab and group counts disagree it only updates the progress and skips jumping and highlighting,
    /// because the indices no longer correspond to anything.
    /// </summary>
    private void UpdateState(bool forceRebuild)
    {
        var target = Target;
        var groups = ResolveGroups();
        if (target is null || groups.Count == 0)
        {
            return;
        }

        var tops = new List<double>(groups.Count);
        foreach (var group in groups)
        {
            tops.Add(group.TranslatePoint(new Point(0d, 0d), target.Content as UIElement ?? target).Y);
        }

        var progress = SettingsGroupScrollPolicy.ResolveProgress(
            target.VerticalOffset,
            target.ViewportHeight,
            target.ExtentHeight);
        ApplyProgress(progress);
        ApplyDivider(target.VerticalOffset);

        if (_groups.Count != groups.Count)
        {
            if (forceRebuild)
            {
                Rebuild();
            }

            return;
        }

        var atBottom = SettingsGroupScrollPolicy.IsAtBottom(
            target.VerticalOffset,
            target.ViewportHeight,
            target.ExtentHeight);
        var active = SettingsGroupScrollPolicy.ResolveActiveIndex(tops, target.VerticalOffset, atBottom);
        if (active < 0 || active >= _groups.Count)
        {
            return;
        }

        for (var index = 0; index < _groups.Count; index++)
        {
            _groups[index].IsActive = index == active;
        }

        UpdateIndicator();
    }

    /// <summary>把滚动进度写进进度线的缩放。只写 ScaleX，不触发布局。/ Writes scroll progress into the line's scale; only ScaleX, so no layout runs.</summary>
    private void ApplyProgress(double progress)
    {
        if (_progressScale is not null)
        {
            _progressScale.ScaleX = progress;
        }
    }

    /// <summary>
    /// 按滚动量淡入淡出页头下方的分隔线。静止在顶部时完全不可见，因此页头在半透明材质上不会留下
    /// 任何永久性的实色块；只有确实有内容滚上去时才出现一条发丝线。
    /// 只在目标透明度变化超过千分之一时才启动动画，避免每个滚动事件都重启动画。
    /// Fades the hairline under the header according to how far the content has scrolled. It is invisible at the
    /// top, so the header leaves no permanent solid block over translucent material, and a hairline appears only
    /// once content really has scrolled away above. The animation restarts only when the target opacity moves by
    /// more than a thousandth, so a stream of scroll events does not restart it continuously.
    /// </summary>
    private void ApplyDivider(double verticalOffset)
    {
        if (_divider is null)
        {
            return;
        }

        var offset = double.IsFinite(verticalOffset) ? Math.Max(0d, verticalOffset) : 0d;
        var target = Math.Clamp(offset / DividerFadeDistanceDip, 0d, 1d);
        if (Math.Abs(target - _dividerOpacity) < 0.001d)
        {
            return;
        }

        _dividerOpacity = target;
        _divider.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(target, DividerFadeDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// 把指示条移到激活标签下方。宽度按目标标签实测宽度直接赋值而不做动画：
    /// 宽度是布局属性，给它加动画会每一帧都触发布局，而位移用变换就能保持不触发布局。
    /// Moves the indicator under the active tab. Its width is assigned from the measured tab width rather than
    /// animated: width is a layout property, so animating it would run layout every frame, while the movement
    /// itself stays a transform and costs no layout.
    /// </summary>
    private void OnTabsSizeChanged(object sender, SizeChangedEventArgs e) => UpdateIndicator();

    private void UpdateIndicator()
    {
        if (_indicator is null || _indicatorTranslate is null || _tabs is null || Target is null)
        {
            return;
        }

        var active = _groups.FirstOrDefault(item => item.IsActive);
        if (active is null)
        {
            _indicator.Visibility = Visibility.Collapsed;
            return;
        }

        if (_tabs.ItemContainerGenerator.ContainerFromIndex(active.Index) is not FrameworkElement container ||
            container.ActualWidth <= 0d)
        {
            // 容器还没生成或还没测量，指示条先隐藏，等下一次滚动或布局回调再定位。
            // The container is not generated or measured yet; hide the indicator and place it on the next
            // scroll or layout pass.
            _indicator.Visibility = Visibility.Collapsed;
            return;
        }

        if (_visibleGroupIndex != active.Index)
        {
            _visibleGroupIndex = active.Index;
            container.BringIntoView();
        }
        _indicator.Visibility = Visibility.Visible;
        _indicator.Width = container.ActualWidth;

        if (_indicator.Parent is UIElement parent)
        {
            _indicatorTranslate.X = container.TranslatePoint(new Point(0d, 0d), parent).X;
        }
    }

    /// <summary>把某个分组带到视口顶端，用一个可被滚轮或拖动打断的动画完成。/ Brings a group to the viewport top with an animation a wheel event or drag can interrupt.</summary>
    private void JumpTo(int groupIndex)
    {
        var target = Target;
        var groups = ResolveGroups();
        if (target is null || groupIndex < 0 || groupIndex >= groups.Count || _groups.Count != groups.Count)
        {
            return;
        }

        var anchor = target.Content as UIElement ?? target;
        var groupTop = groups[groupIndex].TranslatePoint(new Point(0d, 0d), anchor).Y;
        var destination = SettingsGroupScrollPolicy.ResolveJumpOffset(
            groupTop,
            JumpTopInsetDip,
            target.ViewportHeight,
            target.ExtentHeight);

        if (Math.Abs(destination - target.VerticalOffset) < 0.5d)
        {
            return;
        }

        // Keyboard navigation and reduced motion complete scrolling immediately.
        if (InputManager.Current.MostRecentInputDevice is KeyboardDevice || MotionPolicy.ResolveCurrent().Mode != MotionMode.Full)
        {
            StopJump();
            target.ScrollToVerticalOffset(destination);
            return;
        }
        _isJumping = true;
        BeginAnimation(
            JumpOffsetProperty,
            new DoubleAnimation(target.VerticalOffset, destination, JumpDuration)
            {
                // 滚动是屏上移动，用 ease-in-out；曲线取强版本，避免结尾拖沓。
                // Scrolling is on-screen movement, so ease in and out; the curve is a strong variant so the
                // tail does not drag.
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>标签条使用的跳转命令；它只是一个薄适配器，让标签可以只声明式地绑定。/ The jump command used by the tabs: a thin adapter so tabs can bind declaratively.</summary>
    private sealed class JumpToGroupCommand : ICommand
    {
        private readonly SettingsGroupStrip _owner;

        public JumpToGroupCommand(SettingsGroupStrip owner) => _owner = owner;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is SettingsGroupItem;

        public void Execute(object? parameter)
        {
            if (parameter is SettingsGroupItem item)
            {
                _owner.JumpTo(item.Index);
            }
        }
    }
}
