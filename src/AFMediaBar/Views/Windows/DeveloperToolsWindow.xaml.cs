// 呈现测试动作与复制意图；窗口拥有本次视图模型，业务服务由 App 拥有。
using AFMediaBar.Classes.Services;
using AFMediaBar.ViewModels.Windows;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>单次工具会话的独立 WPF-UI 窗口。</summary>
public partial class DeveloperToolsWindow : FluentWindow
{
    public DeveloperToolsViewModel ViewModel { get; }

    public DeveloperToolsWindow(DeveloperToolsViewModel viewModel, WindowAppearanceService appearance)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        appearance.Attach(this);
        ViewModel.CopyRequested += OnCopyRequested;
    }

    private void OnCopyRequested(string text)
    {
        try { if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { ViewModel.ReportClipboardFailure(); }
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.CopyRequested -= OnCopyRequested;
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
