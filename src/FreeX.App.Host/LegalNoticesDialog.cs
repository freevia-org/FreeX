using Free.Shared.Shell.Wpf;
using Free.Shared.Shell;
using FreeX.App.Services;

namespace FreeX.App.Host;

/// <summary>
/// FreeX Legal Notices dialog. Thin wrapper over <see cref="SharedLegalNoticesDialog"/> that
/// composes the FreeX presentation with the WPF renderer.
/// </summary>
public sealed partial class LegalNoticesDialog : SharedLegalNoticesDialog
{
    public LegalNoticesDialog()
        : this(LegalNoticeProvider.GetDocuments())
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<LegalNoticeDocument> documents)
        : base(FreeXLegalNoticesPresentation.Create(documents, UiText.Get))
    {
    }
}
