// 小音符的独立图层只动画透明度和变换；中断从当前值接续，不改封面、命中或布局。
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private bool _noteRolling;
    private int _noteAnimationVersion;
    internal event Action<bool>? QuickLaunchHoverChanged;
    internal bool CanShowQuickLaunchIcon => CanUseQuickLaunch && SongImageBorder.IsMouseOver &&
        _quickLaunchSettings.InteractionMode == QuickLaunchInteractionMode.SelectThenLaunch && _quickLaunchSettings.ShowApplicationIconOnHover;

    internal void SuspendQuickLaunchPresentation()
    {
        CancelQuickLaunchInteraction();
        InvalidateQuickLaunchRequests();
        _quickLaunchSession?.Suspend();
        ResetQuickLaunchVisuals();
    }

    internal void ShowQuickLaunchIcon(ImageSource? icon, int wheelDelta)
    {
        var animate = CurrentMotion.Mode == MotionMode.Full;
        var duration = animate ? CurrentMotion.StandardDuration : TimeSpan.Zero;
        var version = ++_noteAnimationVersion;
        if (icon is null)
        {
            StopNoteRollAtCurrentPosition();
            AnimateNoteValue(SongImagePlaceholder, OpacityProperty, 1, duration);
            AnimateNoteValue(QuickLaunchIconViewport, OpacityProperty, 0, duration, () =>
            { if (version == _noteAnimationVersion) ClearNoteImages(); });
            return;
        }
        var height = Math.Max(1, QuickLaunchIconViewport.ActualHeight);
        var direction = wheelDelta >= 0 ? 1 : -1;
        if (QuickLaunchIconIncoming.Source is null)
        {
            // 首次悬停只淡入容器，避免容器与图像同时渐显造成中途变暗。
            AnimateNoteValue(QuickLaunchIconIncoming, OpacityProperty, 1, TimeSpan.Zero);
            AnimateNoteValue(NoteIncomingTranslate, TranslateTransform.YProperty, 0, TimeSpan.Zero);
        }
        var rolling = animate && wheelDelta != 0 && QuickLaunchIconIncoming.Source is not null && QuickLaunchIconViewport.Opacity > 0;
        if (rolling && !_noteRolling)
        {
            QuickLaunchIconOutgoing.Source = QuickLaunchIconIncoming.Source;
            NoteOutgoingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            QuickLaunchIconOutgoing.BeginAnimation(OpacityProperty, null);
            NoteOutgoingTranslate.Y = NoteIncomingTranslate.Y;
            QuickLaunchIconOutgoing.Opacity = QuickLaunchIconIncoming.Opacity;
            NoteIncomingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            NoteIncomingTranslate.Y = -direction * height;
            QuickLaunchIconIncoming.BeginAnimation(OpacityProperty, null);
            QuickLaunchIconIncoming.Opacity = 0;
        }
        // 连续输入只替换入场图层的最新目标，保持它当前的位置和透明度；不新增图层或排队。
        QuickLaunchIconIncoming.Source = icon;
        _noteRolling = rolling || _noteRolling;
        AnimateNoteValue(SongImagePlaceholder, OpacityProperty, 0, duration);
        AnimateNoteValue(QuickLaunchIconViewport, OpacityProperty, 1, duration);
        AnimateNoteValue(NoteIncomingTranslate, TranslateTransform.YProperty, 0, duration);
        AnimateNoteValue(QuickLaunchIconIncoming, OpacityProperty, 1, duration);
        AnimateNoteValue(NoteOutgoingTranslate, TranslateTransform.YProperty, direction * height, duration);
        AnimateNoteValue(QuickLaunchIconOutgoing, OpacityProperty, 0, duration, () =>
        {
            if (version != _noteAnimationVersion) return;
            _noteRolling = false;
            QuickLaunchIconOutgoing.Source = null;
        });
    }

    internal void ResetQuickLaunchVisuals()
    {
        _noteAnimationVersion++;
        StopNoteRollAtCurrentPosition();
        AnimateNoteValue(SongImagePlaceholder, OpacityProperty, 1, TimeSpan.Zero);
        AnimateNoteValue(QuickLaunchIconViewport, OpacityProperty, 0, TimeSpan.Zero);
        SetNotePressed(false, immediate: true);
        ClearNoteImages();
    }

    private void ClearNoteImages()
    {
        QuickLaunchIconIncoming.Source = null;
        QuickLaunchIconOutgoing.Source = null;
        NoteIncomingTranslate.Y = 0;
        NoteOutgoingTranslate.Y = 0;
        _noteRolling = false;
    }

    private void StopNoteRollAtCurrentPosition()
    {
        foreach (var transform in new[] { NoteIncomingTranslate, NoteOutgoingTranslate })
        {
            var current = transform.Y;
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = current;
        }
        foreach (var image in new[] { QuickLaunchIconIncoming, QuickLaunchIconOutgoing })
        {
            var current = image.Opacity;
            image.BeginAnimation(OpacityProperty, null);
            image.Opacity = current;
        }
        // 冻结不是完成：淡出未结束就重新进入时，继续复用两层当前状态。
        _noteRolling = QuickLaunchIconOutgoing.Source is not null;
    }

    private static void AnimateNoteValue(DependencyObject target, DependencyProperty property, double value, TimeSpan duration, Action? completed = null)
    {
        void Begin(AnimationTimeline? animation)
        {
            if (target is UIElement element) element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
            else ((Animatable)target).BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }
        var current = (double)target.GetValue(property);
        Begin(null);
        target.SetValue(property, value);
        if (duration == TimeSpan.Zero) { completed?.Invoke(); return; }
        var animation = new DoubleAnimation(current, value, duration) { EasingFunction = CreateEaseOut(), FillBehavior = FillBehavior.Stop };
        if (completed is not null) animation.Completed += (_, _) => completed();
        Begin(animation);
    }
}
