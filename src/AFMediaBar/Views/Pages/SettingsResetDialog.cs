namespace AFMediaBar.Views.Pages;

using AFMediaBar.Resources;
using Wpf.Ui;
using Wpf.Ui.Controls;

internal static class SettingsResetDialog
{
    // A process-wide strong dialog host would retain the closed window and its last cached page.
    private static WeakReference<ContentDialogHost>? _host;
    private static CancellationToken _lifetime;
    public static void SetHost(ContentDialogHost host, CancellationToken lifetime)
    {
        _host = new(host);
        _lifetime = lifetime;
    }
    public static void ClearHost(ContentDialogHost host)
    {
        if (_host?.TryGetTarget(out var current) == true && ReferenceEquals(current, host))
        {
            _host = null;
            _lifetime = CancellationToken.None;
        }
    }

    /// <summary>
    /// 询问是否恢复默认设置。
    ///
    /// 参数是**作用域名称的文案键**而不是名称本身：句子由语言文件拼装，调用方只说明"重置哪一块"，
    /// 因此切换语言后对话框跟着换语言，而不需要每个页面各自维护一份已翻译的名称。
    /// Asks whether to restore the defaults.
    ///
    /// The argument is the <em>localization key</em> of a scope name rather than the name itself: the language file composes
    /// the sentence and the caller only says which part is being reset, so the dialog follows a language change without every
    /// page keeping its own translated copy of the name.
    /// </summary>
    /// <param name="scopeKey">作用域名称的文案键，例如 <c>Common.Page.Lyrics</c>。/ Localization key of the scope name, such as <c>Common.Page.Lyrics</c>.</param>
    /// <param name="resetAll">是否使用覆盖所有页面的全局重置提示。</param>
    /// <param name="cancellationToken">Cancellation for the editor whose reset is being confirmed.</param>
    public static async Task<bool> ConfirmAsync(string scopeKey, bool resetAll = false, CancellationToken cancellationToken = default)
    {
        if (_lifetime.IsCancellationRequested || cancellationToken.IsCancellationRequested || _host is null || !_host.TryGetTarget(out var host) || host is null) return false;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime, cancellationToken);
        var lifetime = request.Token;
        var service = new ContentDialogService();
        service.SetDialogHost(host);
        var dialog = new ContentDialog
        {
            Title = Translations.Get("Common.ResetDialog.Title"),
            Content = resetAll
                ? Translations.Get("Common.ResetDialog.AllContent")
                : Translations.Format("Common.ResetDialog.Content", Translations.Get(scopeKey)),
            PrimaryButtonText = Translations.Get("Common.ResetDialog.Confirm"),
            CloseButtonText = Translations.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        try { return await service.ShowAsync(dialog, lifetime) == ContentDialogResult.Primary; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return false; }
    }
}
