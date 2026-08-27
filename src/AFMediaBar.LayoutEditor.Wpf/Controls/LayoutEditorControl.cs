using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Ports;

namespace AFMediaBar.LayoutEditor.Wpf.Controls;

/// <summary>
/// Independent editor host. The WPF settings page embeds this control and
/// supplies application-specific preview and mutation callbacks at the shell
/// boundary; pointer state and viewport ownership stay in this module.
/// </summary>
public sealed class LayoutEditorControl : Grid, IDisposable
{
    private readonly Border _hostSurface;
    private LayoutEditorSession? _session;
    private ILayoutPreviewHost? _previewHost;
    private Func<LayoutProfile, UIElement?>? _previewFactory;

    public LayoutEditorControl()
    {
        ClipToBounds = true;
        _hostSurface = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Children.Add(_hostSurface);
    }

    public LayoutEditorSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
            {
                return;
            }

            if (_session is not null)
            {
                _session.StateChanged -= Session_OnStateChanged;
            }

            _session = value;
            if (_session is not null)
            {
                _session.StateChanged += Session_OnStateChanged;
            }

            RenderPreview();
        }
    }

    public ILayoutPreviewHost? PreviewHost
    {
        get => _previewHost;
        set
        {
            _previewHost = value;
            RenderPreview();
        }
    }

    public UIElement? PreviewContent
    {
        get => _hostSurface.Child;
        set => _hostSurface.Child = value;
    }

    public Func<LayoutProfile, UIElement?>? PreviewFactory
    {
        get => _previewFactory;
        set
        {
            _previewFactory = value;
            RenderPreview();
        }
    }

    public void Dispose()
    {
        if (_session is not null)
        {
            _session.StateChanged -= Session_OnStateChanged;
        }

        _session = null;
        _previewHost = null;
        _previewFactory = null;
        Children.Clear();
    }

    private void Session_OnStateChanged(object? sender, EventArgs e) => RenderPreview();

    private void RenderPreview()
    {
        if (_session is null)
        {
            return;
        }

        var profile = _session.Document.Get(_session.ProfileKey);
        if (_previewFactory is not null)
        {
            _hostSurface.Child = _previewFactory(profile);
        }

        _previewHost?.Show(profile, _session.PreviewBounds, _session.SelectedInstanceId);
    }
}
