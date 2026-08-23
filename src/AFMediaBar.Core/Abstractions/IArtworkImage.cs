namespace AFMediaBar.Abstractions;

/// <summary>
/// Marker contract for an artwork image owned and decoded by the active UI shell.
/// </summary>
public interface IArtworkImage;

/// <summary>
/// Carries a decoded shell image and a stable content fingerprint.
/// </summary>
public readonly record struct ArtworkDecodeResult(
    IArtworkImage? Artwork,
    string? Fingerprint);

/// <summary>
/// Decodes a bounded media stream without exposing a UI framework type to Core or Platform.
/// </summary>
public interface IArtworkDecoder
{
    Task<ArtworkDecodeResult> DecodeAsync(
        Stream source,
        long? sourceLength,
        CancellationToken cancellationToken);
}
