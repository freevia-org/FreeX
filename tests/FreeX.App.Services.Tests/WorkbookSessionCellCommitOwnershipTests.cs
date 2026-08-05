using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionCellCommitOwnershipTests
{
    [Fact]
    public void CommitCellTextAcrossSelection_FillsEveryAreaAndPreservesSelection()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var sheet = session.ActiveSheet;
        var areaA = Range(sheet.Id, 1, 1, 2, 1);
        var areaC = Range(sheet.Id, 1, 3, 2, 3);

        session.SynchronizeSelectionState(sheet.Id, areaC, [areaA, areaC], areaC.Start);

        session.CommitCellTextAcrossSelection("5").Success.Should().BeTrue();

        sheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new NumberValue(5));
        sheet.GetCell(1, 3)!.Value.Should().Be(new NumberValue(5));
        sheet.GetCell(2, 3)!.Value.Should().Be(new NumberValue(5));
        session.SelectedRange.Should().Be(areaC);
        session.SelectedRanges.Should().Equal(areaA, areaC);
        session.CanUndo.Should().BeTrue();

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(1, 1).Should().BeNull();
        sheet.GetCell(2, 3).Should().BeNull();
    }

    [Fact]
    public void CommitCellTextAcrossSelection_UsesSynchronizedGroupedSheets()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var firstSheet = session.ActiveSheet;
        var secondSheet = session.Workbook.AddSheet("Sheet2");
        var selected = Range(firstSheet.Id, 2, 2, 3, 2);

        session.SynchronizeSelectionState(
            firstSheet.Id,
            selected,
            [selected],
            selected.Start,
            [firstSheet.Id, secondSheet.Id],
            firstSheet.Id);

        session.CommitCellTextAcrossSelection("9").Success.Should().BeTrue();

        firstSheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(9));
        firstSheet.GetCell(3, 2)!.Value.Should().Be(new NumberValue(9));
        secondSheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(9));
        secondSheet.GetCell(3, 2)!.Value.Should().Be(new NumberValue(9));

        session.UndoLastEdit().Success.Should().BeTrue();
        firstSheet.GetCell(2, 2).Should().BeNull();
        secondSheet.GetCell(2, 2).Should().BeNull();
    }

    [Fact]
    public void CommitCellText_UsesSynchronizedCrossSheetFormulaEditAddress()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var sourceSheet = session.ActiveSheet;
        var targetSheet = session.Workbook.AddSheet("Target");
        var formulaCell = new CellAddress(sourceSheet.Id, 1, 1);
        var pointedCell = new CellAddress(targetSheet.Id, 4, 3);
        var pointedRange = new GridRange(pointedCell, pointedCell);

        session.SynchronizeSelectionState(
            targetSheet.Id,
            pointedRange,
            [pointedRange],
            pointedCell,
            formulaEditAddress: formulaCell);

        session.CommitCellText("=1+1").Success.Should().BeTrue();

        sourceSheet.GetCell(formulaCell)!.FormulaText.Should().Be("1+1");
        sourceSheet.GetCell(formulaCell)!.Value.Should().Be(new NumberValue(2));
        targetSheet.GetCell(pointedCell).Should().BeNull();
        session.ActiveSheet.Id.Should().Be(sourceSheet.Id);
        session.ActiveCell.Should().Be(formulaCell);
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startColumn,
        uint endRow,
        uint endColumn) =>
        new(
            new CellAddress(sheetId, startRow, startColumn),
            new CellAddress(sheetId, endRow, endColumn));
}
