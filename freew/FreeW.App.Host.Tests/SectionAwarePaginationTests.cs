using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Page composition of multi-section documents (the <see cref="SectionAwareDocumentPaginator"/> path),
/// distilled from a real A5 book with a cover section, 18 sections and 138 footnotes that printed as
/// 392 pages against Word's 95 -- most of them a header over an empty body.
/// </summary>
public sealed class SectionAwarePaginationTests
{
    /// <summary>
    /// A section that references no footnote must not reserve footnote space. It used to fall through
    /// to the whole-document fallback and reserve every footnote in the document on each of its pages,
    /// leaving one line of body per page -- a ten-line cover page printed as ten pages.
    /// </summary>
    [StaFact]
    public void FootnoteFreeSection_KeepsItsFullBodyArea()
    {
        var doc = BuildCoverThenFootnotedSection(coverParagraphs: 10, footnotes: 40);
        var editor = new DocumentView();
        editor.LoadModel(doc);

        var assignment = PaginationEngine.ComputeBlockPageAssignment(editor);

        // Blocks 0..9 are the cover paragraphs, block 10 the section-break marker: all on page 1.
        Assert.All(assignment.Take(11), pageIndex => Assert.Equal(0, pageIndex));
        Assert.True(assignment[11] >= 1, "the footnoted section starts on a new page");
    }

