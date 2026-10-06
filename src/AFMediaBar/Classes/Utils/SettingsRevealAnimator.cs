// Owns settings-content entrance clocks and first-frame callbacks; unload or completion releases them.
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Utils;

/// <summary>Applies one Fluent entrance to settings content without moving navigation or changing layout.</summary>
public static class SettingsRevealAnimator
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(RevealState), typeof(SettingsRevealAnimator), new PropertyMetadata(null));

    /// <summary>Reveals a loaded content panel once; page navigation may explicitly replay it.</summary>
    public static void Play(Panel? host)
    {
        if (host is null) return;
        if (host.GetValue(StateProperty) is not RevealState state)
        {
            state = new RevealState(host);
            host.SetValue(StateProperty, state);
        }
        state.Play(replay: false);
    }

    /// <summary>Replays on navigation or mode changes; an interrupted reveal continues from its current values.</summary>
    public static void Replay(Panel? host)
    {
        if (host is null) return;
        if (host.GetValue(StateProperty) is RevealState state)
            state.Play(replay: true);
        else
            Play(host);
    }

    private sealed class RevealState(Panel host)
    {
        private bool _revealed;
        private bool _running;
        private TranslateTransform? _offset;
        private Transform? _originalTransform;
        private TransformGroup? _ownedTransform;
        private EventHandler? _rendering;
        private DispatcherOperation? _start;

        public void Play(bool replay)
        {
            if (_revealed && !replay) return;
            var opacity = _running ? host.Opacity : 0d;
            var position = _running ? _offset?.Y : null;
            Settle();
            _revealed = true;
            var reveal = SettingsRevealPolicy.Resolve(0, MotionPolicy.ResolveCurrent());
            if (!reveal.ShouldAnimate || InputManager.Current.MostRecentInputDevice is KeyboardDevice) return;

            _running = true;
            host.Opacity = opacity;
            if (reveal.OffsetY != 0d)
            {
                _originalTransform = host.RenderTransform;
                _offset = new TranslateTransform(0d, position ?? reveal.OffsetY);
                _ownedTransform = new TransformGroup();
                if (_originalTransform is not null)
                    _ownedTransform.Children.Add(_originalTransform);
                _ownedTransform.Children.Add(_offset);
                host.RenderTransform = _ownedTransform;
            }
            host.Unloaded += OnUnloaded;
            SystemParameters.StaticPropertyChanged += OnEnvironmentChanged;
            RenderCapability.TierChanged += OnRenderingTierChanged;
            // Start after the first rendered layout so constructing a complex page cannot consume its entrance.
            _start = host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _start = null;
                if (!_running || !host.IsLoaded || Window.GetWindow(host)?.IsVisible != true)
                {
                    Settle();
                    return;
                }
                _rendering = (_, _) =>
                {
                    CompositionTarget.Rendering -= _rendering;
                    _rendering = null;
                    _start = host.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        _start = null;
                        if (!_running || !host.IsLoaded || Window.GetWindow(host)?.IsVisible != true)
                        {
                            Settle();
                            return;
                        }
                        var spline = host.TryFindResource("AfSplineFluentEntrance") as KeySpline ?? new KeySpline(0d, 0d, 0d, 1d);
                        var fromOpacity = host.Opacity;
                        var fromPosition = _offset?.Y ?? 0d;
                        host.Opacity = 1d;
                        var fade = CreateAnimation(fromOpacity, 1d, reveal.Duration, spline);
                        fade.Completed += (_, _) => Settle();
                        host.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
                        if (_offset is not null)
                        {
                            _offset.Y = 0d;
                            _offset.BeginAnimation(TranslateTransform.YProperty,
                                CreateAnimation(fromPosition, 0d, reveal.Duration, spline), HandoffBehavior.SnapshotAndReplace);
                        }
                    }));
                };
                CompositionTarget.Rendering += _rendering;
            }));
        }

        private void OnUnloaded(object sender, RoutedEventArgs args) => Settle();
        private void OnRenderingTierChanged(object? sender, EventArgs args) => Settle();
        private void OnEnvironmentChanged(object? sender, PropertyChangedEventArgs args) => Settle();

        private void Settle()
        {
            _running = false;
            if (_rendering is not null) CompositionTarget.Rendering -= _rendering;
            _rendering = null;
            _start?.Abort();
            _start = null;
            host.Unloaded -= OnUnloaded;
            SystemParameters.StaticPropertyChanged -= OnEnvironmentChanged;
            RenderCapability.TierChanged -= OnRenderingTierChanged;
            host.BeginAnimation(UIElement.OpacityProperty, null);
            host.Opacity = 1d;
            _offset?.BeginAnimation(TranslateTransform.YProperty, null);
            if (ReferenceEquals(host.RenderTransform, _ownedTransform))
                host.RenderTransform = _originalTransform;
            _offset = null;
            _ownedTransform = null;
            _originalTransform = null;
        }
    }

    private static DoubleAnimationUsingKeyFrames CreateAnimation(double from, double to, TimeSpan duration, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.Zero,
            Duration = duration,
            FillBehavior = FillBehavior.Stop
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
        return animation;
    }
}
