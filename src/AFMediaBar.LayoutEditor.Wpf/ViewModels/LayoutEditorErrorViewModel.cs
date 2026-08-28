using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.LayoutEditor.Wpf.ViewModels;

public sealed partial class LayoutEditorErrorViewModel : ObservableObject
{
    [ObservableProperty]
    private string? code;

    [ObservableProperty]
    private string? messageResourceKey;

    [ObservableProperty]
    private bool isWarning;

    public bool HasError => !string.IsNullOrWhiteSpace(Code);

    public void Set(string? errorCode, string? messageKey, bool warning = false)
    {
        Code = errorCode;
        MessageResourceKey = messageKey;
        IsWarning = warning;
        OnPropertyChanged(nameof(HasError));
    }
}
