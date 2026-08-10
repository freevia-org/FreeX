using Free.Shared.Shell;
using FreeW.App.Presentation;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWLegalNoticesPresentationTests
{
    [Fact]
    public void Product_presentation_preserves_the_existing_FreeW_surface_text_and_semantics()
    {
        var presentation = FreeWLegalNoticesPresentation.Create(
        [
            new LegalNoticeDocument("Project License", "FreeW.Legal.ProjectLicense.txt", "license text"),
        ]);

        presentation.WindowTitle.Should().Be("Legal Notices");
        presentation.SummaryText.Should().Be("These notices are packaged with FreeW for offline review.");
        presentation.CloseButtonContent.Should().Be("Close");
        presentation.HelpText.Should().Be(
            "Shows the legal, privacy, and third-party notices packaged with this FreeW executable.");
        presentation.SummaryAutomationName.Should().Be("Legal Notices summary");
        presentation.SectionsAutomationName.Should().Be("Legal notice sections");
        presentation.SectionLinkHelpText.Should().Be("Choose a legal notice section to read and copy.");
        presentation.ReadOnlyBodyHelpText.Should().Be(
            "Read-only legal notice text. Use Ctrl+C to copy selected text.");
        presentation.Sections.Should().ContainSingle().Which.Should().Be(
            new LegalNoticeSectionPresentation(
                "Project License",
                "license text",
                "FreeW.Legal.ProjectLicense.txt",
                "LegalNoticesProjectLicenseTab",
                "LegalNoticesProjectLicenseText"));
    }

    [Fact]
    public void Test_document_adapter_uses_the_same_product_presentation()
    {
        var presentation = FreeWLegalNoticesPresentation.Create(
        [
            ("Privacy Notice", "privacy text"),
        ]);

        presentation.Sections.Should().ContainSingle().Which.Should().Be(
            new LegalNoticeSectionPresentation(
                "Privacy Notice",
                "privacy text",
                string.Empty,
                "LegalNoticesPrivacyNoticeTab",
                "LegalNoticesPrivacyNoticeText"));
    }
}
