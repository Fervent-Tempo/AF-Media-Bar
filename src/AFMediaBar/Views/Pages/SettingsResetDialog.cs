namespace AFMediaBar.Views.Pages;

using Wpf.Ui;
using Wpf.Ui.Controls;

internal static class SettingsResetDialog
{
    private static readonly ContentDialogService DialogService = new();

    public static void SetHost(ContentDialogHost host) => DialogService.SetDialogHost(host);

    public static async Task<bool> ConfirmAsync(string scope)
    {
        var dialog = new ContentDialog
        {
            Title = "恢复默认设置",
            Content = $"确定要恢复{scope}默认设置吗？\n当前媒体播放不会受到影响。",
            PrimaryButtonText = "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        return await DialogService.ShowAsync(dialog, CancellationToken.None) == ContentDialogResult.Primary;
    }
}
