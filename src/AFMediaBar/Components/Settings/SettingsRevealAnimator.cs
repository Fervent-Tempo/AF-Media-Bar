// Owns settings-content entrance clocks and readiness callbacks; completion, unload, or navigation cancellation releases them.
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Utils;

/// <summary>Applies an entrance to loaded settings content while leaving fixed page headers and navigation in place.</summary>
public static class SettingsRevealAnimator
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(RevealState), typeof(SettingsRevealAnimator), new PropertyMetadata(null));

    /// <summary>Reveals content once, waiting for the actual Loaded event instead of consuming the entrance during frame navigation.</summary>
    public static void Play(Panel? host)
    {
        if (host is null) return;
        GetState(host).Play(replay: false);
    }

    /// <summary>Replays on navigation or mode changes, continuing an interrupted entrance from its current values.</summary>
    public static void Replay(Panel? host)
    {
        if (host is null) return;
        GetState(host).Play(replay: true);
    }

    /// <summary>Releases pending readiness callbacks and animation clocks when navigation replaces the content or closes.</summary>
    public static void Cancel(Panel? host)
    {
        if (host?.GetValue(StateProperty) is RevealState state) state.Settle();
    }

    private static RevealState GetState(Panel host)
    {
        if (host.GetValue(StateProperty) is RevealState existing) return existing;
        var state = new RevealState(host);
        host.SetValue(StateProperty, state);
        return state;
    }

    private sealed class RevealState(Panel host)
    {
        private bool _revealed;
        private bool _running;
        private bool _waitingLoaded;
        private int _generation;
        private TranslateTransform? _offset;
        private Transform? _originalTransform;
        private TransformGroup? _ownedTransform;
        private DispatcherOperation? _start;

        public void Play(bool replay)
        {
            if (_revealed && !replay) return;
            var opacity = _running ? host.Opacity : 0d;
            var position = _running ? _offset?.Y : null;
            Settle();
            _revealed = true;
            if (!host.IsLoaded)
            {
                // Cached pages can be requested before Frame has attached them. Leave them readable while waiting.
                _waitingLoaded = true;
                host.Loaded += OnLoaded;
                host.Unloaded += OnUnloaded;
                return;
            }
            Begin(opacity, position);
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            if (!_waitingLoaded) return;
            host.Loaded -= OnLoaded;
            host.Unloaded -= OnUnloaded;
            _waitingLoaded = false;
            Begin(0d, null);
        }

        private void Begin(double fromOpacity, double? fromPosition)
        {
            var reveal = SettingsRevealPolicy.Resolve(0, MotionPolicy.ResolveCurrent());
            if (!reveal.ShouldAnimate) return;
            var generation = _generation;
            _running = true;
            host.Opacity = fromOpacity;
            if (reveal.OffsetY != 0d)
            {
                _originalTransform = host.RenderTransform;
                _offset = new TranslateTransform(0d, fromPosition ?? reveal.OffsetY);
                _ownedTransform = new TransformGroup();
                if (_originalTransform is not null) _ownedTransform.Children.Add(_originalTransform);
                _ownedTransform.Children.Add(_offset);
                host.RenderTransform = _ownedTransform;
            }
            host.Unloaded += OnUnloaded;
            SystemParameters.StaticPropertyChanged += OnEnvironmentChanged;
            RenderCapability.TierChanged += OnRenderingTierChanged;
            // Finish layout before creating clocks, and keep the prepared start values even if the presenter touches opacity.
            host.UpdateLayout();
            _start = host.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _start = null;
                if (generation != _generation) return;
                if (!host.IsLoaded || !host.IsVisible)
                {
                    Settle();
                    return;
                }
                var spline = host.TryFindResource("AfSplineFluentEntrance") as KeySpline ?? new KeySpline(0.23d, 1d, 0.32d, 1d);
                host.Opacity = 1d;
                var fade = CreateAnimation(fromOpacity, 1d, reveal.Duration, spline);
                fade.Completed += (_, _) => { if (generation == _generation) Settle(); };
                host.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
                if (_offset is not null)
                {
                    _offset.Y = 0d;
                    _offset.BeginAnimation(TranslateTransform.YProperty,
                        CreateAnimation(fromPosition ?? reveal.OffsetY, 0d, reveal.Duration, spline), HandoffBehavior.SnapshotAndReplace);
                }
            }));
        }

        private void OnUnloaded(object sender, RoutedEventArgs args) { if (!host.IsLoaded) Settle(); }
        private void OnRenderingTierChanged(object? sender, EventArgs args) => SettleFromEnvironment();
        private void OnEnvironmentChanged(object? sender, PropertyChangedEventArgs args) => SettleFromEnvironment();
        private void SettleFromEnvironment()
        {
            if (host.Dispatcher.CheckAccess()) Settle();
            else
            {
                var generation = _generation;
                host.Dispatcher.InvokeAsync(() => { if (generation == _generation) Settle(); });
            }
        }

        public void Settle()
        {
            _generation++;
            _running = false;
            _waitingLoaded = false;
            _start?.Abort();
            _start = null;
            host.Loaded -= OnLoaded;
            host.Unloaded -= OnUnloaded;
            SystemParameters.StaticPropertyChanged -= OnEnvironmentChanged;
            RenderCapability.TierChanged -= OnRenderingTierChanged;
            host.BeginAnimation(UIElement.OpacityProperty, null);
            host.Opacity = 1d;
            _offset?.BeginAnimation(TranslateTransform.YProperty, null);
            if (ReferenceEquals(host.RenderTransform, _ownedTransform)) host.RenderTransform = _originalTransform;
            _offset = null;
            _ownedTransform = null;
            _originalTransform = null;
        }
    }

    private static DoubleAnimationUsingKeyFrames CreateAnimation(double from, double to, TimeSpan duration, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.Zero, Duration = duration, FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
        return animation;
    }
}
