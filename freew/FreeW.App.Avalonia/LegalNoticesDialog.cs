using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation;

namespace FreeW.App.Avalonia;

internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog
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
