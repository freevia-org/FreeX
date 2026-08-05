using Free.Shared.Shell.Wpf;
using Free.Shared.Shell;
using FreeX.App.Services;

namespace FreeX.App.Host;

/// <summary>
/// FreeX Legal Notices dialog. Thin wrapper over <see cref="SharedLegalNoticesDialog"/> that
/// supplies FreeX-specific strings. All structural and interaction logic lives in the shared
/// base so it can be reused by FreeW (and future apps) without duplication.
/// </summary>
public sealed partial class LegalNoticesDialog : SharedLegalNoticesDialog
{
    public LegalNoticesDialog()
        : this(LegalNoticeProvider.GetDocuments())
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<LegalNoticeDocument> documents)
        : base(
            windowTitle: UiText.Get("LegalNotices_LegalNotices"),
            notices: documents.Select(d => (d.Title, d.Text)).ToList(),
            introText: UiText.Get("LegalNotices_TheseNoticesArePackagedWithThisFreeXExecutableForOfflineReview"),
            closeButtonContent: UiText.Get("LegalNotices_CloseButton"),
            helpText: UiText.Get("LegalNotices_ShowsTheLegalPrivacyAndThirdPartyNoticesPackagedWithThisFreeXExecutable"))
    {
    }
}
