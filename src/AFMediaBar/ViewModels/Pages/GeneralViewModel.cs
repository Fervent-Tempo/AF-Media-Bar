namespace AFMediaBar.ViewModels.Pages
{
    public partial class GeneralViewModel : ObservableObject
    {
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
    }
}
