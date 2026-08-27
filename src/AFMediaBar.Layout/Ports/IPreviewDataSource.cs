namespace AFMediaBar.Layout.Ports;

/// <summary>
/// Supplies deterministic preview data to the editor. Production media
/// services are adapters; the Sandbox can provide a fixed implementation.
/// </summary>
public interface IPreviewDataSource
{
    LayoutPreviewData GetSnapshot();
}

public sealed record LayoutPreviewData(
    string Title,
    string Artist,
    string Source,
    string? ArtworkUri = null);
