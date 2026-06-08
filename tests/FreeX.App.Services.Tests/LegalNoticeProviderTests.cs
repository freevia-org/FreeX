using FreeX.App.Services;
using FluentAssertions;

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
}
