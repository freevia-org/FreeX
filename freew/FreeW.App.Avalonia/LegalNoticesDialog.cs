using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
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
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Opened += (_, _) => ApplyLegalDocumentTextRendering();
    }

    internal LegalNoticesDialog(IReadOnlyList<(string Title, string Text)> notices)
        : base(FreeWLegalNoticesPresentation.Create(notices))
    {
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Opened += (_, _) => ApplyLegalDocumentTextRendering();
    }

    private void ApplyLegalDocumentTextRendering()
    {
        foreach (var textBox in this.GetVisualDescendants().OfType<TextBox>())
        {
            TextOptions.SetTextRenderingMode(textBox, TextRenderingMode.Antialias);
            foreach (var presenter in textBox.GetVisualDescendants().OfType<TextPresenter>())
                TextOptions.SetTextRenderingMode(presenter, TextRenderingMode.Antialias);
        }
    }
}
