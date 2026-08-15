using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class SharedCatalogTranslationTests
{
    [Fact]
    public void SharedSatelliteTranslations_MatchTheApprovedSharedCatalogManifest()
    {
        var manifestPath = TestWorkspaceFileLocator.Find(
            "tests", "FreeX.App.Localization.Tests", "SharedCatalogTranslationHashes.json");
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(manifestPath));
        expected.Should().NotBeNull();
        expected!.Should().HaveCount(43);

        var resourceDirectory = TestWorkspaceFileLocator.FindContainingDirectory(
            "shared", "Free.Shared.Localization", "Resources", "Strings.resx");
        foreach (var (culture, expectedHash) in expected)
        {
            var values = ResxResourceTestSupport.ReadResxValues(
                resourceDirectory,
                $"Strings.{culture}.resx");
            values.Should().HaveCount(culture == "fr-FR" ? 40 : 20, because: culture);

            var canonical = string.Join(
                "\n",
                values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + pair.Value)) + "\n";
            using var sha = SHA256.Create();
            var actualHash = Convert.ToHexString(
                    sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();

            actualHash.Should().Be(expectedHash, because: culture);
        }
    }
}
