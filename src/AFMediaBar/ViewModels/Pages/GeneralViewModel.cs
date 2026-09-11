namespace AFMediaBar.ViewModels.Pages
{
    /// <summary>
    /// 通用设置页面的视图模型。
    /// View model for the General settings page.
    /// </summary>
    public partial class GeneralViewModel : ObservableObject
    {
        public GeneralViewModel() => Classes.Settings.SettingsManager.SettingsChanged += OnSettingsChanged;

        /// <summary>
        /// 调用 GetValues，提供 API。
        /// Provides the public GetValues entry point required by this component.
        /// </summary>
        public Array TrayWheelBehaviors => Enum.GetValues(typeof(Classes.Settings.TrayWheelBehavior));

        public Classes.Settings.TrayWheelBehavior TrayWheelBehavior
        {
            get => Classes.Settings.SettingsManager.Current.TrayWheelBehavior;
            set
            {
                if (Classes.Settings.SettingsManager.Current.TrayWheelBehavior == value)
                {
                    return;
                }

                Classes.Settings.SettingsManager.SetTrayWheelBehavior(value);
                OnPropertyChanged();
            }
        }

        public bool LyricsEnabled
        {
            get => Classes.Settings.SettingsManager.Current.LyricsEnabled;
            set
            {
                if (Classes.Settings.SettingsManager.Current.LyricsEnabled == value)
                    return;

                Classes.Settings.SettingsManager.SetLyricsEnabled(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConfigureTwoLineLyrics));
                OnPropertyChanged(nameof(CanSelectLyricsSecondaryLine));
            }
        }

        public bool TwoLineLyricsEnabled
        {
            get => Classes.Settings.SettingsManager.Current.TwoLineLyricsEnabled;
            set
            {
                if (Classes.Settings.SettingsManager.Current.TwoLineLyricsEnabled == value)
                    return;

                Classes.Settings.SettingsManager.SetTwoLineLyricsEnabled(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSelectLyricsSecondaryLine));
            }
        }

        public Classes.Settings.LyricsSecondaryLineMode LyricsSecondaryLineMode
        {
            get => Classes.Settings.SettingsManager.Current.LyricsSecondaryLineMode;
            set
            {
                if (Classes.Settings.SettingsManager.Current.LyricsSecondaryLineMode == value)
                    return;

                Classes.Settings.SettingsManager.SetLyricsSecondaryLineMode(value);
                OnPropertyChanged();
            }
        }

        public bool CanConfigureTwoLineLyrics => LyricsEnabled;

        public bool CanSelectLyricsSecondaryLine => LyricsEnabled && TwoLineLyricsEnabled;

        public void ResetGeneral() => Classes.Settings.SettingsManager.ResetGeneral();

        private void OnSettingsChanged(object? sender, Classes.Settings.SettingsChangedEventArgs e)
        {
            if (e.ResetScope is not (Classes.Settings.SettingsResetScope.General or Classes.Settings.SettingsResetScope.All)) return;
            OnPropertyChanged(nameof(TrayWheelBehavior));
            OnPropertyChanged(nameof(LyricsEnabled));
            OnPropertyChanged(nameof(TwoLineLyricsEnabled));
            OnPropertyChanged(nameof(LyricsSecondaryLineMode));
            OnPropertyChanged(nameof(CanConfigureTwoLineLyrics));
            OnPropertyChanged(nameof(CanSelectLyricsSecondaryLine));
        }
    }
}
