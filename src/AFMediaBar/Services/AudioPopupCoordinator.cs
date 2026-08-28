using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// Owns audio popup lifetime and delayed volume close. Audio enumeration and
/// volume operations remain in MainWindow.
/// </summary>
internal sealed class AudioPopupCoordinator : IDisposable
{
    private readonly Popup _outputDevicePopup;
    private readonly Popup _outputDeviceStatusPopup;
    private readonly Popup _volumeControlPopup;
    private readonly Popup _volumeStatusPopup;
    private readonly Action _volumeInteractionClosed;
    private readonly DispatcherTimer _volumeCloseTimer;
    private bool _disposed;

    internal AudioPopupCoordinator(Popup outputDevicePopup, Popup outputDeviceStatusPopup,
        Popup volumeControlPopup, Popup volumeStatusPopup, Dispatcher dispatcher,
        Action volumeInteractionClosed)
    {
        _outputDevicePopup = outputDevicePopup;
        _outputDeviceStatusPopup = outputDeviceStatusPopup;
        _volumeControlPopup = volumeControlPopup;
        _volumeStatusPopup = volumeStatusPopup;
        _volumeInteractionClosed = volumeInteractionClosed;
        _volumeCloseTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            OnVolumeCloseTimerTick, dispatcher);
    }

    internal bool IsOutputDeviceOpen => _outputDevicePopup.IsOpen;
    internal bool IsOutputDeviceStatusOpen => _outputDeviceStatusPopup.IsOpen;
    internal bool IsVolumeControlOpen => _volumeControlPopup.IsOpen;
    internal bool IsVolumeStatusOpen => _volumeStatusPopup.IsOpen;
    internal bool IsAnyPrimaryOpen => IsOutputDeviceOpen || IsVolumeControlOpen;
    internal bool IsVolumeClosePending => _volumeCloseTimer.IsEnabled;

    internal void StartVolumeInteractionClose()
    {
        if (_disposed) return;
        _volumeCloseTimer.Stop();
        _volumeCloseTimer.Start();
    }

    internal void StopVolumeInteractionClose() => _volumeCloseTimer.Stop();

    internal void CloseAll()
    {
        _outputDevicePopup.IsOpen = false;
        _outputDeviceStatusPopup.IsOpen = false;
        _volumeControlPopup.IsOpen = false;
        _volumeStatusPopup.IsOpen = false;
        _volumeCloseTimer.Stop();
        _volumeInteractionClosed();
    }

    internal void CloseOutputDevicePopups()
    {
        _outputDevicePopup.IsOpen = false;
        _outputDeviceStatusPopup.IsOpen = false;
    }

    internal void CloseVolumePopups()
    {
        _volumeControlPopup.IsOpen = false;
        _volumeStatusPopup.IsOpen = false;
        _volumeCloseTimer.Stop();
        _volumeInteractionClosed();
    }

    internal void CloseVolumeStatus()
    {
        _volumeStatusPopup.IsOpen = false;
        _volumeCloseTimer.Stop();
        _volumeInteractionClosed();
    }

    internal void SetPlacement(PlacementMode placement, double horizontalOffset, double verticalOffset)
    {
        if (_disposed) return;
        foreach (var popup in new[]
        {
            _volumeControlPopup,
            _outputDevicePopup,
            _outputDeviceStatusPopup,
            _volumeStatusPopup
        })
        {
            popup.Placement = placement;
            popup.HorizontalOffset = horizontalOffset;
            popup.VerticalOffset = verticalOffset;
        }
    }

    internal void SetPlacementTargets(FrameworkElement outputTarget, FrameworkElement volumeTarget)
    {
        if (_disposed) return;
        _volumeControlPopup.PlacementTarget = volumeTarget;
        _volumeStatusPopup.PlacementTarget = volumeTarget;
        _outputDevicePopup.PlacementTarget = outputTarget;
        _outputDeviceStatusPopup.PlacementTarget = outputTarget;
    }

    internal void SetPlacementTarget(MediaCommandKind command, FrameworkElement target)
    {
        if (_disposed) return;
        if (command == MediaCommandKind.SelectOutputDevice)
        {
            _outputDevicePopup.PlacementTarget = target;
            _outputDeviceStatusPopup.PlacementTarget = target;
        }
        else if (command == MediaCommandKind.AdjustVolume)
        {
            _volumeControlPopup.PlacementTarget = target;
            _volumeStatusPopup.PlacementTarget = target;
        }
    }

    private void OnVolumeCloseTimerTick(object? sender, EventArgs e)
    {
        _volumeCloseTimer.Stop();
        _volumeStatusPopup.IsOpen = false;
        _volumeControlPopup.IsOpen = false;
        _volumeInteractionClosed();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseAll();
        _volumeCloseTimer.Tick -= OnVolumeCloseTimerTick;
    }
}
