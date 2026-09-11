namespace AFMediaBar.ViewModels.Pages
{
    /// <summary>
    /// 关于页面的视图模型。
    /// View model for the About page.
    /// </summary>
    public partial class AboutViewModel : ObservableObject
    {
        public void ResetAll() => Classes.Settings.SettingsManager.ResetAll();
    }
}
