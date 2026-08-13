using Free.Shared.Shell.Wpf;
using Free.Shared.Shell;
using FreeW.App.Presentation;

namespace FreeW.App.Host;

/// <summary>
/// FreeW Legal Notices dialog. Thin wrapper over <see cref="SharedLegalNoticesDialog"/> that
/// composes the FreeW presentation with the WPF renderer.
/// </summary>
public sealed partial class LegalNoticesDialog : SharedLegalNoticesDialog
{
    public LegalNoticesDialog()
        : this(FreeWLegalNoticeProvider.GetDocuments(typeof(LegalNoticesDialog).Assembly))
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<LegalNoticeDocument> notices)
        : base(FreeWLegalNoticesPresentation.Create(notices))
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<(string Title, string Text)> notices)
        : base(FreeWLegalNoticesPresentation.Create(notices))
    {
    }
}
