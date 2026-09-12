using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>歌词设置页面。 / Lyric settings page.</summary>
public partial class LyricsPage : INavigableView<LyricsViewModel>
{
    public LyricsViewModel ViewModel { get; }
    public LyricsPage(LyricsViewModel viewModel) { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
    private async void ResetButton_Click(object sender, RoutedEventArgs e) { if (await SettingsResetDialog.ConfirmAsync("歌词页")) ViewModel.ResetLyrics(); }
}