    /// <summary>
    /// A paragraph that runs over a page boundary must continue on the next printed page. Page boxes
    /// hold whole blocks, so the section-aware paginator printed such a paragraph clipped on its
    /// first page and left the page it continued onto empty.
    /// </summary>
    [StaFact]
    public void ParagraphSpanningPages_ContinuesOnTheFollowingPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("cover")
        {
            SectionBreak = new FreeW.Core.Model.Section(LandscapePage(), SectionBreakKind.NextPage)
        });
        var words = string.Join(" ", Enumerable.Range(1, 1400).Select(i => $"word{i:0000}"));
        doc.Blocks.Add(new Paragraph(words));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var paginator = PrintLayout.BuildPaginator(editor);
        Assert.IsType<SectionAwareDocumentPaginator>(paginator);
        paginator.ComputePageCount();

        // Page 1 = cover section; the long paragraph needs at least pages 2 and 3.
        Assert.True(paginator.PageCount >= 3, $"expected the paragraph to span pages, got {paginator.PageCount}");
        var secondPage = ExtractDrawnText(paginator.GetPage(1).Visual);
        var thirdPage = ExtractDrawnText(paginator.GetPage(2).Visual);
        Assert.Contains("word0001", secondPage);
        Assert.DoesNotContain("word1400", secondPage);
        Assert.Contains("word", thirdPage);

        // Nothing is lost between the pages: the last word reaches the final page.
        var lastPage = ExtractDrawnText(paginator.GetPage(paginator.PageCount - 1).Visual);
        Assert.Contains("word1400", lastPage);
    }

    /// <summary>
    /// Word reserves, on each page, the space of the notes that page carries. One uniform reserve
    /// sized for the most heavily annotated page left a band of empty body on every other page of the
    /// section. Pages are now cut one at a time -- splitting a paragraph that straddles the cut -- so a
    /// page without notes keeps its whole body and no text is lost at the cut.
    /// </summary>
    [StaFact]
    public void FootnotedSection_ReservesOnlyEachPagesOwnNotes_AndCarriesSplitParagraphsOver()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("cover")
        {
            SectionBreak = new FreeW.Core.Model.Section(LandscapePage(), SectionBreakKind.NextPage)
        });
        var annotated = new Paragraph();
        annotated.Runs.Add(new Run("Heavily annotated opening"));
        for (var id = 1; id <= 6; id++)
        {
            doc.Footnotes[id] = new Footnote(
                id,
                $"Footnote {id}: " + string.Join(" ", Enumerable.Repeat("a fairly long note body", 14)));
            annotated.Runs.Add(Run.FootnoteReference(id));
        }
        doc.Blocks.Add(annotated);
        var words = string.Join(" ", Enumerable.Range(1, 1600).Select(i => $"word{i:0000}"));
        doc.Blocks.Add(new Paragraph(words));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var segments = new System.Collections.Generic.List<PaginationEngine.SegmentPagination>();
        PaginationEngine.ComputeBlockPageAssignment(editor, out var totalPages, segments);

        // Page 1 = cover; page 2 carries all six notes; every later page carries none.
        var footnoted = segments.Where(segment => segment.PageOffset >= 1).ToList();
        Assert.True(footnoted.Count >= 3, $"expected one segment per page, got {footnoted.Count}");
        Assert.True(footnoted[0].FootnoteReserveDip > 0);
        // Six long notes overflow one band: like Word, the rest continues in the next pages' bands
        // (each capped), and once the notes are exhausted the pages keep their whole body.
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(LandscapePage());
        var (_, top, _, bottom) = PageLayout.MarginsDip(LandscapePage());
        var bodyHeight = Math.Max(pageWidth, pageHeight) - top - bottom;
        Assert.All(footnoted, segment => Assert.True(
            segment.FootnoteReserveDip <= bodyHeight * FreeW.App.Presentation.DocumentView.DocumentNoteRegionPlanner.MaxFootnoteBandShareOfBody + 1));
        Assert.Equal(0, footnoted[^1].FootnoteReserveDip);
        Assert.Equal(totalPages, segments.Sum(segment => segment.PageCount));

        var paginator = PrintLayout.BuildPaginator(editor);
        paginator.ComputePageCount();
        var printed = new StringBuilder();
        for (var i = 0; i < paginator.PageCount; i++)
            printed.Append(ExtractDrawnText(paginator.GetPage(i).Visual)).Append('|');
        var text = printed.ToString();
        for (var i = 1; i <= 1600; i += 37)
            Assert.Contains($"word{i:0000}", text);
        Assert.Contains("word1600", text);
        Assert.Equal(1600, System.Text.RegularExpressions.Regex.Matches(text, @"word\d{4}").Count);
    }

    /// <summary>
    /// A "first page only" page border belongs to the first page of the section that defines it, and
    /// the section-aware paginator must draw it at all (it used to draw no page border).
    /// </summary>
    [StaFact]
    public void FirstPageBorder_IsDrawnOnTheSectionsFirstPageOnly()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var coverPage = LandscapePage();
        coverPage.PageBorder = new PageBorder("#112233", 3) { Display = PageBorderDisplay.FirstPage };
        doc.Blocks.Add(new Paragraph("cover")
        {
            SectionBreak = new FreeW.Core.Model.Section(coverPage, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("body"));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var paginator = PrintLayout.BuildPaginator(editor);
        Assert.IsType<SectionAwareDocumentPaginator>(paginator);
        paginator.ComputePageCount();

        Assert.True(HasStroke(paginator.GetPage(0).Visual, Color.FromRgb(0x11, 0x22, 0x33)));
        Assert.False(HasStroke(paginator.GetPage(1).Visual, Color.FromRgb(0x11, 0x22, 0x33)));
    }

    /// <summary>
    /// stress008: a single-section document's footnotes were drawn into the bottom margin while the body
    /// ran on underneath them, so every footnoted page held more body than Word's. Such a document now
    /// prints through the per-page reserve, with the notes inside the body area.
    /// </summary>
    [StaFact]
    public void SingleSectionFootnotes_PrintWithAPerPageReserve_AndNotesInsideTheBody()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var id = 1; id <= 3; id++)
        {
            doc.Footnotes[id] = new Footnote(id, $"Note{id} text");
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run(string.Join(" ", Enumerable.Repeat($"body{id}", 120))));
            paragraph.Runs.Add(Run.FootnoteReference(id));
            doc.Blocks.Add(paragraph);
        }
        var editor = new DocumentView();
        editor.LoadModel(doc);

        Assert.True(PrintLayout.UsesPerPageFootnoteReserve(editor, PrintLayout.ResolvePrintBalloonSources(editor)));
        var paginator = PrintLayout.BuildPaginator(editor);
        Assert.IsType<SectionAwareDocumentPaginator>(paginator);
        paginator.ComputePageCount();

        var page = paginator.GetPage(0);
        Assert.Contains("Note1", ExtractDrawnText(page.Visual));
        var (_, _, _, bottomMargin) = PageLayout.MarginsDip(doc.Page);
        var noteBounds = FindTextBounds(page.Visual, "Note1");
        Assert.NotNull(noteBounds);
        Assert.True(noteBounds!.Value.Bottom <= page.Size.Height - bottomMargin + 1,
            "the notes sit inside the body area, above the bottom margin");
    }

    /// <summary>
    /// A long note continues on the next page (Word) instead of reserving most of its reference page,
    /// and a manual page break in a footnoted document leaves no blank page.
    /// </summary>
    [StaFact]
    public void LongFootnote_ContinuesOnTheNextPage_AndPageBreakLeavesNoBlankPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var breakParagraph = new Paragraph();
        breakParagraph.Runs.Add(new Run("cover"));
        breakParagraph.Runs.Add(new Run(string.Empty) { IsPageBreak = true });
        doc.Blocks.Add(breakParagraph);
        var longNote = string.Join(" ", Enumerable.Range(1, 600).Select(i => $"n{i:000}"));
        doc.Footnotes[1] = new Footnote(1, longNote);
        var referencing = new Paragraph();
        referencing.Runs.Add(new Run("Referencing paragraph"));
        referencing.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(referencing);
        doc.Blocks.Add(new Paragraph(string.Join(" ", Enumerable.Range(1, 1500).Select(i => $"word{i:0000}"))));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var paginator = PrintLayout.BuildPaginator(editor);
        Assert.IsType<SectionAwareDocumentPaginator>(paginator);
        paginator.ComputePageCount();

        var pages = Enumerable.Range(0, paginator.PageCount)
            .Select(index => ExtractDrawnText(paginator.GetPage(index).Visual))
            .ToList();
        Assert.All(pages, text => Assert.False(string.IsNullOrWhiteSpace(text), "no blank page"));
        Assert.Contains("cover", pages[0]);
        Assert.Contains("Referencing", pages[1]);
        Assert.Contains("n001", pages[1]);
        Assert.DoesNotContain("n600", pages[1]);
        Assert.Contains("n600", string.Concat(pages.Skip(2)));
    }

    private static Rect? FindTextBounds(Visual root, string text)
    {
        Rect? found = null;
        void Walk(Visual visual)
        {
            if (VisualTreeHelper.GetDrawing(visual) is { } drawing && Contains(drawing))
            {
                var bounds = VisualTreeHelper.GetDescendantBounds(visual);
                if (!bounds.IsEmpty && !ReferenceEquals(visual, root))
                    bounds = visual.TransformToAncestor(root).TransformBounds(bounds);
                found = found is { } existing ? Rect.Union(existing, bounds) : bounds;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
                if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                    Walk(child);
        }
        bool Contains(Drawing drawing) => drawing switch
        {
            System.Windows.Media.DrawingGroup group => group.Children.Any(Contains),
            GlyphRunDrawing { GlyphRun.Characters: { } characters } => new string(characters.ToArray()).Contains(text, StringComparison.Ordinal),
            _ => false,
        };
        Walk(root);
        return found;
    }

    private static PageSettings LandscapePage() => new()
    {
        WidthPt = 792,
        HeightPt = 612,
        Landscape = true,
        MarginLeftPt = 72,
        MarginRightPt = 72,
        MarginTopPt = 72,
        MarginBottomPt = 72,
    };

    private static TextDocument BuildCoverThenFootnotedSection(int coverParagraphs, int footnotes)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var i = 0; i < coverParagraphs; i++)
            doc.Blocks.Add(new Paragraph($"cover line {i}"));
        doc.Blocks.Add(new Paragraph("end of cover")
        {
            SectionBreak = new FreeW.Core.Model.Section(LandscapePage(), SectionBreakKind.NextPage)
        });

        for (var id = 1; id <= footnotes; id++)
        {
            doc.Footnotes[id] = new Footnote(
                id,
                $"Footnote {id}: " + string.Join(" ", Enumerable.Repeat("a fairly long note body", 12)));
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run($"Body paragraph {id} with a reference"));
            paragraph.Runs.Add(Run.FootnoteReference(id));
            doc.Blocks.Add(paragraph);
        }

        return doc;
    }

    private static bool HasStroke(Visual visual, Color color)
    {
        if (ContainsStroke(VisualTreeHelper.GetDrawing(visual), color))
            return true;
        var count = VisualTreeHelper.GetChildrenCount(visual);
        for (var i = 0; i < count; i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child && HasStroke(child, color))
                return true;
        return false;
    }

    private static bool ContainsStroke(Drawing? drawing, Color color) => drawing switch
    {
        System.Windows.Media.DrawingGroup group => group.Children.Any(child => ContainsStroke(child, color)),
        GeometryDrawing { Pen.Brush: SolidColorBrush brush } => brush.Color == color,
        _ => false
    };

    private static string ExtractDrawnText(Visual visual)
    {
        var sb = new StringBuilder();
        Append(VisualTreeHelper.GetDrawing(visual), sb);
        var count = VisualTreeHelper.GetChildrenCount(visual);
        for (var i = 0; i < count; i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                sb.Append(ExtractDrawnText(child));
        return sb.ToString();
    }

    private static void Append(Drawing? drawing, StringBuilder sb)
    {
        switch (drawing)
        {
            case System.Windows.Media.DrawingGroup group:
                foreach (var child in group.Children)
                    Append(child, sb);
                break;
            case GlyphRunDrawing { GlyphRun.Characters: { } characters }:
                sb.Append(characters.ToArray());
                break;
        }
    }
}
