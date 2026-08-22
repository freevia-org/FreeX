using FluentAssertions;
using Free.Shared.Shell;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class LegalNoticesDialogPresentationTests
{
    [Fact]
    public void Shared_presentation_projects_heading_body_source_and_navigation_identity_once()
    {
        var presentation = new LegalNoticesDialogPresentation(
            "Legal Notices",
            [
                new LegalNoticeDocument("Third-Party Notices", "App.Legal.ThirdParty.md", "notice body"),
                new LegalNoticeDocument("---", "App.Legal.Fallback.md", "fallback body"),
            ],
            "Summary",
            "Close",
            "Dialog help",
            "Summary name",
            "Sections name",
            "Section link help",
            "Body help");

        presentation.Sections.Should().Equal(
            new LegalNoticeSectionPresentation(
                "Third-Party Notices",
                "notice body",
                "App.Legal.ThirdParty.md",
                "LegalNoticesThirdPartyNoticesTab",
                "LegalNoticesThirdPartyNoticesText"),
            new LegalNoticeSectionPresentation(
                "---",
                "fallback body",
                "App.Legal.Fallback.md",
                "LegalNoticesDocumentTab",
                "LegalNoticesDocumentText"));
        presentation.CloseButtonContent.Should().Be("Close");
        presentation.CloseIsDefault.Should().BeTrue();
        presentation.CloseIsCancel.Should().BeTrue();
        presentation.TextRenderingPolicy.Should().Be(LegalNoticesTextRenderingPolicy.Default);
        presentation.SectionLinkHelpText.Should().Be("Section link help");
        presentation.ReadOnlyBodyHelpText.Should().Be("Body help");
    }

    [Fact]
    public void FreeX_factory_owns_every_localized_legal_notices_semantic()
    {
        var resolvedKeys = new List<string>();
        string Resolve(string key)
        {
            resolvedKeys.Add(key);
            return $"localized:{key}";
        }

        var notice = new LegalNoticeDocument("Project License", "FreeX.Legal.ProjectLicense.txt", "license");
        var presentation = FreeXLegalNoticesPresentation.Create([notice], Resolve);

        presentation.WindowTitle.Should().Be("localized:LegalNotices_LegalNotices");
        presentation.SummaryText.Should().Be("localized:LegalNotices_TheseNoticesArePackagedWithThisFreeXExecutableForOfflineReview");
        presentation.CloseButtonContent.Should().Be("localized:LegalNotices_CloseButton");
        presentation.HelpText.Should().Be("localized:LegalNotices_ShowsTheLegalPrivacyAndThirdPartyNoticesPackagedWithThisFreeXExecutable");
        presentation.SummaryAutomationName.Should().Be("localized:LegalNotices_LegalNoticesSummary");
        presentation.SectionsAutomationName.Should().Be("localized:LegalNotices_LegalNoticeSections");
        presentation.SectionLinkHelpText.Should().Be("localized:LegalNotices_ChooseALegalNoticeSectionToReadAndCopy");
        presentation.ReadOnlyBodyHelpText.Should().Be("localized:LegalNotices_ReadOnlyLegalNoticeTextUseCtrlCToCopySelectedText");
        presentation.TextRenderingPolicy.Should().Be(LegalNoticesTextRenderingPolicy.Default);
        presentation.Sections.Should().ContainSingle().Which.SourceResourceName.Should().Be(notice.ResourceName);
        resolvedKeys.Should().OnlyHaveUniqueItems().And.HaveCount(8);
    }
}
