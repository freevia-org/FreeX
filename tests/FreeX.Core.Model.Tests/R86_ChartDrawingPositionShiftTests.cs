using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R86-commands-insert-move-refadjust-5-1: Insert/Delete Rows/Columns never moved a chart's own
/// drawing position (Left/Top) — only chart.DataRange shifted. A chart with "Move and size with
/// cells" (OneCell/TwoCellAnchor) anchored below/right of an inserted or deleted row/column band
/// stayed fixed on the canvas while the data it plots moved underneath it, because ChartModel has no
/// cell-anchor field and no insert/delete/move command ever touched Left/Top.
/// </summary>
public sealed class R86_ChartDrawingPositionShiftTests
{
    // ── Finding: a TwoCell/OneCell-anchored chart's position must track inserted/deleted rows ──────

    [Fact]
    public void InsertRows_ChartAnchoredBelowInsertedRows_ShiftsDownAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // sheet.DefaultRowHeight is 20 by default, so 10 inserted rows add 200 px of height.
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DrawingAnchorKind = ChartDrawingAnchorKind.TwoCell,
            Left = 0,
            Top = 400,
        };
        sheet.Charts.Add(chart);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 10);
        command.Apply(ctx).Success.Should().BeTrue();

        chart.Top.Should().Be(600,
            "10 inserted rows above the chart's anchor add 10*20=200px, so the chart must move down " +
            "to stay visually below the data that shifted under it, not remain fixed at its old Top");
        chart.Left.Should().Be(0, "no columns were inserted");

        command.Revert(ctx);
        chart.Top.Should().Be(400);
        chart.Left.Should().Be(0);
    }

    // ── No-regression sibling: an Absolute-anchored chart ("don't move with cells") must stay put ──

    [Fact]
    public void InsertRows_ChartWithAbsoluteAnchor_DoesNotShiftPosition()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DrawingAnchorKind = ChartDrawingAnchorKind.Absolute,
            Left = 0,
            Top = 400,
        };
        sheet.Charts.Add(chart);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 10);
        command.Apply(ctx).Success.Should().BeTrue();

        chart.Top.Should().Be(400, "an Absolute-anchored chart ('Don't move or size with cells') must stay fixed on the page");

        command.Revert(ctx);
        chart.Top.Should().Be(400);
    }

    [Fact]
    public void DeleteRows_ChartAnchoredBelowDeletedRows_ShiftsUpAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DrawingAnchorKind = ChartDrawingAnchorKind.TwoCell,
            Left = 0,
            Top = 600,
        };
        sheet.Charts.Add(chart);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 10);
        command.Apply(ctx).Success.Should().BeTrue();

        chart.Top.Should().Be(400,
            "deleting the 10 rows above the chart's anchor removes 10*20=200px, so the chart must move " +
            "up to stay visually anchored to the same (now-relocated) data instead of staying fixed");

        command.Revert(ctx);
        chart.Top.Should().Be(600);
    }

    [Fact]
    public void InsertColumns_ChartAnchoredRightOfInsertedColumns_ShiftsRightAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // sheet.DefaultColumnWidth is 8.43 character units; the writer's px conversion factor is *8.
        var defaultColumnWidthPx = sheet.DefaultColumnWidth * 8;
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DrawingAnchorKind = ChartDrawingAnchorKind.TwoCell,
            Left = defaultColumnWidthPx * 20,
            Top = 0,
        };
        sheet.Charts.Add(chart);

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 5);
        command.Apply(ctx).Success.Should().BeTrue();

        chart.Left.Should().BeApproximately(defaultColumnWidthPx * 25, 0.001,
            "5 inserted columns above the chart's anchor add 5 default-width columns, so the chart " +
            "must move right to stay visually right of the data that shifted under it");

        command.Revert(ctx);
        chart.Left.Should().BeApproximately(defaultColumnWidthPx * 20, 0.001);
    }
}
