using Free.Shared.Shell;

namespace FreeX.App.Services;

/// <summary>FreeX Legal Notices content contract shared by both desktop renderers.</summary>
public static class FreeXLegalNoticesPresentation
{
    public static LegalNoticesDialogPresentation Create(
        IReadOnlyList<LegalNoticeDocument> notices,
        Func<string, string> resolveText)
    {
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(resolveText);

        return new LegalNoticesDialogPresentation(
            windowTitle: resolveText("LegalNotices_LegalNotices"),
            notices: notices,
            summaryText: resolveText("LegalNotices_TheseNoticesArePackagedWithThisFreeXExecutableForOfflineReview"),
            closeButtonContent: resolveText("LegalNotices_CloseButton"),
            helpText: resolveText("LegalNotices_ShowsTheLegalPrivacyAndThirdPartyNoticesPackagedWithThisFreeXExecutable"),
            summaryAutomationName: resolveText("LegalNotices_LegalNoticesSummary"),
            sectionsAutomationName: resolveText("LegalNotices_LegalNoticeSections"),
            sectionLinkHelpText: resolveText("LegalNotices_ChooseALegalNoticeSectionToReadAndCopy"),
            readOnlyBodyHelpText: resolveText("LegalNotices_ReadOnlyLegalNoticeTextUseCtrlCToCopySelectedText"));
    }
}
