using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>FreeP's Avalonia renderer for the shared offline Legal Notices presentation.</summary>
internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog
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
