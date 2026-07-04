using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for H21: Undo/Redo of a multi-cell command (e.g. Sort) must restore the
/// selection to the affected range's bounding box, matching Excel, instead of collapsing to a
/// single cell.
/// </summary>
public sealed class WorkbookSessionUndoSelectionTests
{
    [Fact]
    public void UndoLastEdit_AfterSortSelectedRange_RestoresFullAffectedRangeSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(2));
        sheet.SetCell(b1, new TextValue("two"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("three"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("one"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var range = new GridRange(a1, new CellAddress(sheet.Id, 3, 2));
        session.SelectRange(range);

        var sortResult = session.SortSelectedRange(ascending: true);
        sortResult.Success.Should().BeTrue(sortResult.ErrorMessage);
        session.SelectedRange.Should().Be(range);

        var undoResult = session.UndoLastEdit();

        undoResult.Success.Should().BeTrue(undoResult.ErrorMessage);
        session.SelectedRange.Should().Be(range);
        session.ActiveCell.Should().Be(a1);
    }

    [Fact]
    public void RedoLastEdit_AfterUndoOfSortSelectedRange_RestoresFullAffectedRangeSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(2));
        sheet.SetCell(b1, new TextValue("two"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("three"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("one"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var range = new GridRange(a1, new CellAddress(sheet.Id, 3, 2));
        session.SelectRange(range);

        session.SortSelectedRange(ascending: true).Success.Should().BeTrue();
        session.UndoLastEdit().Success.Should().BeTrue();
        session.SelectRange(new GridRange(a1, a1));

        var redoResult = session.RedoLastEdit();

        redoResult.Success.Should().BeTrue(redoResult.ErrorMessage);
        session.SelectedRange.Should().Be(range);
        session.ActiveCell.Should().Be(a1);
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
