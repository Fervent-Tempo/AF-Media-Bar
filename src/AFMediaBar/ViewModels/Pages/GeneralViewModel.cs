namespace AFMediaBar.ViewModels.Pages
{
    /// <summary>
    /// 通用设置页面的视图模型。
    /// View model for the General settings page.
    /// </summary>
    public partial class GeneralViewModel : ObservableObject
    {
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
    }
}
