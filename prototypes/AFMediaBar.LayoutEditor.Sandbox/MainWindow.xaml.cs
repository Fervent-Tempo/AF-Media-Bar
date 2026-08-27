using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;
using AFMediaBar.LayoutEditor.Wpf.Controls;

namespace AFMediaBar.LayoutEditor.Sandbox;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var document = LayoutDefaultTemplates.LoadDocument();
        var session = new LayoutEditorSession(document);
        var editor = new LayoutEditorControl
        {
            Session = session,
            PreviewHost = new SandboxPreviewHost(StatusText)
        };
        EditorHost.Content = editor;
    }

    private sealed class SandboxPreviewHost(TextBlock status) : ILayoutPreviewHost
    {
        public void Show(LayoutProfile profile, LayoutGridRect? previewBounds, string? selectedInstanceId)
        {
            status.Text = $"Sandbox preview | {profile.Key} | containers: {profile.Containers.Count} | selected: {selectedInstanceId ?? "none"} | ghost: {previewBounds?.ToString() ?? "none"}";
        }
    }
}
