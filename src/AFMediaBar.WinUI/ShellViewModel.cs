using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.WinUI;

public sealed class ShellViewModel : ObservableObject
{
    private string _status = string.Empty;

    public ShellViewModel(Action openSettings, Action exit)
    {
        OpenSettingsCommand = new RelayCommand(openSettings);
        ExitCommand = new RelayCommand(exit);
    }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand ExitCommand { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
