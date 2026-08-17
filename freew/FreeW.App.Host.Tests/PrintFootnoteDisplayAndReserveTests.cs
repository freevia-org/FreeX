using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round-140 regression coverage for two footnote defects in the print/print-preview/PDF/XPS path:
/// (a) <c>PrintPreviewWindow.BuildNotesAtFoot</c> drew a note's raw internal <see cref="Footnote.Id"/>
/// instead of its computed display sequence, so a document with a deleted (gapped) footnote id printed
/// the wrong number at the foot of the page; (b) the footnote body-reserve computed for print/print
/// preview/PDF (<c>PrintLayout.ApplyFootnoteBodyReserve</c>) and for the live-editing page-break gutter
/// (<c>PaginationEngine.ComputeSegmentPageAssignment</c>) reserved the combined height of every
/// footnote the whole document owns on every single page, instead of only the footnotes actually
/// referenced on that page.
/// </summary>
public sealed class PrintFootnoteDisplayAndReserveTests
{
    // ── (a) display sequence, not raw id ─────────────────────────────────────────────────────────

    [StaFact]
    public void PrintPage_DeletedFootnoteLeavesGap_ShowsRenumberedDisplaySequenceNotRawId()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();

        // Simulates deleting the middle footnote of an original 1/2/3: the surviving footnotes keep
        // their original (now gapped) ids 1 and 3, but Word (and every other renderer in this
        // codebase) displays the recomputed sequence 1 and 2.
        model.Footnotes[1] = new Footnote(1, "first note body");
        model.Footnotes[3] = new Footnote(3, "third note body");

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body text with two notes"));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(Run.FootnoteReference(3));
        model.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(model);
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();
        var page = paginator.GetPage(0);

        var text = ExtractDrawnText((Visual)page.Visual);

