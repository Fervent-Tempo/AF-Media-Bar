// Presents taskbar text, dimensions and component layout; configuration remains owned by the settings boundary.
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using System.Windows.Controls;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>Appearance for the taskbar environment being edited.</summary>
public partial class AppearancePage : INavigableView<TaskbarAppearanceViewModel>
{
    /// <summary>Requests the shared application font group from the owning settings window.</summary>
    public static readonly RoutedEvent OpenApplicationFontsEvent = EventManager.RegisterRoutedEvent(nameof(OpenApplicationFonts), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AppearancePage));
    /// <summary>Bubbles the font navigation intent without retaining the navigation window.</summary>
    public event RoutedEventHandler OpenApplicationFonts { add => AddHandler(OpenApplicationFontsEvent, value); remove => RemoveHandler(OpenApplicationFontsEvent, value); }

    /// <summary>Taskbar presentation state.</summary>
    public TaskbarAppearanceViewModel ViewModel { get; }
    /// <summary>Creates the taskbar appearance page.</summary>
    public AppearancePage(TaskbarAppearanceViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
    private void OpenApplicationFonts_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenApplicationFontsEvent));
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.TaskbarAppearance", cancellationToken: ViewModel.ContextCancellationToken)) ViewModel.ResetAppearance();
    }
}
