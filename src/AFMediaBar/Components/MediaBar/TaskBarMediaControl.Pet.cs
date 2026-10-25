// 媒体栏拥有宠物输入租约和呈现状态；共享输入服务由应用容器释放。
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private PetInputService? _petInput;
    private IDisposable? _petLease;
    private PetView? _petLeaseView;
    private bool _petDisposed;
    private PetWebEnvironment? _petEnvironment;
    private PetOverflowHost? _petOverflow;
    private double PetCrossSize => Math.Max(1, (_isVertical ? MainBorder.Width : MainBorder.Height) - 4);
    private double PetInlineSize => Math.Min(PresentationSettings.PetComponent.SizeDip,
        double.IsFinite(PetCrossSize) ? PetCrossSize : PetComponentSettings.DefaultSizeDip);
    private double PetSurfaceLength => (PresentationSettings.PetComponent.AllowOverflow
        ? PresentationSettings.PetComponent.SizeDip : PetInlineSize) + TaskbarWidgetPadding * 2;
    private PetView ActivePet => _petOverflow?.IsVisible == true ? _petOverflow.View : TaskbarPet;

    public void BindPetInput(PetInputService source, PetWebEnvironment? environment = null)
    {
        if (_petDisposed) return;
        _petLease?.Dispose();
        _petLease = null;
        _petInput = source;
        _petEnvironment = environment;
        ReleasePetOverflow();
        TaskbarPet.BindEnvironment(environment);
        UpdatePetPresentation();
    }

    private void UpdatePetPresentation()
    {
        var settings = PresentationSettings.PetComponent;
        TaskbarPet.Configure(settings);
        TaskbarPet.Width = TaskbarPet.Height = PetInlineSize;
        var active = !_petDisposed && IsLoaded && IsVisible && IsBarPresentation &&
            TaskbarPetSurface.Visibility == Visibility.Visible && !_isHostVisibilitySuspended &&
            !_isPlacementMotionActive && _backgroundPruneLevel < MemoryPruneLevel.DisplayOff;
        var visible = TaskbarPetSurface.Visibility == Visibility.Visible;
        if (active && settings.AllowOverflow && Window.GetWindow(this) is { } owner)
        {
            TaskbarPet.SetPresentation(false, false);
            TaskbarPet.Visibility = Visibility.Hidden;
            _petOverflow ??= new PetOverflowHost(TaskbarPetSurface, owner, _petEnvironment);
            _petOverflow.Show(settings, _isVertical, _verticalTaskbarOnLeft);
        }
        else
        {
            if (!visible || !IsLoaded || !settings.AllowOverflow) ReleasePetOverflow();
            else _petOverflow?.Hide();
            TaskbarPet.Visibility = Visibility.Visible;
            TaskbarPet.SetPresentation(active, visible);
        }
        if (active && _petInput is not null)
        {
            if (!ReferenceEquals(_petLeaseView, ActivePet))
            {
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
        if (args.ChangedButton is MouseButton.Left or MouseButton.Right) ActivePet.Press();
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
