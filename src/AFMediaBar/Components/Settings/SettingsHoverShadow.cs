// Animates only a card's empty background border. Each template instance owns and releases its own effect and subscriptions.
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Components;

/// <summary>Provides subtle mouse-hover elevation without moving cards or filtering their text and controls.</summary>
public static class SettingsHoverShadow
{
    /// <summary>Enables hover elevation on a background border whose content is rendered by a sibling.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SettingsHoverShadow), new PropertyMetadata(false, OnEnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(ShadowState), typeof(SettingsHoverShadow), new PropertyMetadata(null));

    /// <summary>Reads whether the background border participates in hover elevation.</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    /// <summary>Enables or disables elevation for a background border.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement surface) return;
        if (surface.GetValue(StateProperty) is ShadowState previous) previous.Dispose();
        surface.ClearValue(StateProperty);
        if ((bool)args.NewValue)
            surface.SetValue(StateProperty, new ShadowState(surface));
    }

    private sealed class ShadowState : IDisposable
    {
        private readonly FrameworkElement _surface;
        private FrameworkElement? _owner;
        private DropShadowEffect? _shadow;
        private Effect? _originalEffect;
        private bool _loaded;
        private bool _mouseHover;
        private int _originalZIndex;

        public ShadowState(FrameworkElement surface)
        {
            _surface = surface;
            surface.Loaded += OnLoaded;
            surface.Unloaded += OnUnloaded;
            if (surface.IsLoaded) Attach();
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => Attach();
        private void OnUnloaded(object sender, RoutedEventArgs args) => Detach();

        private void Attach()
        {
            if (_loaded) return;
            _loaded = true;
            _owner = _surface.TemplatedParent as FrameworkElement ?? _surface;
            _originalZIndex = System.Windows.Controls.Panel.GetZIndex(_owner);
            _owner.MouseEnter += OnMouseEnter;
            _owner.MouseLeave += OnMouseLeave;
            _owner.IsEnabledChanged += OnOwnerStateChanged;
            SystemParameters.StaticPropertyChanged += OnEnvironmentChanged;
            RenderCapability.TierChanged += OnTierChanged;
            Update(animate: false);
        }

        private void OnMouseEnter(object sender, MouseEventArgs args)
        {
            _mouseHover = args.StylusDevice is null;
            Update(animate: true);
        }

        private void OnMouseLeave(object sender, MouseEventArgs args)
        {
            _mouseHover = false;
            Update(animate: true);
        }

        private void OnOwnerStateChanged(object sender, DependencyPropertyChangedEventArgs args) => Update(animate: false);
        private void OnTierChanged(object? sender, EventArgs args) => RefreshEnvironment();
        private void OnEnvironmentChanged(object? sender, PropertyChangedEventArgs args) => RefreshEnvironment();
        private void RefreshEnvironment()
        {
            if (_surface.Dispatcher.CheckAccess()) Update(animate: false);
            else _surface.Dispatcher.InvokeAsync(() => { if (_loaded) Update(animate: false); });
        }

        private void Update(bool animate)
        {
            if (!_loaded) return;
            var motion = MotionPolicy.ResolveCurrent();
            if (!motion.UseDecorativeEffects || _owner?.IsEnabled != true || _owner is SettingsRow { IsNested: true })
            {
                RemoveEffect();
                RestoreLayerOrder();
                return;
            }
            // The next row otherwise paints over the lower half of the hovered card's shadow.
            _owner.SetCurrentValue(System.Windows.Controls.Panel.ZIndexProperty, _mouseHover ? _originalZIndex + 1 : _originalZIndex);
            if (_shadow is null)
            {
                _originalEffect = _surface.Effect;
                _shadow = new DropShadowEffect { BlurRadius = 14d, ShadowDepth = 1d, Opacity = 0d, RenderingBias = RenderingBias.Performance };
                _surface.Effect = _shadow;
            }
            Animate(DropShadowEffect.ShadowDepthProperty, _mouseHover ? 4d : 1d);
            Animate(DropShadowEffect.OpacityProperty, _mouseHover ? 0.30d : 0d);

            void Animate(DependencyProperty property, double target)
            {
                var from = (double)_shadow.GetValue(property);
                _shadow.BeginAnimation(property, null);
                _shadow.SetValue(property, target);
                if (animate && motion.FastDuration > TimeSpan.Zero)
                    _shadow.BeginAnimation(property, new DoubleAnimation(from, target, motion.FastDuration)
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                        FillBehavior = FillBehavior.Stop
                    }, HandoffBehavior.SnapshotAndReplace);
            }
        }

        private void RemoveEffect()
        {
            if (_shadow is null) return;
            _shadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, null);
            _shadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            if (ReferenceEquals(_surface.Effect, _shadow)) _surface.Effect = _originalEffect;
            _shadow = null;
            _originalEffect = null;
        }

        private void Detach()
        {
            if (!_loaded) return;
            _loaded = false;
            if (_owner is not null)
            {
                _owner.MouseEnter -= OnMouseEnter;
                _owner.MouseLeave -= OnMouseLeave;
                _owner.IsEnabledChanged -= OnOwnerStateChanged;
            }
            SystemParameters.StaticPropertyChanged -= OnEnvironmentChanged;
            RenderCapability.TierChanged -= OnTierChanged;
            RemoveEffect();
            RestoreLayerOrder();
            _mouseHover = false;
            _owner = null;
        }

        private void RestoreLayerOrder() => _owner?.SetCurrentValue(System.Windows.Controls.Panel.ZIndexProperty, _originalZIndex);

        public void Dispose()
        {
            Detach();
            _surface.Loaded -= OnLoaded;
            _surface.Unloaded -= OnUnloaded;
        }
    }
}
