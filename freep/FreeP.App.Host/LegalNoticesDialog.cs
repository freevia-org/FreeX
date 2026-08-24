using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>FreeP's WPF renderer for the shared offline Legal Notices presentation.</summary>
internal sealed class LegalNoticesDialog : SharedLegalNoticesDialog
{
    public LegalNoticesDialog()
        : this(FreePLegalNoticeProvider.GetDocuments())
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<LegalNoticeDocument> notices)
        : base(FreePLegalNoticesPresentation.Create(notices))
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<(string Title, string Text)> notices)
        : base(FreePLegalNoticesPresentation.Create(notices))
    {
    }
}
