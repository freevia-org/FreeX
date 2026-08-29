using System.Linq;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// freew-change-bars F4: the Simple Markup change bar (<c>ChangeBarAdorner</c> in
/// FreeW.App.Host/Editing/DocumentView.cs) is the only visual cue that a paragraph carries a tracked
/// change while Review &gt; Display for Review is Simple Markup, but it used to be added only to the
/// live on-screen editor's own <see cref="AdornerLayer"/> (<c>DocumentView.SyncChangeBarAdorner</c>).
/// <see cref="PrintLayout.BuildPaginator"/> is the single hub behind Print, Print Preview, and PDF/XPS
/// export (see <see cref="PrintPreviewWindow"/>, <c>MainWindow.Print</c>, <c>PdfExport</c>,
/// <c>XpsExport</c>), and its <see cref="HeaderFooterPaginator.GetPage"/> composited header, footer,
/// watermark, border, line numbers, notes, and balloons onto every page but never a change bar -- a
/// tracked-change document printed in Simple Markup came out looking identical to an unmodified one.
/// These tests exercise the real production entry point (<see cref="PrintLayout.BuildPaginator"/> plus
/// the returned paginator's <see cref="System.Windows.Documents.DocumentPaginator.GetPage"/>) and walk
/// the actual rendered <see cref="System.Windows.Media.Visual"/> tree for a change-bar stroke, rather
/// than asserting against a helper that supplies the bar directly.
/// </summary>
public sealed class PrintPreviewChangeBarPipelineTests
{
    // Matches HeaderFooterPaginator.BuildChangeBars / ChangeBarAdorner.CreateBarPen exactly.
    private static readonly Color ChangeBarColor = Color.FromRgb(0x60, 0x60, 0xC0);
    private const double ChangeBarWidth = 3.0;

    [StaFact]
    public void BuildPaginator_SimpleMarkupWithTrackedInsertion_DrawsChangeBarOnPrintedPage()
    {
        var view = MakeEditorWithInsertedRun();
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.True(
            ContainsChangeBarStroke(page.Visual),
            "Print/Print Preview/PDF/XPS must draw the Simple Markup change bar for a paragraph carrying a tracked insertion.");
    }

    [StaFact]
    public void BuildPaginator_SimpleMarkupWithNoRevisions_DrawsNoChangeBar()
    {
        // Sibling/no-regression: same Simple Markup mode, but nothing in the document is a tracked
        // change -- today's overwhelmingly common document. No bar should appear.
        var view = MakeEditorWithPlainText();
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.False(
            ContainsChangeBarStroke(page.Visual),
            "A document with no tracked changes must not grow a change bar just because Simple Markup is active.");
    }

