// 媒体栏拥有宠物输入租约和呈现状态；共享输入服务由应用容器释放。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Resources;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Classes.Models;
using System.Diagnostics;
using Wpf.Ui.Controls;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private readonly PetSpeechBubble _petBubble = new();
    public bool IsPetBubbleOpen => _petBubble.IsOpen || _petMenu?.IsOpen == true;
    private ContextMenu? _petMenu;
    private long _petMenuOpenedAtTicks, _petMenuDismissedPress;
    public event EventHandler? PetSettingsRequested;
    private bool DismissPetMenuPointer()
    {
        if (_petMenu is not null)
        {
            _petMenuDismissedPress = _petInput?.LastButtonPressTicks ?? 0;
            _petMenu.IsOpen = false;
            return true;
        }
        // Hook 在 WPF 消息前记录按压身份，关闭菜单的同一次按压不能再次打开或说话。
        return _petMenuDismissedPress != 0 && _petMenuDismissedPress == _petInput?.LastButtonPressTicks;
    }

    internal static IEnumerable<(string Key, bool Overflow, bool Free)> PetPlacementChoices(PetComponentSettings settings, bool floating)
    {
        if (!settings.AllowOverflow || settings.FreePlacement) yield return (floating ? "DockBar" : "Dock", true, false);
        if (settings.AllowOverflow) yield return (floating ? "InlineBar" : "Inline", false, false);
        if (!settings.AllowOverflow || !settings.FreePlacement) yield return ("Free", true, true);
    }

    private async Task OpenPetMenuAsync(FrameworkElement target, bool keyboard = false)
    {
        if (!keyboard && DismissPetMenuPointer()) return;
        if (_petMenu?.IsOpen == true) return;
        var pet = ActivePet;
        pet.ResetPetting();
        pet.InputSuspended = true;
        var menu = new ContextMenu { StaysOpen = false, Width = 256, Padding = new Thickness(4) };
        menu.SetResourceReference(StyleProperty, typeof(ContextMenu));
        menu.SetResourceReference(FontFamilyProperty, "AppTextFontFamily");
        menu.SetResourceReference(FontWeightProperty, "AppTextFontWeight");
        menu.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        menu.SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        menu.BorderThickness = new Thickness(1);
        ContextMenuHelper.AttachOutsideClickDismissal(menu);
        _petMenu = menu;
        void Add(string key, SymbolRegular icon, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = Translations.Get("Widgets.Pet.Menu." + key), Icon = new SymbolIcon(icon), IsEnabled = enabled };
            item.Click += (_, _) => { menu.IsOpen = false; action(); }; menu.Items.Add(item);
        }
        var snapshot = _petCompanion?.Snapshot ?? MediaSnapshot.Disconnected;
        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var buttons = new List<System.Windows.Controls.Button>();
        void Transport(string key, SymbolRegular icon, bool enabled, Action action)
        {
            var label = Translations.Get("Widgets.Pet.Menu." + key);
            var button = new Wpf.Ui.Controls.Button { Content = new SymbolIcon(icon), Width = 60, Height = 40, Margin = new Thickness(2), IsEnabled = enabled, ToolTip = label, Appearance = ControlAppearance.Transparent };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, args) => { args.Handled = true; menu.IsOpen = false; action(); };
            buttons.Add(button); transport.Children.Add(button);
        }
        Transport("Previous", SymbolRegular.Previous24, snapshot.CanSkipPrevious, () => SkipPreviousRequested?.Invoke(this, EventArgs.Empty));
        Transport(snapshot.IsPlaying ? "Pause" : "Play", snapshot.IsPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24, snapshot.CanPlayPause, () => TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty));
        Transport("Next", SymbolRegular.Next24, snapshot.CanSkipNext, () => SkipNextRequested?.Invoke(this, EventArgs.Empty));
        var transportItem = new MenuItem { Header = transport, Focusable = false, StaysOpenOnClick = true };
        menu.Items.Add(transportItem);
        menu.Items.Add(new Separator());
        var mute = new MenuItem { Header = Translations.Get("Widgets.Pet.Menu.Mute"), Icon = new SymbolIcon(SymbolRegular.SpeakerMute24), IsEnabled = false };
        mute.Click += async (_, _) => { menu.IsOpen = false; try { if (_petCompanion is not null) await _petCompanion.ToggleMuteAsync(); } catch (Exception error) { AppLogService.Current?.Warn("Pet", error.Message); } };
        menu.Items.Add(mute);
        menu.Items.Add(new Separator());
        foreach (var choice in PetPlacementChoices(PresentationSettings.PetComponent, Window.GetWindow(this) is AFMediaBar.Views.Windows.FloatingBarWindow))
            Add(choice.Key, choice.Free ? SymbolRegular.ArrowMove24 : SymbolRegular.ArrowDown24, () =>
            {
                var s = SettingsManager.Current.PetComponent;
                SettingsManager.Current.PetComponent = s with { AllowOverflow = choice.Overflow, FreePlacement = choice.Free };
            });
        menu.Items.Add(new Separator());
        var follow = SettingsManager.Current.PetComponent.FollowArtworkColor;
        var colorItem = new MenuItem();
        void RefreshColorItem()
        {
            colorItem.Header = Translations.Get("Widgets.Pet.Menu." + (follow ? "LockColor" : "FollowColor"));
            colorItem.IsEnabled = !follow || _petCompanion?.CanFixOutfitColor == true;
            var hex = (follow ? _petCompanion?.ArtworkColor : null) ?? SettingsManager.Current.PetComponent.OutfitColorHex ?? ColorHex.Format(SystemParameters.WindowGlassColor);
            if (ColorHex.TryParse(hex, out var color)) colorItem.Icon = new FontIcon { Glyph = "●", FontSize = 14, Foreground = new SolidColorBrush(color) };
        }
        RefreshColorItem();
        if (_petCompanion is not null) _petCompanion.ArtworkColorChanged += RefreshColorItem;
        colorItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            if (follow) _petCompanion?.FixOutfitColor();
            else { var s = SettingsManager.Current.PetComponent; SettingsManager.Current.PetComponent = s with { FollowArtworkColor = true }; }
        };
        menu.Items.Add(colorItem);
        var focus = new MenuItem { Icon = new SymbolIcon(SymbolRegular.Timer24), IsEnabled = _petCompanion is not null };
        var cancelFocus = new MenuItem { Header = Translations.Get("Widgets.Pet.Focus.Cancel") };
        cancelFocus.Click += (_, _) => { menu.IsOpen = false; _petCompanion?.CancelFocus(); };
        foreach (var minutes in new[] { 10, 25, 45 })
        {
            var preset = new MenuItem { Header = Translations.Format("Widgets.Pet.Focus.Minutes", minutes) };
            preset.Click += (_, _) => { menu.IsOpen = false; _petCompanion?.StartFocus(minutes); };
            focus.Items.Add(preset);
        }
        focus.Items.Add(new Separator());
        var customPanel = new StackPanel { Margin = new Thickness(4) };
        customPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = Translations.Get("Widgets.Pet.Focus.Custom"), Margin = new Thickness(0, 0, 0, 6) });
        var customRow = new StackPanel { Orientation = Orientation.Horizontal };
        var minutesInput = new NumberBox { Minimum = 1, Maximum = 120, Value = 25, Width = 120 };
        System.Windows.Automation.AutomationProperties.SetName(minutesInput, Translations.Get("Widgets.Pet.Focus.Custom"));
        var startFocus = new Wpf.Ui.Controls.Button { Content = Translations.Get("Widgets.Pet.Focus.Start"), Margin = new Thickness(6, 0, 0, 0) };
        startFocus.Click += (_, args) =>
        {
            var value = minutesInput.Value;
            if (value is null || !double.IsFinite(value.Value) || value < 1 || value > 120 || value != Math.Truncate(value.Value)) return;
            args.Handled = true; menu.IsOpen = false; _petCompanion?.StartFocus((int)value.Value);
        };
        customRow.Children.Add(minutesInput); customRow.Children.Add(startFocus); customPanel.Children.Add(customRow);
        var customItem = new MenuItem { Header = customPanel, StaysOpenOnClick = true, Focusable = false };
        focus.Items.Add(customItem);
        focus.Items.Add(cancelFocus);
        void RefreshFocus()
        {
            var running = _petCompanion?.IsFocusActive == true;
            var remaining = _petCompanion?.FocusRemaining ?? TimeSpan.Zero;
            focus.Header = running ? Translations.Format("Widgets.Pet.Focus.Remaining", $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}") : Translations.Get("Widgets.Pet.Focus.Title");
            foreach (var item in focus.Items.OfType<FrameworkElement>()) item.Visibility = ReferenceEquals(item, cancelFocus) == running ? Visibility.Visible : Visibility.Collapsed;
        }
        RefreshFocus();
        if (_petCompanion is not null) _petCompanion.FocusChanged += RefreshFocus;
        menu.Items.Add(focus);
        Add("Settings", SymbolRegular.Settings24, () => PetSettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { menu.IsOpen = false; args.Handled = true; return; }
            var enabled = buttons.Where(b => b.IsEnabled).ToArray();
            var current = Array.FindIndex(enabled, b => b.IsKeyboardFocusWithin);
            var firstItem = menu.Items.OfType<MenuItem>().FirstOrDefault(i => i != transportItem && i.IsEnabled);
            if (current >= 0 && args.Key is Key.Left or Key.Right)
            { enabled[(current + (args.Key == Key.Left ? enabled.Length - 1 : 1)) % enabled.Length].Focus(); args.Handled = true; }
            else if (current >= 0 && args.Key is Key.Up or Key.Down)
            { (args.Key == Key.Down ? firstItem : menu.Items.OfType<MenuItem>().LastOrDefault(i => i.IsEnabled))?.Focus(); args.Handled = true; }
            else if (args.Key == Key.Up && firstItem?.IsKeyboardFocusWithin == true && enabled.Length > 0)
            { enabled[^1].Focus(); args.Handled = true; }
        };
        menu.Closed += (_, _) =>
        {
            if (_petCompanion is not null) { _petCompanion.ArtworkColorChanged -= RefreshColorItem; _petCompanion.FocusChanged -= RefreshFocus; }
            if (!ReferenceEquals(_petMenu, menu)) return;
            _petMenuDismissedPress = _petInput?.LastButtonPressTicks ?? 0;
            _petPressPoint = null;
            pet.InputSuspended = false;
            pet.ResetPetting();
            menu.PlacementTarget = null;
            _petMenu = null;
        };
        _petMenuOpenedAtTicks = Stopwatch.GetTimestamp();
        ContextMenuHelper.OpenAtScreenAnchor(menu, Window.GetWindow(this) ?? target,
            keyboard ? target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2)) : null);
        menu.Focus();
        if (keyboard) { if (buttons.FirstOrDefault(b => b.IsEnabled) is { } first) first.Focus(); else menu.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); }
        try
        {
            if (_petCompanion is null) return;
            var volume = await _petCompanion.ReadVolumeAsync();
            if (!_petDisposed && ReferenceEquals(_petMenu, menu) && menu.IsOpen && snapshot.SourceId == _petCompanion.Snapshot.SourceId && volume is not null)
            { mute.IsEnabled = true; mute.Header = Translations.Get("Widgets.Pet.Menu." + (volume.IsMuted ? "Unmute" : "Mute")); }
        }
        catch (Exception error) { AFMediaBar.Classes.Services.AppLogService.Current?.Warn("Pet", error.Message); }
    }
    private Point? _petPressPoint;
    private PetCompanionService? _petCompanion;
    private IDisposable? _petContextLease;
    private IDisposable? _petStatusLease;
    private PetInputService? _petInput;
    private IDisposable? _petLease;
    private PetView? _petLeaseView;
    private bool _petDisposed;
    private PetWebEnvironment? _petEnvironment;
    private PetOverflowHost? _petOverflow;
    private DesktopWidgetZOrderService? _petZOrder;
    private double PetCrossSize => Math.Max(1, (_isVertical ? MainBorder.Width : MainBorder.Height) - 4);
    private double PetInlineSize
    {
        get
        {
            var size = Math.Min(PresentationSettings.PetComponent.SizeDip,
                double.IsFinite(PetCrossSize) ? PetCrossSize : PetComponentSettings.DefaultSizeDip);
            if (TaskbarPetSurface.ActualWidth > 2 && TaskbarPetSurface.ActualHeight > 2)
                size = Math.Min(size, Math.Min(TaskbarPetSurface.ActualWidth, TaskbarPetSurface.ActualHeight) - 2);
            // 向下对齐物理像素，避免分数 DPI 下布局取整侵入内边距。
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return Math.Floor(size * scale) / scale;
        }
    }
    private double PetSurfaceLength => (PresentationSettings.PetComponent.AllowOverflow
        ? PresentationSettings.PetComponent.SizeDip
        : Math.Min(PresentationSettings.PetComponent.SizeDip, double.IsFinite(PetCrossSize) ? PetCrossSize : PetComponentSettings.DefaultSizeDip)) + TaskbarWidgetPadding * 2;
    private PetView ActivePet => _petOverflow?.IsVisible == true ? _petOverflow.View : TaskbarPet;

    public void BindPetInput(PetInputService source, PetWebEnvironment? environment = null, PetCompanionService? companion = null, DesktopWidgetZOrderService? zOrder = null)
    {
        if (_petDisposed) return;
        _petLease?.Dispose();
        _petLease = null;
        _petCompanion = companion;
        _petZOrder = zOrder;
        _petInput = source;
        _petEnvironment = environment;
        ReleasePetOverflow();
        TaskbarPet.BindEnvironment(environment);
        UpdatePetPresentation();
    }

    private void PetSurfaceSizeChanged(object sender, SizeChangedEventArgs args) => UpdatePetPresentation();

    private void UpdatePetPresentation()
    {
        var settings = PresentationSettings.PetComponent;
        TaskbarPet.Configure(settings);
        TaskbarPet.Width = TaskbarPet.Height = PetInlineSize;
        var active = !_petDisposed && IsLoaded && IsVisible && IsBarPresentation &&
            TaskbarPetSurface.Visibility == Visibility.Visible && !_isHostVisibilitySuspended &&
            _backgroundPruneLevel < MemoryPruneLevel.DisplayOff;
        var visible = TaskbarPetSurface.Visibility == Visibility.Visible;
        if (active && settings.AllowOverflow && Window.GetWindow(this) is { } owner)
        {
            TaskbarPet.SetPresentation(false, false);
            TaskbarPet.Visibility = Visibility.Hidden;
            if (_petOverflow is null)
            {
                _petOverflow = new PetOverflowHost(TaskbarPetSurface, owner, _petEnvironment, _petZOrder);
                _petOverflow.Moved += _petBubble.Reposition;
                _petOverflow.Activated += SpeakPet;
                _petOverflow.MenuRequested += async () => await OpenPetMenuAsync(_petOverflow.View);
                _petOverflow.DismissMenuPointer = DismissPetMenuPointer;
                _petOverflow.PositionSaved += SavePetPosition;
            }
            _petOverflow.Show(settings, _isVertical, _verticalTaskbarOnLeft);
        }
        else
        {
            if (!visible || !IsLoaded || !settings.AllowOverflow) ReleasePetOverflow();
            else _petOverflow?.Hide();
            TaskbarPet.Visibility = Visibility.Visible;
            TaskbarPet.SetPresentation(active, visible);
        }
        if (active && _petCompanion is not null)
            _petContextLease ??= _petCompanion.Subscribe((animation, beat) => { ActivePet.SetAmbient(_petCompanion.ArtworkColor); ActivePet.SetContext(animation, beat); }, (text, activation) => _petBubble.Show(ActivePet, text, false, activation), () =>
            {
                if (!ActivePet.IsVisible || IsPetBubbleOpen || ActivePet.IsInteracting) return false;
                ActivePet.SetInteraction("waking", 2000);
                return true;
            });
        if (active && _petCompanion is not null)
            _petStatusLease ??= _petCompanion.SubscribeStatus((key, text) => _petMenu?.IsOpen != true && _petBubble.ShowStatus(ActivePet, key, text));
        if (active && _petInput is not null)
        {
            if (!ReferenceEquals(_petLeaseView, ActivePet))
            {
                if (_petMenu is not null) _petMenu.IsOpen = false;
                _petBubble.Close();
                // 换到新的呈现窗口也要丢弃旧租约的排队点击。
                _petLease?.Dispose();
                _petLease = null;
                _petLeaseView = ActivePet;
            }
            _petLease ??= _petInput.Subscribe(input =>
            {
                if (_petDisposed || !IsLoaded) return;
                if (input.Blink && input.ObservedAtTicks > _petMenuOpenedAtTicks && _petMenu is { } menu)
                    ContextMenuHelper.CloseIfOutside(menu, (int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
                ActivePet.Present(input);
            });
        }
        else SuspendPetPresentation();
    }

    private void SuspendPetPresentation()
    {
        if (_petMenu is not null) _petMenu.IsOpen = false;
        _petBubble.Close();
        _petStatusLease?.Dispose();
        _petStatusLease = null;
        _petContextLease?.Dispose();
        _petContextLease = null;
        _petLease?.Dispose();
        _petLease = null;
        _petLeaseView = null;
        _petOverflow?.Hide();
        if (!IsLoaded) ReleasePetOverflow();
        TaskbarPet.SetPresentation(false, TaskbarPetSurface.Visibility == Visibility.Visible);
    }

    private void DisposePetPresentation()
    {
        if (_petDisposed) return;
        _petDisposed = true;
        SuspendPetPresentation();
        ReleasePetOverflow();
        _petBubble.Dispose();
        TaskbarPet.Dispose();
        _petInput = null;
        _petEnvironment = null;
    }

    private void ReleasePetOverflow()
    {
        _petOverflow?.Dispose();
        _petOverflow = null;
    }

    private void PetSurfaceMouseEnter(object sender, MouseEventArgs args)
    {
        AnimateComponentHover(TaskbarPetSurface, true);
        ActivePet.SetHovered(true);
    }
    private void PetSurfaceMouseLeave(object sender, MouseEventArgs args)
    {
        AnimateComponentHover(TaskbarPetSurface, false);
        ActivePet.SetHovered(false);
    }
    private async void PetSurfaceMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton == MouseButton.Right) { args.Handled = true; await OpenPetMenuAsync(TaskbarPetSurface); return; }
        if (DismissPetMenuPointer()) { args.Handled = true; return; }
        if (args.ChangedButton == MouseButton.Left) ActivePet.Press();
        if (args.ChangedButton == MouseButton.Left) _petPressPoint = args.GetPosition(this);
    }

    private static void SavePetPosition(FloatingBarPosition position)
    {
        var settings = SettingsManager.Current.PetComponent;
        SettingsManager.Current.PetComponent = settings with
        {
            Positions = settings.Positions.Where(p => !string.Equals(p.MonitorDeviceId, position.MonitorDeviceId, StringComparison.OrdinalIgnoreCase)).Append(position).ToArray()
        };
    }

    private void SpeakPet()
    {
        if (_petCompanion?.IsFocusActive == true) { ActivePet.SetInteraction("happy", 800); return; }
        if (_petCompanion is not null) _petBubble.Show(ActivePet, _petCompanion.Speak());
    }

    private void PetSurfaceMouseUp(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        var start = _petPressPoint;
        _petPressPoint = null;
        if (start is { } point && (args.GetPosition(this) - point).Length < SystemParameters.MinimumHorizontalDragDistance) SpeakPet();
        args.Handled = true;
    }

    private async void PetSurfaceKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Apps || (args.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) { args.Handled = true; await OpenPetMenuAsync(TaskbarPetSurface, true); }
        else if (args.Key is Key.Enter or Key.Space) { SpeakPet(); args.Handled = true; }
    }

    public bool IsPetComponentClick(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, TaskbarPetSurface)) return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : null;
        }
        return false;
    }
}
