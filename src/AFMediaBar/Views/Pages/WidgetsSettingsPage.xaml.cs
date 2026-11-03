// 页面只呈现参数与导航意图；设备枚举和取消由作用域内的编辑器协调。
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Input;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>呈现小组件参数、快速启动列表与返回布局的入口。</summary>
public partial class WidgetsSettingsPage : INavigableView<WidgetsSettingsViewModel>
{
    public WidgetsSettingsViewModel ViewModel { get; }
    private readonly PetWebEnvironment? _petEnvironment;
    private bool _petPreviewExpanded;
    private readonly PetVoiceService? _petVoice;
    private readonly PetCompanionService? _companion;
    private readonly AFMediaBar.Components.PetSpeechBubble _petBubble = new();
    private readonly System.Windows.Threading.DispatcherTimer _petDemoTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private long _petDemoUntil;
    internal bool IsPetDemoRunning => _petDemoTimer.IsEnabled;

    private void PetDemoPlay(object sender, RoutedEventArgs args)
    {
        _petDemoUntil = Environment.TickCount64 + 1800;
        _petDemoTimer.Start();
    }
    private void AdvancePetDemo(object? sender, EventArgs args)
    {
        var remaining = _petDemoUntil - Environment.TickCount64;
        if (!IsLoaded || !_petPreviewExpanded || remaining <= 0)
        {
            _petDemoTimer.Stop();
            PetPreview.SetContext("idle", 0);
            return;
        }
        PetPreview.SetContext("listening", Math.Exp(-((1800 - remaining) % 450) / 90d) * 0.8);
    }
    private void PetDemoSpeak(object sender, RoutedEventArgs args)
    {
        _petDemoTimer.Stop();
        PetPreview.SetContext("happy", 0);
        var text = _companion?.NextPhrase() ?? AFMediaBar.Resources.Translations.Get("Widgets.Pet.Line.Idle.0");
        _petBubble.Show(PetPreview, text);
        _petVoice?.Play(text);
    }
    private void PetDemoNotify(object sender, RoutedEventArgs args)
    {
        _petDemoTimer.Stop();
        PetPreview.SetContext("notifying", 0);
        _petBubble.Show(PetPreview, AFMediaBar.Resources.Translations.Get("Widgets.Pet.Demo.Message"));
    }
    private void PetDemoVoice(object sender, RoutedEventArgs args) => _petVoice?.Play(_companion?.NextPhrase() ?? AFMediaBar.Resources.Translations.Get("Widgets.Pet.Line.Idle.0"), true);

    public WidgetsSettingsPage(WidgetsSettingsViewModel viewModel, PetWebEnvironment? petEnvironment = null, PetVoiceService? petVoice = null, PetCompanionService? companion = null)
    {
        ViewModel = viewModel;
        _petEnvironment = petEnvironment;
        _petVoice = petVoice;
        _companion = companion;
        DataContext = this;
        InitializeComponent();
        _petDemoTimer.Tick += AdvancePetDemo;
        Loaded += (_, _) =>
        {
            ViewModel.Components.PropertyChanged += PreviewSettingsChanged;
            RefreshPetPreview();
        };
        Unloaded += (_, _) =>
        {
            ViewModel.Components.PropertyChanged -= PreviewSettingsChanged;
            _petDemoTimer.Stop();
            _petBubble.Close();
            _petVoice?.Stop();
            PetPreview.SetContext("idle", 0);
            PetPreview.SetPresentation(false, false);
        };
    }

    private void PetPreviewExpanded(object sender, RoutedEventArgs args)
    {
        _petPreviewExpanded = true;
        PetPreview.BindEnvironment(_petEnvironment);
        RefreshPetPreview();
    }

    private void PetPreviewCollapsed(object sender, RoutedEventArgs args)
    {
        _petDemoTimer.Stop();
        _petBubble.Close();
        PetPreview.SetContext("idle", 0);
        _petPreviewExpanded = false;
        PetPreview.SetPresentation(false, false);
    }

    private void PreviewSettingsChanged(object? sender, PropertyChangedEventArgs args) => RefreshPetPreview();
    private void RefreshPetPreview()
    {
        if (!_petPreviewExpanded || !IsLoaded) return;
        PetPreview.Configure(ViewModel.Components.PetSettings with { SizeDip = 120 });
        PetPreview.SetPresentation(true, true);
    }
    private void PetPreviewMouseEnter(object sender, MouseEventArgs args) => PetPreview.SetHovered(true);
    private void PetPreviewMouseLeave(object sender, MouseEventArgs args) => PetPreview.SetHovered(false);
    private void PetPreviewMouseMove(object sender, MouseEventArgs args) =>
        PetPreview.Present(new(((FrameworkElement)sender).PointToScreen(args.GetPosition((IInputElement)sender)), false));
    private void PetPreviewMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        PetPreview.Press();
        PetPreview.Present(new(((FrameworkElement)sender).PointToScreen(args.GetPosition((IInputElement)sender)), true));
        args.Handled = true;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs args)
    {
        SettingsRevealAnimator.Play(sender as Panel);
        await ViewModel.RefreshAsync();
    }

    private async void BrowseQuickLaunch_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = AFMediaBar.Resources.Translations.Get("Media.Dialog.Browse.Title"),
            Filter = AFMediaBar.Resources.Translations.Get("Media.Dialog.Browse.Filter"),
            Multiselect = false, CheckFileExists = true
        };
        if (dialog.ShowDialog() == true) await ViewModel.Note.AddFileAsync(dialog.FileName);
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs args)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.Widgets", cancellationToken: ViewModel.ContextCancellationToken))
            ViewModel.Reset();
    }
}
