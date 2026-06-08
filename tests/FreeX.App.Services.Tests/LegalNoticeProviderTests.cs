using FreeX.App.Services;
using FluentAssertions;
using System.Text;

namespace FreeX.App.Services.Tests;

public sealed class LegalNoticeProviderTests
{
    [Fact]
    public void GetDocuments_EmbedsFullOfflineLegalNoticeSet()
    {
        var documents = LegalNoticeProvider.GetDocuments();

        documents
            .Select(document => document.Title)
            .Should()
            .Equal(
                "Project License",
                "Legal Notices",
                "Privacy Notice",
                "Third-Party Notices",
                "Third-Party License Texts");
        documents.Should().OnlyContain(document => !string.IsNullOrWhiteSpace(document.Text));
        documents.Should().Contain(document =>
            document.Title == "Legal Notices" &&
            document.Text.Contains("FreeX", StringComparison.Ordinal));
        documents.Should().Contain(document =>
            document.Title == "Privacy Notice" &&
            document.Text.Contains("Privacy", StringComparison.OrdinalIgnoreCase));
        documents.Should().Contain(document =>
            document.Title == "Third-Party Notices" &&
            document.ResourceName == "FreeX.Legal.ThirdPartyNotices.md");
    }

    [Fact]
    public void ExpectedEmbeddedResources_MatchAssemblyLegalManifestNames()
    {
        var assembly = typeof(LegalNoticeProvider).Assembly;
        var expectedResourceNames = LegalNoticeProvider.ExpectedEmbeddedResources
            .Select(resource => resource.ResourceName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var manifestLegalResourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("FreeX.Legal.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        manifestLegalResourceNames.Should().Equal(expectedResourceNames);
    }

    [Fact]
    public void ExpectedEmbeddedResources_LoadAsNonEmptyUtf8Text()
    {
        var assembly = typeof(LegalNoticeProvider).Assembly;
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        foreach (var resource in LegalNoticeProvider.ExpectedEmbeddedResources)
        {
            using var stream = assembly.GetManifestResourceStream(resource.ResourceName);

            stream.Should().NotBeNull($"'{resource.ResourceName}' must be embedded for offline Help > Legal Notices");
            using var memory = new MemoryStream();
            stream!.CopyTo(memory);
            memory.Length.Should().BeGreaterThan(0, $"'{resource.ResourceName}' must not be empty");

            var text = strictUtf8.GetString(memory.ToArray());
            text.Should().NotBeNullOrWhiteSpace($"'{resource.ResourceName}' should decode to readable UTF-8 text");
            text.Should().NotContain("\uFFFD", $"'{resource.ResourceName}' should not need replacement characters");
        }
    }
}
