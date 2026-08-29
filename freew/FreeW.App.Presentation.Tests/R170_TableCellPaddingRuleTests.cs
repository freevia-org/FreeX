using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r170. The print preview's change-bar band estimator (PrintPreviewWindow.cs) kept its own table
/// height estimate that wrapped cell text at the full column width and added no cell padding at
/// all, while the layout the page actually renders takes both from DocumentViewLayoutPlanner. The
/// gap ran about 1.3-1.6 DIP per row, so a long table walked every change bar after it off its
/// paragraph. The two rules are now public here and read by both readers; these pin the rules.
/// </summary>
public sealed class R170_TableCellPaddingRuleTests
{
    [Fact]
    public void ResolveTableCellContentWidthDip_SubtractsTheAuthoredHorizontalMargins()
    {
        var table = new Table();
        var cell = new TableCell { Margins = new TableCellMargins(TopPt: 0, LeftPt: 12, BottomPt: 0, RightPt: 18) };

        var contentWidth = DocumentViewLayoutPlanner.ResolveTableCellContentWidthDip(table, cell, 300);

        contentWidth.Should().BeApproximately(300 - PageLayout.PointsToDip(30), 0.001);
    }

    [Fact]
    public void ResolveTableCellContentWidthDip_FallsBackToTheTableDefaultMargins()
    {
        var table = new Table { DefaultCellMargins = new TableCellMargins(0, 20, 0, 20) };
        var cell = new TableCell();

        DocumentViewLayoutPlanner.ResolveTableCellContentWidthDip(table, cell, 300)
            .Should().BeApproximately(300 - PageLayout.PointsToDip(40), 0.001);
    }

    [Fact]
    public void ResolveTableCellContentWidthDip_NeverCollapsesBelowAUsableWidth()
    {
        var table = new Table();
        var cell = new TableCell { Margins = new TableCellMargins(0, 500, 0, 500) };

        DocumentViewLayoutPlanner.ResolveTableCellContentWidthDip(table, cell, 40)
            .Should().Be(12);
    }

    [Fact]
    public void AddTableCellVerticalPaddingDip_AddsTheDefaultGutterWhenNoMarginsAreAuthored()
    {
        var table = new Table();
        var cell = new TableCell();

        // Word's default cell margins are 0pt top/bottom, so the estimate must still reserve the
        // planner's own gutter -- adding nothing is what made the preview under-estimate.
        DocumentViewLayoutPlanner.AddTableCellVerticalPaddingDip(table, cell, 18)
            .Should().BeGreaterThan(18);
    }

    [Fact]
    public void AddTableCellVerticalPaddingDip_HonoursLargerAuthoredMargins()
    {
        var table = new Table();
        var cell = new TableCell { Margins = new TableCellMargins(TopPt: 30, LeftPt: 5.4, BottomPt: 30, RightPt: 5.4) };

        DocumentViewLayoutPlanner.AddTableCellVerticalPaddingDip(table, cell, 18)
            .Should().BeApproximately(18 + PageLayout.PointsToDip(60), 0.001);
    }

    [Fact]
    public void ApplyTableRowHeightFloorDip_FloorsAShortRow()
    {
        DocumentViewLayoutPlanner.ApplyTableRowHeightFloorDip(1).Should().BeGreaterThan(1);
        DocumentViewLayoutPlanner.ApplyTableRowHeightFloorDip(400).Should().Be(400);
    }
}
