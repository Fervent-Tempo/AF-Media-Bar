// Presents release highlights only; requests are cancelled when this singleton page leaves the visual tree.
using System.Diagnostics;
using System.Windows;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>Read-only latest and historical version highlights.</summary>
public partial class ReleaseHighlightsPage : INavigableView<ReleaseHighlightsViewModel>
{
    /// <summary>Injected bindable page state.</summary>
    public ReleaseHighlightsViewModel ViewModel { get; }

    /// <summary>Creates the page; no requests run in the constructor.</summary>
    public ReleaseHighlightsPage(ReleaseHighlightsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();
    private void OnPageUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();
    private void OnOpenReleaseNotes(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReleaseHighlightsItem item } &&
            ReleaseHighlightsPolicy.IsSafeNotesUrl(item.ReleaseNotesUrl))
        {
            try { Process.Start(new ProcessStartInfo(item.ReleaseNotesUrl!) { UseShellExecute = true }); }
            catch (Exception) { /* The page remains usable when no browser can handle the official link. */ }
        }
    }
}
