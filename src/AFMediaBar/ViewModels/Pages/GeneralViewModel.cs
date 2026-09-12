namespace AFMediaBar.ViewModels.Pages;

/// <summary>只包含应用级常规设置；歌词与滚轮分别由专页拥有。 / App-level general settings only; lyrics and wheel settings have dedicated pages.</summary>
public partial class GeneralViewModel : ObservableObject
{
    public void ResetGeneral() => Classes.Settings.SettingsManager.ResetGeneral();
}