    [StaFact]
    public void BuildPaginator_AllMarkupWithTrackedInsertion_DrawsNoChangeBar()
    {
        // Sibling/no-regression: All Markup mode shows the tracked change inline (colour/strikethrough)
        // instead of a margin bar -- ShouldShowSimpleMarkupChangeBar is false there, on screen and here.
        var view = MakeEditorWithInsertedRun();
        view.ApplyDisplayForReview(ReviewDisplayMode.AllMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.False(
            ContainsChangeBarStroke(page.Visual),
            "All Markup already shows the change inline; it must not also draw the Simple Markup margin bar.");
    }

    [StaFact]
    public void BuildPaginator_SimpleMarkupWithFormatRevisionOnly_DrawsChangeBar()
    {
        // A tracked *formatting* change (w:rPrChange) carries no Revision insert/delete mark, only
        // FormatRevision -- ChangeBarAdorner.InlineHasRevision treats that as a change too.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("reformatted")
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "A", null)
        });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);
        view.LoadModel(doc);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.True(
            ContainsChangeBarStroke(page.Visual),
            "A tracked formatting-only change must also draw the Simple Markup change bar.");
    }

    [StaFact]
    public void BuildPaginator_NoMarkupWithTrackedInsertion_DrawsNoChangeBar()
    {
        // Sibling/no-regression: No Markup mode hides deleted text entirely and shows insertions as
        // plain accepted text -- ShouldShowSimpleMarkupChangeBar is false there too.
        var view = MakeEditorWithInsertedRun();
        view.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.False(
            ContainsChangeBarStroke(page.Visual),
            "No Markup must not draw the Simple Markup margin bar either.");
    }

    // ── freew-print-layout F1: table-height estimate for the WITHIN-page change-bar offset ──────────

    [StaFact]
    public void BuildPaginator_TrackedChangeAfterWrappingTable_ChangeBarAlignsWithRealRenderedLine()
    {
        // ResolveChangeBarBands (this file) used to size a Table block purely from its row count
        // (Rows.Count * 1.5 line-heights), blind to how many lines the cell's own text actually wraps
        // to. A single-cell table whose cell needs far more than 1.5 lines therefore made every block
        // after it on the same page get its band computed at a currentY far short of where it truly
        // renders -- reproduced here the same way the finding's own probe did: render the real page and
        // compare the drawn change-bar stroke's Y against the real rendered line's Y.
        var view = MakeEditorWithWrappingTableThenTrackedParagraph();
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var (markerY, barY) = MeasureMarkerAndChangeBarY(paginator);

        var lineHeightDip = view.Document.FontSize * (4.0 / 3.0);
        Assert.True(
            Math.Abs(barY - markerY) <= lineHeightDip * 1.5,
            $"change-bar stroke Y ({barY:F1}) must track the real rendered MARKERTEXT line " +
            $"(Y={markerY:F1}), not a flat row-count guess for the table above it.");
    }

    [StaFact]
    public void BuildPaginator_TrackedChangeAfterSingleLineTable_ChangeBarStillAlignsWithRealRenderedLine()
    {
        // Sibling/no-regression: a table whose cell content fits on one short line -- the overwhelming
        // common case, and the one the old estimate happened to get roughly right -- must still line up
        // after the fix.
        var view = MakeEditorWithSingleLineTableThenTrackedParagraph();
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);

        var paginator = PrintLayout.BuildPaginator(view);
        var (markerY, barY) = MeasureMarkerAndChangeBarY(paginator);

        var lineHeightDip = view.Document.FontSize * (4.0 / 3.0);
        Assert.True(
            Math.Abs(barY - markerY) <= lineHeightDip * 1.5,
            $"change-bar stroke Y ({barY:F1}) must track the real rendered MARKERTEXT line " +
            $"(Y={markerY:F1}) for a normal, single-line-cell table too.");
    }

    private static DocumentView MakeEditorWithWrappingTableThenTrackedParagraph()
    {
        // Mirrors the finding's own repro: one row/one cell table whose paragraph needs far more than
        // 1.5 line-heights to wrap, immediately followed by a tracked insertion.
        var cellText = string.Join(" ", Enumerable.Repeat("wordwordword", 220));
        return MakeEditorWithTableThenTrackedParagraph(cellText);
    }

    private static DocumentView MakeEditorWithSingleLineTableThenTrackedParagraph() =>
        MakeEditorWithTableThenTrackedParagraph("short");

    private static DocumentView MakeEditorWithTableThenTrackedParagraph(string cellText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        // A tall page keeps everything on one physical page -- this finding is about the WITHIN-page
        // offset, not about which page a block lands on (that part, GetPageNumber, was already correct).
        doc.Page.HeightPt = 5000;

        var table = new Table();
        var row = new FreeW.Core.Model.TableRow();
        row.Cells.Add(new FreeW.Core.Model.TableCell(cellText));
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        var marker = new Paragraph();
        marker.Runs.Add(new Run("MARKERTEXT") { Revision = RevisionKind.Inserted, RevisionAuthor = "A" });
        doc.Blocks.Add(marker);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    /// <summary>
    /// Renders every physical page of <paramref name="paginator"/> and returns the Y (in that page's own
    /// visual coordinate space) of the real rendered "MARKERTEXT" glyph run and of the Simple Markup
    /// change-bar stroke, wherever each first appears.
    /// </summary>
    private static (double MarkerY, double BarY) MeasureMarkerAndChangeBarY(DocumentPaginator paginator)
    {
        paginator.ComputePageCount();
        var markerY = double.NaN;
        var barY = double.NaN;
        for (var i = 0; i < paginator.PageCount; i++)
        {
            var visual = (Visual)paginator.GetPage(i).Visual;
            if (double.IsNaN(markerY) && TryFindGlyphRunBaselineY(visual, visual, "MARKERTEXT", out var foundMarkerY))
                markerY = foundMarkerY;
            if (double.IsNaN(barY) && TryFindChangeBarLineMidpointY(visual, visual, out var foundBarY))
                barY = foundBarY;
        }

        Assert.False(double.IsNaN(markerY), "test setup must actually render the MARKERTEXT paragraph.");
        Assert.False(double.IsNaN(barY), "Simple Markup must draw a change-bar stroke for the tracked paragraph.");
        return (markerY, barY);
    }

    /// <summary>Finds a change-bar-coloured stroked line anywhere under <paramref name="current"/> and
    /// returns its vertical midpoint transformed into <paramref name="root"/>'s coordinate space.</summary>
    private static bool TryFindChangeBarLineMidpointY(Visual root, Visual current, out double y)
    {
        if (VisualTreeHelper.GetDrawing(current) is { } drawing
            && TryFindChangeBarLineLocalMidpointY(drawing, out var localY))
        {
            y = current.TransformToAncestor(root).Transform(new System.Windows.Point(0, localY)).Y;
            return true;
        }

        var count = VisualTreeHelper.GetChildrenCount(current);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(current, i) is Visual child
                && TryFindChangeBarLineMidpointY(root, child, out y))
                return true;
        }

        y = 0;
        return false;
    }

    private static bool TryFindChangeBarLineLocalMidpointY(Drawing drawing, out double y)
    {
        switch (drawing)
        {
            case System.Windows.Media.DrawingGroup group:
                foreach (var child in group.Children)
                    if (TryFindChangeBarLineLocalMidpointY(child, out y))
                        return true;
                break;
            case GeometryDrawing { Geometry: LineGeometry line } gd
                when gd.Pen is { Brush: SolidColorBrush { Color: var c } } pen
                    && c == ChangeBarColor && pen.Thickness == ChangeBarWidth:
                y = (line.StartPoint.Y + line.EndPoint.Y) / 2.0;
                return true;
        }

        y = 0;
        return false;
    }

    /// <summary>Finds the first glyph run whose original characters contain <paramref name="containingText"/>
    /// anywhere under <paramref name="current"/> and returns its baseline Y transformed into
    /// <paramref name="root"/>'s coordinate space -- the real rendered line position, independent of any
    /// estimate this file's production code makes.</summary>
    private static bool TryFindGlyphRunBaselineY(Visual root, Visual current, string containingText, out double y)
    {
        if (VisualTreeHelper.GetDrawing(current) is { } drawing
            && TryFindGlyphRunLocalBaselineY(drawing, containingText, out var localY))
        {
            y = current.TransformToAncestor(root).Transform(new System.Windows.Point(0, localY)).Y;
            return true;
        }

        var count = VisualTreeHelper.GetChildrenCount(current);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(current, i) is Visual child
                && TryFindGlyphRunBaselineY(root, child, containingText, out y))
                return true;
        }

        y = 0;
        return false;
    }

    private static bool TryFindGlyphRunLocalBaselineY(Drawing drawing, string containingText, out double y)
    {
        switch (drawing)
        {
            case System.Windows.Media.DrawingGroup group:
                foreach (var child in group.Children)
                    if (TryFindGlyphRunLocalBaselineY(child, containingText, out y))
                        return true;
                break;
            case GlyphRunDrawing { GlyphRun.Characters: { } characters } grd
                when new string(characters.ToArray()).Contains(containingText, StringComparison.Ordinal):
                y = grd.GlyphRun.BaselineOrigin.Y;
                return true;
        }

        y = 0;
        return false;
    }

    private static DocumentView MakeEditorWithInsertedRun()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("base "));
        para.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = "A" });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static DocumentView MakeEditorWithPlainText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("nothing has changed here"));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    /// <summary>Walks a rendered page's visual tree for a stroked line matching the change-bar pen.</summary>
    private static bool ContainsChangeBarStroke(System.Windows.Media.Visual? visual)
    {
        if (visual is null)
            return false;
        if (visual is DrawingVisual dv && VisualTreeHelper.GetDrawing(dv) is { } drawing && ContainsChangeBarStroke(drawing))
            return true;

        var count = VisualTreeHelper.GetChildrenCount(visual);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(visual, i) is System.Windows.Media.Visual child
                && ContainsChangeBarStroke(child))
                return true;
        }
        return false;
    }

    private static bool ContainsChangeBarStroke(Drawing drawing)
    {
        switch (drawing)
        {
            case System.Windows.Media.DrawingGroup group:
                return group.Children.Any(ContainsChangeBarStroke);
            case GeometryDrawing gd:
                return gd.Pen is { Brush: SolidColorBrush { Color: var c } } pen
                    && c == ChangeBarColor
                    && pen.Thickness == ChangeBarWidth;
            default:
                return false;
        }
    }
}
