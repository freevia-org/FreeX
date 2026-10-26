using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r530: inserting rows above a cell-anchored chart shifts its PIXEL position
/// (ShiftChartPositionRowsUp does chart.Top += insertedHeight), and undo restores it from a
/// snapshot rather than by subtracting the same amount back. Nothing asserted that restore, so the
/// two lines in RestoreChartStructuralState that write Left/Top could have been deleted with the
/// whole suite still green -- charts would simply stay displaced after every undo.
///
/// <para>Snapshot-restore is the right design here and worth pinning for that reason: the forward
/// shift is conditional (it only moves charts at or below the inserted boundary, and skips
/// absolutely-anchored ones), so an inverse subtraction would have to reproduce that condition
/// against row heights that undo has already changed. The snapshot also captures the chart by
/// IDENTITY -- ChartPositionSnapshot holds the chart reference -- so the restore writes to the chart
/// it measured rather than to whatever now sits at that index.</para>
/// </summary>
public sealed class R530_ChartPositionSurvivesInsertUndoTests
{
    private static (Workbook Workbook, Sheet Sheet, ChartModel Chart, WorkbookCommandContext Context) SheetWithAnchoredChart()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultRowHeight = 20.0;

        // The anchor kind is load-bearing, not incidental: ShiftChartPosition* deliberately skips
        // Absolute-anchored charts, which is ChartModel's DEFAULT. A chart left at the default would
        // never move, and every assertion below would pass without exercising anything.
        var chart = new ChartModel { Left = 300, Top = 200, DrawingAnchorKind = ChartDrawingAnchorKind.OneCell };
        sheet.Charts.Add(chart);

        return (workbook, sheet, chart, new WorkbookCommandContext(workbook));
    }

    [Fact]
    public void Undoing_a_row_insert_puts_the_chart_back()
    {
        var (_, sheet, chart, context) = SheetWithAnchoredChart();
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 3);

        command.Apply(context).Success.Should().BeTrue();
        // Non-vacuity: if the insert did not move the chart, the restore below proves nothing.
        chart.Top.Should().BeGreaterThan(200, "inserting rows above a cell-anchored chart moves it down");

        command.Revert(context);

        chart.Top.Should().Be(200);
    }

    [Fact]
    public void Undoing_a_column_insert_puts_the_chart_back()
    {
        var (_, sheet, chart, context) = SheetWithAnchoredChart();
        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 3);

        command.Apply(context).Success.Should().BeTrue();
        chart.Left.Should().BeGreaterThan(300, "inserting columns left of a cell-anchored chart moves it right");

        command.Revert(context);

        chart.Left.Should().Be(300);
    }

    [Fact]
    public void An_absolutely_anchored_chart_never_moves_at_all()
    {
        // The other half of the contract: Excel's "don't move or size with cells" anchor pins the
        // chart to the sheet's pixel grid, so neither the shift nor the restore should touch it.
        var (_, sheet, _, context) = SheetWithAnchoredChart();
        var pinned = new ChartModel { Left = 300, Top = 200, DrawingAnchorKind = ChartDrawingAnchorKind.Absolute };
        sheet.Charts.Add(pinned);
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 3);

        command.Apply(context).Success.Should().BeTrue();
        pinned.Top.Should().Be(200);

        command.Revert(context);

        pinned.Top.Should().Be(200);
    }
}
