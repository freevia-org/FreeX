using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 158 regression coverage for freew-footnote-layout F1: <see cref="SectionAwareDocumentPaginator"/>
/// -- the paginator <see cref="PrintLayout.BuildPaginator"/> routes to for any multi-section document
/// that needs distinct header/footer, page geometry, or line-numbering per section (or an even/odd-page
/// section start) -- painted only the body <see cref="FlowDocument"/>, header, and footer sub-visuals for
/// each page box. It never read <c>PageBox.FootnoteIds</c>/<c>EndnoteIds</c>, so the note-region <see
/// cref="System.Windows.Controls.StackPanel"/> that <see cref="PageBox"/>'s own constructor builds (the
/// separator rule + numbered note text) was simply never touched: the in-body reference-mark superscript
/// still printed, but the footnote/endnote's own text never appeared on any printed page, in Print
/// Preview, or in a PDF/XPS export.
/// </summary>
public sealed class SectionAwarePrintFootnoteRegressionTests
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 1. Reproduces the finding: a two-section document with differing page geometry (so
    //    PrintLayout.BuildPaginator routes to SectionAwareDocumentPaginator) carrying a footnote in
    //    section 1 must print the footnote's own text, not just the in-body reference mark.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void SectionAwarePrint_FootnoteInLandscapeSection_PrintsFootnoteText()
    {
        var doc = BuildTwoSectionDocumentWithFootnote();

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var paginator = PrintLayout.BuildPaginator(editor);

        // Confirms the document really is routed to the section-aware paginator this finding is about
        // -- a document that stayed on the ordinary FlowDocument path would not reproduce the defect.
        paginator.Should().BeOfType<SectionAwareDocumentPaginator>(
            "a two-section document with differing page geometry must route through the section-aware " +
            "paginator (PrintLayout.BuildPaginator's NeedsSectionAwareRendering branch)");

        paginator.ComputePageCount();
        var page = paginator.GetPage(0);
        var text = ExtractDrawnText((Visual)page.Visual);

        text.Should().Contain("PROBE-FOOTNOTE-TEXT-MARKER",
            "the footnote's own body text must be painted on the page that carries its reference mark, " +
            "not silently dropped -- the reference superscript alone is not enough for the reader to " +
            "recover the note");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 2. Sibling / no-regression: a two-section document with the SAME footnote but sharing one page
    //    geometry stays on the ordinary (non-section-aware) print path, which already worked -- the fix
    //    must not disturb that path or its own footnote rendering.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void OrdinaryPrint_SameGeometrySectionsWithFootnote_StillPrintsFootnoteText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Footnotes[1] = new Footnote(1, "PROBE-FOOTNOTE-TEXT-MARKER");

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Section1 body with a note"));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(paragraph);

        var sharedPage = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 72,
            MarginBottomPt = 72,
        };
        var marker = new Paragraph("[ section break marker ]")
        {
            SectionBreak = new FreeW.Core.Model.Section(sharedPage, SectionBreakKind.NextPage)
        };
        doc.Blocks.Add(marker);

        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 792;
        doc.Page.MarginLeftPt = 72;
        doc.Page.MarginRightPt = 72;
        doc.Page.MarginTopPt = 72;
        doc.Page.MarginBottomPt = 72;
        doc.Blocks.Add(new Paragraph("Section2 Final Paragraph"));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var paginator = PrintLayout.BuildPaginator(editor);

        paginator.Should().NotBeOfType<SectionAwareDocumentPaginator>(
            "two sections that share one page geometry, header/footer, and line-numbering must stay on " +
            "the ordinary FlowDocument print path");

        paginator.ComputePageCount();
        var page = paginator.GetPage(0);
        var text = ExtractDrawnText((Visual)page.Visual);

        text.Should().Contain("PROBE-FOOTNOTE-TEXT-MARKER",
            "the ordinary (non-section-aware) print path already rendered footnote text correctly and " +
            "must keep doing so after this fix");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Section 1: landscape Letter (so its geometry differs from the final portrait section, forcing
    /// <c>PrintLayout.BuildPaginator</c> onto <see cref="SectionAwareDocumentPaginator"/>), carrying a
    /// paragraph with a footnote reference. Section 2 (final): plain portrait Letter.
    /// </summary>
    private static TextDocument BuildTwoSectionDocumentWithFootnote()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Footnotes[1] = new Footnote(1, "PROBE-FOOTNOTE-TEXT-MARKER");

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Section1 body with a note"));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(paragraph);

        var landscapePage = new PageSettings
        {
            WidthPt = 792,
            HeightPt = 612,
            Landscape = true,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 72,
            MarginBottomPt = 72,
        };
        var marker = new Paragraph("[ section break marker ]")
        {
            SectionBreak = new FreeW.Core.Model.Section(landscapePage, SectionBreakKind.NextPage)
        };
        doc.Blocks.Add(marker);

        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 792;
        doc.Page.Landscape = false;
        doc.Page.MarginLeftPt = 72;
        doc.Page.MarginRightPt = 72;
        doc.Page.MarginTopPt = 72;
        doc.Page.MarginBottomPt = 72;
        doc.Blocks.Add(new Paragraph("Section2 Final Paragraph"));

        return doc;
    }

    /// <summary>
    /// Walks a visual tree and its <see cref="Drawing"/> content, concatenating every
    /// <see cref="GlyphRunDrawing"/>'s original characters -- the same characters
    /// <see cref="FormattedText"/>/<c>DrawingContext.DrawText</c> stamped onto the glyph run, so this
    /// recovers the literal printed text without needing an on-screen render. Text painted through a
    /// <see cref="VisualBrush"/> (the body/header/footer sub-visuals) is intentionally NOT reachable
    /// this way -- only content drawn directly via <c>DrawText</c> on a visual that is itself a child
    /// in the returned page's visual tree shows up, which is exactly how the note region this finding
    /// is about must be painted for print/preview/PDF/XPS to show it at all.
    /// </summary>
    private static string ExtractDrawnText(Visual visual)
    {
        var sb = new StringBuilder();
        AppendDrawingText(VisualTreeHelper.GetDrawing(visual), sb);
        var childCount = VisualTreeHelper.GetChildrenCount(visual);
        for (var i = 0; i < childCount; i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                sb.Append(ExtractDrawnText(child));
        return sb.ToString();
    }

    private static void AppendDrawingText(Drawing? drawing, StringBuilder sb)
    {
        switch (drawing)
        {
            case System.Windows.Media.DrawingGroup group:
                foreach (var child in group.Children)
                    AppendDrawingText(child, sb);
                break;
            case GlyphRunDrawing { GlyphRun.Characters: { } characters }:
                sb.Append(characters.ToArray());
                break;
        }
    }
}
