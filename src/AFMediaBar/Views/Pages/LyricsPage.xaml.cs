using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>呈现文字与歌词设置，编辑器与服务由既有作用域拥有。</summary>
public partial class LyricsPage : INavigableView<LyricsViewModel>
{
    public LyricsViewModel ViewModel { get; }
    public ContentLayoutViewModel LayoutEditor { get; }
    public TaskbarAppearanceViewModel Taskbar => LayoutEditor.Layout;
    public bool HasTaskbarSettings => true;
    public LyricsPage(ContentLayoutViewModel editors) { LayoutEditor = editors; ViewModel = editors.Lyrics; DataContext = this; InitializeComponent(); }
    private void OpenApplicationFonts_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(AppearancePage.OpenApplicationFontsEvent));
    /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs e) { if (await SettingsResetDialog.ConfirmAsync("Settings.Page.TextAndLyrics", cancellationToken: ViewModel.ContextCancellationToken)) ViewModel.ResetTextAndLyrics(); }

    private async void ManageArtistSeparators_Click(object sender, RoutedEventArgs e)
    {
        var separators = await ArtistSeparatorsEditor.EditAsync(ViewModel.ArtistSeparatorsText, ViewModel.DefaultArtistSeparatorsText, ViewModel.ContextCancellationToken);
        if (separators is not null) ViewModel.ApplyArtistSeparators(separators);
    }
}
