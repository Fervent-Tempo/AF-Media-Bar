using AFMediaBar.Abstractions;
using AFMediaBar.Services;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class UpdateManifestParserTests
{
    private static readonly Uri ManifestUri = new("https://example.test/latest.json");

    [TestMethod]
    public void TryParse_AcceptsHttpsLinksAndNormalizesChecksum()
    {
        const string checksum = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var json = $$"""
            {
              "schemaVersion": 1,
              "version": "2.3.4.5",
              "minimumSupportedVersion": "1.2",
              "title": "  Release 2.3.4  ",
              "releaseNotesUrl": "https://example.test/releases/2.3.4",
              "downloads": {
                "github": "https://example.test/AFMediaBar.exe",
                "insecure": "http://example.test/AFMediaBar.exe"
              },
              "sha256": {
                "github": "{{checksum}}",
                "invalid": "not-a-checksum"
              },
              "changelog": [" First ", "", "Second"]
            }
            """;

        var parsed = UpdateManifestParser.TryParse(
            new TestLocalizer(),
            json,
            ManifestUri,
            out var update,
            out _);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(update);
        Assert.AreEqual(new Version(2, 3, 4), update.Version);
        Assert.AreEqual("1.2.0", update.MinimumSupportedVersion);
        Assert.AreEqual("Release 2.3.4", update.Title);
        Assert.HasCount(1, update.DownloadUris);
        Assert.IsTrue(update.DownloadUris.ContainsKey("github"));
        Assert.AreEqual(checksum.ToLowerInvariant(), update.Checksums["github"]);
        Assert.HasCount(2, update.Changelog);
        Assert.AreEqual("First", update.Changelog[0]);
    }

    [TestMethod]
    public void TryParse_RejectsInvalidVersionWithLocalizedError()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "version": "not-a-version",
              "releaseNotesUrl": "https://example.test/releases/current"
            }
            """;

        var parsed = UpdateManifestParser.TryParse(
            new TestLocalizer(),
            json,
            ManifestUri,
            out var update,
            out var error);

        Assert.IsFalse(parsed);
        Assert.IsNull(update);
        Assert.AreEqual("localized:Msg.UpdateManifestVersionInvalid", error);
    }

    [TestMethod]
    public void TryParse_RejectsManifestWithoutHttpsLinks()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "version": "2.0.0",
              "releaseNotesUrl": "http://example.test/releases/2.0.0",
              "downloads": {
                "insecure": "http://example.test/AFMediaBar.exe"
              }
            }
            """;

        var parsed = UpdateManifestParser.TryParse(
            new TestLocalizer(),
            json,
            ManifestUri,
            out var update,
            out var error);

        Assert.IsFalse(parsed);
        Assert.IsNull(update);
        Assert.AreEqual("localized:Msg.UpdateManifestLinksMissing", error);
    }

    private sealed class TestLocalizer : IStringLocalizer
    {
        public string Get(string key, params object[] args) => $"localized:{key}";
    }
}
