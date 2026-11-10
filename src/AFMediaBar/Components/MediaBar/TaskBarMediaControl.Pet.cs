// 媒体栏拥有宠物输入租约和呈现状态；共享输入服务由应用容器释放。
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Resources;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private readonly PetSpeechBubble _petBubble = new();
    public bool IsPetBubbleOpen => _petBubble.IsOpen || _petMenu?.IsOpen == true;
    private ContextMenu? _petMenu;
    public event EventHandler? PetSettingsRequested;
    private async void OpenPetMenu(FrameworkElement target)
    {
        if (_petMenu?.IsOpen == true || _petCompanion is null) return;
        ActivePet.ResetPetting();
        ActivePet.InputSuspended = true;
        var menu = new ContextMenu { PlacementTarget = target };
        _petMenu = menu;
        void Add(string key, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = Translations.Get("Widgets.Pet.Menu." + key), IsEnabled = enabled };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        var snapshot = _petCompanion.Snapshot;
        Add(snapshot.IsPlaying ? "Pause" : "Play", () => TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty), snapshot.CanPlayPause);
        Add("Previous", () => SkipPreviousRequested?.Invoke(this, EventArgs.Empty), snapshot.CanSkipPrevious);
        Add("Next", () => SkipNextRequested?.Invoke(this, EventArgs.Empty), snapshot.CanSkipNext);
        var mute = new MenuItem { Header = Translations.Get("Widgets.Pet.Menu.Mute"), IsEnabled = false };
        mute.Click += async (_, _) => { try { await _petCompanion.ToggleMuteAsync(); } catch (Exception error) { AFMediaBar.Classes.Services.AppLogService.Current?.Warn("Pet", error.Message); } };
        menu.Items.Add(mute);
        menu.Items.Add(new Separator());
        Add("Dock", () => { var s = SettingsManager.Current.PetComponent; SettingsManager.Current.PetComponent = s with { AllowOverflow = true, FreePlacement = false }; });
        Add("Settings", () => PetSettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Closed += (_, _) => { ActivePet.InputSuspended = false; ActivePet.ResetPetting(); if (ReferenceEquals(_petMenu, menu)) _petMenu = null; };
        menu.IsOpen = true;
        try
        {
            var volume = await _petCompanion.ReadVolumeAsync();
            if (!_petDisposed && menu.IsOpen && snapshot.SourceId == _petCompanion.Snapshot.SourceId && volume is not null)
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
                _petOverflow.MenuRequested += () => OpenPetMenu(_petOverflow.View);
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
            _petContextLease ??= _petCompanion.Subscribe((animation, beat) => { ActivePet.SetPlayback(_petCompanion.Snapshot.IsConnected && !_petCompanion.Snapshot.IsPlaying); ActivePet.SetAmbient(_petCompanion.ArtworkColor); ActivePet.SetContext(animation, beat); }, (text, activation) => _petBubble.Show(ActivePet, text, false, activation));
        if (active && _petCompanion is not null)
            _petStatusLease ??= _petCompanion.SubscribeStatus((key, text) => _petMenu?.IsOpen != true && _petBubble.ShowStatus(ActivePet, key, text));
        if (active && _petInput is not null)
        {
            if (!ReferenceEquals(_petLeaseView, ActivePet))
            {
                _petBubble.Close();
                // 换到新的呈现窗口也要丢弃旧租约的排队点击。
                _petLease?.Dispose();
                _petLease = null;
                _petLeaseView = ActivePet;
            }
            _petLease ??= _petInput.Subscribe(input => { if (!_petDisposed && IsLoaded) ActivePet.Present(input); });
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
    private void PetSurfaceMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton == MouseButton.Right) { OpenPetMenu(TaskbarPetSurface); args.Handled = true; return; }
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

    private void PetSurfaceKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Apps || (args.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) { OpenPetMenu(TaskbarPetSurface); args.Handled = true; }
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
