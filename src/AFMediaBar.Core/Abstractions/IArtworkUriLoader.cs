namespace AFMediaBar.Abstractions;

/// <summary>
/// Loads artwork from a remote URI and decodes it with the active UI shell.
/// </summary>
public interface IArtworkUriLoader
{
    Task<ArtworkDecodeResult> LoadAsync(Uri uri, CancellationToken cancellationToken);
}
