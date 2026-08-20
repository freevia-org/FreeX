using System.Text;
using FreeW.App.Host;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// freew-change-bars F2: the WPF PDF export used to embed a searchable/copyable text layer for
/// revision text the user explicitly hid via Review &gt; Display for Review &gt; No Markup/Original.
///
/// <para>
/// FreeW hides that text purely visually -- <see cref="DocumentView"/> keeps the real characters on the
/// live <c>Run</c> and only paints it invisible (transparent foreground + a near-zero font size) so the
/// model still round-trips when the user switches back to All Markup. <see cref="PdfExport"/> then
/// rasterises that same FlowDocument to a page image AND separately walks its rendered glyph runs (via
/// <c>Free.Shared.Pdf.Wpf.WpfVisualTextOverlayExtractor</c>) to build a searchable/selectable text
/// overlay -- which used to include every glyph run regardless of whether it was actually visible, so
/// the "hidden" text was invisible on the page yet fully recoverable via Ctrl+F / Select-All+Copy in any
/// PDF viewer.
/// </para>
/// </summary>
public sealed class R159_HiddenRevisionTextPdfOverlayLeakTests
{
    private static DocumentView BuildDocWithDeletedRevision()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Kept visible text. "));
        paragraph.Runs.Add(new Run("SecretDeletedMarkerText")
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Alice",
        });
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void RenderToBytes_NoMarkupHidesDeletedRevision_TextIsNotInSelectableOverlay()
    {
        var view = BuildDocWithDeletedRevision();
        view.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var bytes = PdfExport.RenderToBytes(paginator, "Sample");

        var pdfText = Encoding.Latin1.GetString(bytes);

        // The deleted run is invisible on the page in No Markup; the exported PDF's raw bytes (which,
        // per RenderToBytes_SampleDocument_CarriesSelectableTextLayer, embed the overlay text
        // uncompressed) must not carry it as searchable/selectable text either.
        Assert.DoesNotContain("SecretDeletedMarkerText", pdfText);
        // Sibling: ordinary visible text on the same page must still be present/searchable -- the fix
        // must not blank the overlay wholesale.
        Assert.Contains("Kept visible text", pdfText);
    }

    // ── Sibling/no-regression: a visible tracked change must still be searchable ──────────────────

    [StaFact]
    public void RenderToBytes_AllMarkupShowsDeletedRevision_TextIsStillInSelectableOverlay()
    {
        var view = BuildDocWithDeletedRevision();
        view.ApplyDisplayForReview(ReviewDisplayMode.AllMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var bytes = PdfExport.RenderToBytes(paginator, "Sample");

        var pdfText = Encoding.Latin1.GetString(bytes);

        // In All Markup the deleted run is shown (struck through, in the revision colour) -- it must
        // remain searchable/selectable, exactly like any other visible text. The fix targets only the
        // invisible (transparent + near-zero font size) rendering technique, not every revision run.
        Assert.Contains("SecretDeletedMarkerText", pdfText);
        Assert.Contains("Kept visible text", pdfText);
    }
}
