using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

/// <summary>FreeX localized adapter for the shared Avalonia Legal Notices surface.</summary>
internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog
{
    public LegalNoticesDialog()
        : base(
            windowTitle: UiText.Get("LegalNotices_LegalNotices"),
            notices: LegalNoticeProvider.GetDocuments()
                .Select(document => (document.Title, document.Text))
                .ToArray(),
            introText: UiText.Get("LegalNotices_TheseNoticesArePackagedWithThisFreeXExecutableForOfflineReview"),
            closeButtonContent: UiText.Get("LegalNotices_CloseButton"),
            helpText: UiText.Get("LegalNotices_ShowsTheLegalPrivacyAndThirdPartyNoticesPackagedWithThisFreeXExecutable"),
            readOnlyTextHelpText: UiText.Get("LegalNotices_ReadOnlyLegalNoticeTextUseCtrlCToCopySelectedText"),
            sectionHelpText: UiText.Get("LegalNotices_ChooseALegalNoticeSectionToReadAndCopy"),
            acceptsTab: false,
            enableKeyboardLifecycle: true)
    {
    }
}
