using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;
using TableRow = FreeW.Core.Model.TableRow;
using TableCell = FreeW.Core.Model.TableCell;

namespace FreeW.App.Host.Tests;

/// <summary>
/// freew-print-layout meta F2 (round 171). r170 shared the cell-margin/vertical-padding/row-floor rules
/// between the print preview's change-bar table estimator (<c>PrintLayout.EstimateChangeBarTableHeightDip</c>,
/// PrintPreviewWindow.cs) and the real layout rule the live document view uses
/// (<see cref="DocumentViewLayoutPlanner"/>), but the estimator still never read
/// <see cref="TableRow.HeightPt"/>/<see cref="TableRow.HeightRule"/>, so an authored
/// <see cref="TableRowHeightRule.Exact"/> row height taller than its content had no effect on the
/// estimate at all -- the change bar for every paragraph after that table stayed misplaced.
/// <para>
/// This pins that the estimator's per-row height, for a table with an authored Exact row height, agrees
/// with <see cref="DocumentViewLayoutPlanner.ResolveAuthoredTableRowHeightDip"/> -- the single method
/// both the estimator and the planner's own pagination pass must funnel the authored-height decision
/// through -- rather than against a hard-coded DIP number.
/// </para>
/// </summary>
public sealed class R171_ChangeBarTableAuthoredRowHeightTests
{
    [StaFact]
    public void ChangeBarTableEstimate_HonorsAuthoredExactRowHeight_MatchingTheSharedPlannerRule()
    {
        const double authoredHeightPt = 300; // far taller than a one-word "short" cell needs
        var (view, row) = MakeEditorWithExactHeightTableThenTrackedParagraph(authoredHeightPt);
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);

        var estimatedTableHeightDip = EstimateFirstTableHeightViaChangeBarBands(view);

        // The Exact rule ignores content height entirely once an authored height is set (Word clips
        // rather than grows), so calling the shared planner method with a deliberately different content
        // height (0, versus whatever real per-cell measurement the estimator used) must still land on
        // the exact same number -- proving the estimator defers the authored-height decision to the one
        // shared place instead of re-deriving it.
        var plannerRowHeightDip = DocumentViewLayoutPlanner.ResolveAuthoredTableRowHeightDip(row, 0);

        Assert.True(
            plannerRowHeightDip > 100,
            $"the authored {authoredHeightPt}pt Exact height must dominate the tiny 'short' cell content " +
            $"(planner says {plannerRowHeightDip:F1} DIP).");
        Assert.True(
            Math.Abs(estimatedTableHeightDip - plannerRowHeightDip) < 0.5,
            $"change-bar table estimate ({estimatedTableHeightDip:F1} DIP) must match the shared " +
            $"authored-height rule ({plannerRowHeightDip:F1} DIP) for a row with an Exact height taller " +
            "than its content.");
    }

    [StaFact]
    public void ChangeBarTableEstimate_WithNoAuthoredHeight_StillEstimatesFromContent()
    {
        // Sibling/no-regression: a row with no authored height (the overwhelmingly common case) must
        // keep estimating from its own content, unaffected by the new authored-height plumbing.
        var (view, row) = MakeEditorWithExactHeightTableThenTrackedParagraph(authoredHeightPt: null);
        view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);

        Assert.Null(row.HeightPt);
        var estimatedTableHeightDip = EstimateFirstTableHeightViaChangeBarBands(view);
        var plannerFloorDip = DocumentViewLayoutPlanner.ResolveAuthoredTableRowHeightDip(row, 0);

        // With no authored height, the shared rule floors at DefaultTableRowHeightDip (24 DIP); the
        // estimate for a real one-line "short" cell must land at or above that floor and stay small
        // (nowhere near the 300pt-authored case above), i.e. it is still driven by content, not by a
        // phantom authored height.
        Assert.True(estimatedTableHeightDip >= plannerFloorDip - 0.5);
        Assert.True(estimatedTableHeightDip < 60,
            $"a single short line with no authored height must not balloon to {estimatedTableHeightDip:F1} DIP.");
    }

    private static double EstimateFirstTableHeightViaChangeBarBands(DocumentView view)
    {
        var flow = PrintLayout.BuildPaginatedDocument(view);
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.ComputePageCount();

        var (contentWidthDip, _) = PageLayout.ContentAreaDip(view.Model.Page);
        var lineHeightDip = view.Document.FontSize * (4.0 / 3.0);

        var bands = PrintLayout.ResolveChangeBarBands(view, paginator, contentWidthDip, lineHeightDip);

        Assert.True(bands.TryGetValue(0, out var page0Bands), "the tracked marker paragraph must land on page 0.");
        var band = Assert.Single(page0Bands);
        // The band's Top is the running Y offset accumulated over every preceding block -- here, just
        // the one table -- so it IS the estimator's computed table height.
        return band.Top;
    }

    private static (DocumentView View, TableRow Row) MakeEditorWithExactHeightTableThenTrackedParagraph(
        double? authoredHeightPt)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        // A tall page keeps everything on one physical page -- this test is about the WITHIN-page
        // offset, not about which page a block lands on.
        doc.Page.HeightPt = 5000;

        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(new TableCell("short"));
        if (authoredHeightPt is { } heightPt)
        {
            row.HeightPt = heightPt;
            row.HeightRule = TableRowHeightRule.Exact;
        }
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        var marker = new Paragraph();
        marker.Runs.Add(new Run("MARKERTEXT") { Revision = RevisionKind.Inserted, RevisionAuthor = "A" });
        doc.Blocks.Add(marker);

        var view = new DocumentView();
        view.LoadModel(doc);
        return (view, row);
    }
}
