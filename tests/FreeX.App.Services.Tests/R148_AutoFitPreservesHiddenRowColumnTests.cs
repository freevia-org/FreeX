using FluentAssertions;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression tests for rowcol-sizing F1: AutoFit Row Height / AutoFit Column Width must not
/// silently un-hide a hidden row/column that happens to fall inside the AutoFit selection.
/// PlanAutoFitRowHeights/PlanAutoFitColumnWidths used to emit a <see cref="RowColumnSizePlan"/> for
/// every row/column in the measurement bounds regardless of the row/column's own hidden state; the
/// resulting SetRowHeightCommand/SetColumnWidthCommand then cleared HiddenRows/HiddenCols as a side
/// effect of setting an explicit size (see SheetLayoutCommands.cs), reversing the user's explicit
/// Hide with no warning.
/// </summary>
public sealed class R148_AutoFitPreservesHiddenRowColumnTests
{
    [Fact]
    public void PlanAutoFitRowHeights_HiddenRowInSelection_DoesNotProduceAPlan()
    {
        // Row 5 is manually hidden but sits inside the A1:A10 AutoFit selection.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultRowHeight = 20;
        sheet.HiddenRows.Add(5);

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));

        var plans = RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => new AutoFitCellText($"content {row}"),
            defaultHeight: sheet.DefaultRowHeight);

        plans.Select(p => p.Index).Should().NotContain(5u, "the hidden row must not be resized/unhidden by AutoFit");
        plans.Select(p => p.Index).Should().BeEquivalentTo(Enumerable.Range(1, 10).Where(r => r != 5).Select(r => (uint)r));
    }

    [Fact]
    public void PlanAutoFitRowHeights_HiddenRowInSelection_ApplyingTheCommand_LeavesRowHidden()
    {
        // End-to-end reproduction from the finding: plan -> composite command -> Apply must not
        // clear sheet.HiddenRows for row 5.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultRowHeight = 20;
        sheet.HiddenRows.Add(5);
        var context = new TestCommandContext(workbook);

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));

        var plans = RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => new AutoFitCellText($"content {row}"),
            defaultHeight: sheet.DefaultRowHeight);

        var command = RowColumnSizingPlanner.CreateAutoFitRowHeightCommand(sheet.Id, plans);
        command.Should().NotBeNull();
        command!.Apply(context).Success.Should().BeTrue();

        sheet.IsRowEffectivelyHidden(5).Should().BeTrue("AutoFit Row Height must not silently un-hide a hidden row caught in the selection");
        sheet.RowHeights.Should().NotContainKey(5u, "a hidden row must not receive an explicit AutoFit height");
    }

    [Fact]
    public void PlanAutoFitColumnWidths_HiddenColumnInSelection_DoesNotProduceAPlan()
    {
        // Column 3 is manually hidden but sits inside a 6-column AutoFit-column-width selection.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 8.43;
        sheet.HiddenCols.Add(3);

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 6));

        var plans = RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => new AutoFitCellText($"content {col}"),
            defaultWidth: sheet.DefaultColumnWidth);

        plans.Select(p => p.Index).Should().NotContain(3u, "the hidden column must not be resized/unhidden by AutoFit");
        plans.Select(p => p.Index).Should().BeEquivalentTo(Enumerable.Range(1, 6).Where(c => c != 3).Select(c => (uint)c));
    }

    [Fact]
    public void PlanAutoFitColumnWidths_HiddenColumnInSelection_ApplyingTheCommand_LeavesColumnHidden()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 8.43;
        sheet.HiddenCols.Add(3);
        var context = new TestCommandContext(workbook);

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 6));

        var plans = RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => new AutoFitCellText($"content {col}"),
            defaultWidth: sheet.DefaultColumnWidth);

        var command = RowColumnSizingPlanner.CreateAutoFitColumnWidthCommand(sheet.Id, plans);
        command.Should().NotBeNull();
        command!.Apply(context).Success.Should().BeTrue();

        sheet.IsColEffectivelyHidden(3).Should().BeTrue("AutoFit Column Width must not silently un-hide a hidden column caught in the selection");
        sheet.ColumnWidths.Should().NotContainKey(3u, "a hidden column must not receive an explicit AutoFit width");
    }

    [Fact]
    public void PlanAutoFitRowHeights_VisibleRowsAroundAHiddenRow_AreStillSizedNormally()
    {
        // Sibling/no-regression case: the fix must not stop AutoFit from sizing the rows that are
        // NOT hidden -- only the hidden row itself is skipped.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultRowHeight = 20;
        sheet.HiddenRows.Add(5);

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        var longSentence = new string('x', 60);

        var plans = RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => new AutoFitCellText(longSentence, WrapText: true, ColumnWidth: 8.43),
            defaultHeight: sheet.DefaultRowHeight);

        plans.Should().HaveCount(9);
        plans.Should().OnlyContain(p => p.Size > sheet.DefaultRowHeight, "the visible rows' wrapped long content must still grow their AutoFit height as before");
    }

    [Fact]
    public void PlanAutoFitColumnWidths_GroupCollapsedColumn_IsAlsoSkipped()
    {
        // "Effectively hidden" covers group-collapse too, not just manual hide -- must be skipped
        // the same way as HiddenCols.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DefaultColumnWidth = 8.43;
        sheet.GroupHiddenCols.Add(3);

        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 6));

        var plans = RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => new AutoFitCellText($"content {col}"),
            defaultWidth: sheet.DefaultColumnWidth);

        plans.Select(p => p.Index).Should().NotContain(3u);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
