using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
namespace AFMediaBar.Views.Pages;
/// <summary>歌词设置页面。 / Lyric settings page.</summary>
public partial class LyricsPage : INavigableView<LyricsViewModel>
{
    public LyricsViewModel ViewModel { get; }
    /// <summary>请求文字布局入口，由当前设置窗口负责导航。</summary>
    public static readonly RoutedEvent OpenTextLayoutEvent = EventManager.RegisterRoutedEvent(nameof(OpenTextLayout), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(LyricsPage));
    public event RoutedEventHandler OpenTextLayout { add => AddHandler(OpenTextLayoutEvent, value); remove => RemoveHandler(OpenTextLayoutEvent, value); }
    private void OpenTextLayout_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenTextLayoutEvent));
    public LyricsPage(LyricsViewModel viewModel) { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
    /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs e) { if (await SettingsResetDialog.ConfirmAsync("Common.Page.Lyrics", cancellationToken: ViewModel.ContextCancellationToken)) ViewModel.ResetLyrics(); }

    private async void ManageArtistSeparators_Click(object sender, RoutedEventArgs e)
    {
        var separators = await ArtistSeparatorsEditor.EditAsync(ViewModel.ArtistSeparatorsText, ViewModel.DefaultArtistSeparatorsText, ViewModel.ContextCancellationToken);
        if (separators is not null) ViewModel.ApplyArtistSeparators(separators);
    }
}