        text.Should().Contain("2. third note body",
            "the surviving footnote (raw id 3) is the SECOND note in display order and print/preview " +
            "must show the same computed sequence the on-screen body mark and page-box note region show");
        text.Should().NotContain("3. third note body",
            "the raw internal dictionary key must never leak into the printed/exported note label");
        text.Should().Contain("1. first note body");
    }

    [StaFact]
    public void PrintPage_ConsecutiveFootnoteIds_StillShowMatchingLabels()
    {
        // Sibling/neighbour case: when ids already match the display sequence (the common, no-deletion
        // case), the fix must not disturb anything -- labels equal the ids exactly as before.
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Footnotes[1] = new Footnote(1, "alpha note body");
        model.Footnotes[2] = new Footnote(2, "beta note body");

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body text with two notes"));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(Run.FootnoteReference(2));
        model.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(model);
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();
        var page = paginator.GetPage(0);

        var text = ExtractDrawnText((Visual)page.Visual);

        text.Should().Contain("1. alpha note body");
        text.Should().Contain("2. beta note body");
    }

    // ── (b) per-page footnote reserve, not whole-document sum ───────────────────────────────────

    [StaFact]
    public void PrintPaginator_UnreferencedFootnoteDictionaryEntries_DoNotShrinkPages()
    {
        var referencedOnlyPages = BuildPaginatorPageCount(phantomUnreferencedFootnoteCount: 0);
        var withPhantomsPages = BuildPaginatorPageCount(phantomUnreferencedFootnoteCount: 12);

        // Twelve extra footnote dictionary entries that no body run anywhere references (e.g. notes an
        // editing sequence left orphaned) must never add to the print/print-preview/PDF/XPS reserve --
        // only the one footnote actually referenced (and therefore actually shown) should. Before the
        // fix, ApplyFootnoteBodyReserve summed every entry in TextDocument.Footnotes regardless of
        // whether any page ever displays it, so adding these phantom entries alone shrank every page's
        // usable body area and changed the page count even though nothing visible changed.
        withPhantomsPages.Should().Be(referencedOnlyPages,
            "unreferenced footnote dictionary entries must not shrink a page that never shows them");
    }

    [StaFact]
    public void EditorPageBreakGutter_UnreferencedFootnoteDictionaryEntries_DoNotShrinkPages()
    {
        var referencedOnlyEditor = new DocumentView();
        referencedOnlyEditor.LoadModel(BuildDocWithReferencedAndPhantomFootnotes(phantomUnreferencedFootnoteCount: 0));
        referencedOnlyEditor.CommitToModel();
        var referencedOnlyAssignment = PaginationEngine.ComputeBlockPageAssignment(referencedOnlyEditor);
        var referencedOnlyPages = (referencedOnlyAssignment.Length == 0 ? 0 : referencedOnlyAssignment.Max()) + 1;

        var withPhantomsEditor = new DocumentView();
        withPhantomsEditor.LoadModel(BuildDocWithReferencedAndPhantomFootnotes(phantomUnreferencedFootnoteCount: 12));
        withPhantomsEditor.CommitToModel();
        var withPhantomsAssignment = PaginationEngine.ComputeBlockPageAssignment(withPhantomsEditor);
        var withPhantomsPages = (withPhantomsAssignment.Length == 0 ? 0 : withPhantomsAssignment.Max()) + 1;

        // Same defect, live-editing page-break gutter path (PaginationEngine.ComputeSegmentPageAssignment
        // mirrors PrintLayout's reserve so the gutter marks match what print/preview will produce).
        withPhantomsPages.Should().Be(referencedOnlyPages,
            "PaginationEngine's per-segment footnote reserve must only ever count the footnotes " +
            "actually referenced by the segment's own blocks, not every footnote the document owns");
    }

    /// <summary>
    /// Sibling/neighbour case for (b): a single referenced footnote must still shrink the page it
    /// lands on -- this is the pre-existing <c>FootnoteReserveClampTests</c>/
    /// <c>PagedEditMixedGeometryPaginationTests</c> invariant that the per-page fix must not regress
    /// (a page with a real, referenced footnote must still reserve space and overflow).
    /// </summary>
    [StaFact]
    public void EditorPageBreakGutter_SingleReferencedFootnote_StillShrinksItsOwnPage()
    {
        var baselineEditor = new DocumentView();
        baselineEditor.LoadModel(BuildDocWithReferencedAndPhantomFootnotes(phantomUnreferencedFootnoteCount: -1));
        baselineEditor.CommitToModel();
        var baselineAssignment = PaginationEngine.ComputeBlockPageAssignment(baselineEditor);
        baselineAssignment.Should().OnlyContain(page => page == 0,
            "with no footnote at all, the fourteen short body paragraphs fit on one page");

        var footnoteEditor = new DocumentView();
        footnoteEditor.LoadModel(BuildDocWithReferencedAndPhantomFootnotes(phantomUnreferencedFootnoteCount: 0));
        footnoteEditor.CommitToModel();
        var footnoteAssignment = PaginationEngine.ComputeBlockPageAssignment(footnoteEditor);
        footnoteAssignment.Should().Contain(page => page > 0,
            "a single referenced footnote must still reserve space and push overflow to the next page");
    }

    // ── shared fixtures/helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fourteen short body paragraphs (established elsewhere to comfortably fill, but not overflow,
    /// one page's content area with no reserve) ending in one footnote reference, plus
    /// <paramref name="phantomUnreferencedFootnoteCount"/> extra <see cref="Footnote"/> dictionary
    /// entries that no body run anywhere references (pass -1 to omit the referenced footnote itself,
    /// producing the true zero-footnote baseline).
    /// </summary>
    private static TextDocument BuildDocWithReferencedAndPhantomFootnotes(int phantomUnreferencedFootnoteCount)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 400;
        doc.Page.MarginLeftPt = 36;
        doc.Page.MarginRightPt = 36;
        doc.Page.MarginTopPt = 36;
        doc.Page.MarginBottomPt = 36;

        for (var i = 1; i <= 14; i++)
            doc.Blocks.Add(new Paragraph($"Body line {i:00}"));

        if (phantomUnreferencedFootnoteCount >= 0)
        {
            doc.Footnotes[1] = new Footnote(
                1, string.Join(" ", Enumerable.Repeat("footnote text word", 40)));
            var lastPara = (Paragraph)doc.Blocks[^1];
            lastPara.Runs.Add(Run.FootnoteReference(1));
        }

        // Extra footnote dictionary entries that are never referenced by any body run. Word round-trips
        // these (e.g. an editing sequence that left an orphaned note behind) but they must never
        // reserve print/preview/PDF/XPS or editor body space, since no page ever displays them.
        for (var extra = 0; extra < phantomUnreferencedFootnoteCount; extra++)
            doc.Footnotes[100 + extra] = new Footnote(100 + extra, "an unreferenced phantom footnote body");

        return doc;
    }

    private static int BuildPaginatorPageCount(int phantomUnreferencedFootnoteCount)
    {
        var view = new DocumentView();
        view.LoadModel(BuildDocWithReferencedAndPhantomFootnotes(phantomUnreferencedFootnoteCount));
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();
        return paginator.PageCount;
    }

    /// <summary>
    /// Walks a visual tree and its <see cref="Drawing"/> content, concatenating every
    /// <see cref="GlyphRunDrawing"/>'s original characters -- the same characters
    /// <see cref="System.Windows.Media.FormattedText"/>/<c>DrawingContext.DrawText</c> stamped onto the
    /// glyph run, so this recovers the literal printed text without needing an on-screen render.
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
