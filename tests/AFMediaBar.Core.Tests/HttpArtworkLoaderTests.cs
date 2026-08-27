using System.IO;
using System.Net;
using System.Net.Http;
using AFMediaBar.Abstractions;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class HttpArtworkLoaderTests
{
    [TestMethod]
    public async Task HttpArtworkLoader_DecodesSuccessfulResponse()
    {
        var decoder = new StubArtworkDecoder();
        var content = new ByteArrayContent([1, 2, 3]);
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            }));
        var loader = new HttpArtworkLoader(
            decoder,
            new HttpContentLoader(httpClient));

        var result = await loader.LoadAsync(
            new Uri("https://example.test/cover.jpg"),
            CancellationToken.None);

        Assert.AreEqual("010203", decoder.StreamData);
        Assert.AreEqual(3, decoder.SourceLength);
        Assert.AreSame(decoder.Artwork, result.Artwork);
    }

    [TestMethod]
    public async Task HttpArtworkLoader_RejectsNonHttpUri()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage()));
        var loader = new HttpArtworkLoader(
            new StubArtworkDecoder(),
            new HttpContentLoader(httpClient));

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            loader.LoadAsync(new Uri("file:///cover.jpg"), CancellationToken.None));
    }

    [TestMethod]
    public async Task HttpArtworkLoader_RejectsUnsuccessfulResponse()
    {
        var decoder = new StubArtworkDecoder();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound)));
        var loader = new HttpArtworkLoader(
            decoder,
            new HttpContentLoader(httpClient));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            loader.LoadAsync(
                new Uri("https://example.test/missing.jpg"),
                CancellationToken.None));

        Assert.IsNull(decoder.StreamData);
    }

    [TestMethod]
    public async Task HttpContentLoader_ReadsTextContent()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("future lyrics")
            }));
        var loader = new HttpContentLoader(httpClient);

        var text = await loader.GetStringAsync(
            new Uri("https://example.test/lyrics.txt"),
            CancellationToken.None);

        Assert.AreEqual("future lyrics", text);
    }

    [TestMethod]
    public async Task HttpContentLoader_RejectsNonHttpUri()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage()));
        var loader = new HttpContentLoader(httpClient);

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            loader.GetStringAsync(
                new Uri("ftp://example.test/lyrics.txt"),
                CancellationToken.None));
    }

    private sealed class StubArtworkDecoder : IArtworkDecoder
    {
        internal IArtworkImage? Artwork { get; private set; }

        internal string? StreamData { get; private set; }

        internal long? SourceLength { get; private set; }

        public Task<ArtworkDecodeResult> DecodeAsync(
            Stream source,
            long? sourceLength,
            CancellationToken cancellationToken)
        {
            using var reader = new BinaryReader(source);
            StreamData = Convert.ToHexString(reader.ReadBytes(3));
            SourceLength = sourceLength;
            Artwork = new StubArtworkImage();
            return Task.FromResult(new ArtworkDecodeResult(
                Artwork,
                StreamData));
        }
    }

    private sealed record StubArtworkImage : IArtworkImage;

    private sealed class StubHttpMessageHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
