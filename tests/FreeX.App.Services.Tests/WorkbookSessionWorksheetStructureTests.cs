using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionWorksheetStructureTests
{
    [Fact]
    public void InsertSelectedRows_AppliesToGroupedSheetsAndPreservesSelection()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var active = session.ActiveSheet;
        var grouped = session.Workbook.AddSheet("Grouped");
        active.SetCell(new CellAddress(active.Id, 3, 1), new NumberValue(11));
        grouped.SetCell(new CellAddress(grouped.Id, 3, 1), new NumberValue(22));
        var range = new GridRange(
            new CellAddress(active.Id, 3, 2),
            new CellAddress(active.Id, 4, 4));
        session.SelectRange(range);
        session.SelectAllVisibleSheets();

        var result = session.InsertSelectedRows();

        result.Success.Should().BeTrue();
        result.Operation.Should().Be(WorkbookWorksheetStructureOperation.InsertRows);
        result.TargetRange.Should().Be(range);
        result.ViewportRowDelta.Should().Be(2);
        result.InvalidatesFormulaTraceArrows.Should().BeTrue();
        session.SelectedRange.Should().Be(range);
        session.ActiveCell.Should().Be(range.Start);
        active.GetCell(5, 1)!.Value.Should().Be(new NumberValue(11));
        grouped.GetCell(5, 1)!.Value.Should().Be(new NumberValue(22));
    }

    [Fact]
    public void InsertSelectedRows_RepeatLastReReadsSelection()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var sheet = session.ActiveSheet;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(5));

        SelectRow(session, 2);
        session.InsertSelectedRows().Success.Should().BeTrue();
        SelectRow(session, 5);
        var repeatedSelection = session.SelectedRange;

        session.RepeatLastAction().Success.Should().BeTrue();

        sheet.GetCell(3, 1)!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(7, 1)!.Value.Should().Be(new NumberValue(5));
        session.SelectedRange.Should().Be(repeatedSelection);
    }

    [Fact]
    public void InsertSelectedCells_OwnsShiftCommandAndReportsNoViewportDelta()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var sheet = session.ActiveSheet;
        var address = new CellAddress(sheet.Id, 2, 2);
        var range = new GridRange(address, address);
        sheet.SetCell(address, new NumberValue(7));
        session.SelectRange(range);

        var result = session.InsertSelectedCells(InsertCellsShiftDirection.Right);

        result.Success.Should().BeTrue();
        result.Operation.Should().Be(WorkbookWorksheetStructureOperation.InsertCellsShiftRight);
        result.ViewportRowDelta.Should().Be(0);
        result.ViewportColumnDelta.Should().Be(0);
        result.InvalidatesFormulaTraceArrows.Should().BeFalse();
        session.SelectedRange.Should().Be(range);
        sheet.GetCell(2, 3)!.Value.Should().Be(new NumberValue(7));
    }

    private static void SelectRow(WorkbookSession session, uint row)
    {
        var range = new GridRange(
            new CellAddress(session.ActiveSheet.Id, row, 1),
            new CellAddress(session.ActiveSheet.Id, row, CellAddress.MaxCol));
        session.SelectRange(range);
    }
}
