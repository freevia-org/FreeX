using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R94-cmd-paste-charts-cross-sheet-dataRange: <see cref="PasteChartsCommand"/> and
/// <see cref="DuplicateDrawingObjectCommand"/> both reused
/// <see cref="DuplicateSheetDrawingCloner.CloneChart"/>'s whole-sheet "Duplicate Sheet" DataRange-remap
/// rule (a same-sheet DataRange follows the copy onto the new sheet). That rule is correct for
/// Duplicate Sheet, where the entire sheet -- data included -- is cloned in place under a new SheetId.
/// It is wrong for a plain cross-sheet Ctrl+C/Ctrl+V of a chart-carrying range, or of a selected chart
/// object: only the chart itself travels, not the data it plots, so a same-sheet DataRange (the common
/// case -- a chart plotting data on its own sheet) must keep pointing at the exact original source
/// sheet/cells. Before the fix, pasting cross-sheet silently swapped the DataRange's SheetId to the
/// destination sheet while leaving row/col untouched, pointing at whatever unrelated cells happen to
/// sit at that address on the destination sheet. These tests drive the real production entry points
/// (<see cref="PasteCommandFactory.CreateInternalPasteCommand"/> and
/// <see cref="DuplicateDrawingObjectCommand"/> itself), not a hand-built model.
/// </summary>
public sealed class R94_CrossSheetChartDataRangeTests
{
    [Fact]
    public void InternalPaste_CrossSheetChartCarryKeepsDataRangeOnSourceSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        // Chart lives on Sheet1, plots Sheet1 data elsewhere on the same sheet (F1:G5) -- the
        // extremely common case the finding calls out.
        var dataRange = new GridRange(new CellAddress(sheet1.Id, 0, 5), new CellAddress(sheet1.Id, 4, 6));
        var sourceRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 3));
        var destination = new CellAddress(sheet2.Id, 10, 10);

        sheet1.SetCell(sourceRange.Start, Cell.FromValue(new TextValue("hi")));
        var sourceCells = sourceRange.AllCells()
            .Select(a => (a, sheet1.GetCell(a) ?? Cell.FromValue(BlankValue.Instance)))
            .ToList();

        // Chart's top-left corner sits inside the copied range's pixel box (col 1 starts at
        // 8.43*8=67.44, row 1 starts at 20), same geometry R92's own test used.
        var chart = new ChartModel { Left = 70, Top = 25, Width = 200, Height = 150, DataRange = dataRange };
        sheet1.Charts.Add(chart);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet2.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, default);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet1.Charts.Should().ContainSingle("the original chart on Sheet1 stays put");
        sheet2.Charts.Should().ContainSingle("the chart was carried to the destination sheet");
        var pasted = sheet2.Charts[0];

        // The pasted chart's DataRange must still point at Sheet1's F1:G5 -- NOT get its SheetId
        // swapped to Sheet2 while keeping the same row/col (which would point at unrelated cells).
        pasted.DataRange.Should().Be(dataRange);
        pasted.DataRange.Start.Sheet.Should().Be(sheet1.Id);

        command.Revert(ctx);
        sheet2.Charts.Should().BeEmpty();
        sheet1.Charts.Should().ContainSingle();
    }

    [Fact]
    public void InternalPaste_SameSheetChartCarryKeepsDataRangeUnchanged()
    {
        // No-regression sibling: a same-sheet plain-paste chart carry (R92's own scenario) must not
        // start remapping the DataRange either -- Excel never remaps an object's own data source to
        // follow a cell-range paste, same-sheet or cross-sheet alike.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(new CellAddress(sheet.Id, 0, 5), new CellAddress(sheet.Id, 4, 6));
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var destination = new CellAddress(sheet.Id, 10, 10);

        sheet.SetCell(sourceRange.Start, Cell.FromValue(new TextValue("hi")));
        var sourceCells = sourceRange.AllCells()
            .Select(a => (a, sheet.GetCell(a) ?? Cell.FromValue(BlankValue.Instance)))
            .ToList();

        var chart = new ChartModel { Left = 70, Top = 25, Width = 200, Height = 150, DataRange = dataRange };
        sheet.Charts.Add(chart);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb, sheet.Id, sourceRange, sourceCells, destination, PasteCellsMode.All, default);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var pasted = sheet.Charts.Single(c => c.Id != chart.Id);
        pasted.DataRange.Should().Be(dataRange);
    }

    [Fact]
    public void DuplicateDrawingObjectCommand_CrossSheetChartKeepsDataRangeOnSourceSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(new CellAddress(sheet1.Id, 0, 0), new CellAddress(sheet1.Id, 3, 4));
        new AddChartCommand(sheet1.Id, dataRange, ChartType.Column, "Sales").Apply(ctx).Success.Should().BeTrue();
        var originalChart = sheet1.Charts[0];
        originalChart.DataRange.Should().Be(dataRange);

        var command = new DuplicateDrawingObjectCommand(sheet1.Id, sheet2.Id, SelectionPaneObjectKind.Chart, originalChart.Id);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var duplicate = sheet2.Charts.Single();
        duplicate.DataRange.Should().Be(dataRange);
        duplicate.DataRange.Start.Sheet.Should().Be(sheet1.Id);
    }

    [Fact]
    public void DuplicateDrawingObjectCommand_SameSheetChartStillKeepsDataRangeUnchanged()
    {
        // No-regression sibling covering R91's original same-sheet scenario (already asserted DataRange
        // equality) so this fix's parameter plumbing doesn't disturb same-sheet duplicate/paste either.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 4));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales", left: 10, top: 10).Apply(ctx).Success.Should().BeTrue();
        var originalChart = sheet.Charts[0];

        var command = new DuplicateDrawingObjectCommand(sheet.Id, sheet.Id, SelectionPaneObjectKind.Chart, originalChart.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var duplicate = sheet.Charts.Single(c => c.Id != originalChart.Id);
        duplicate.DataRange.Should().Be(originalChart.DataRange);
    }
}
