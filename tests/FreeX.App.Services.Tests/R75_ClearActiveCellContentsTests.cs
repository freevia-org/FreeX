using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for R75-commands-clear-delete-4-1
/// (<see cref="WorkbookSession.ClearActiveCellContents"/>, used by the Avalonia shell's
/// Backspace/<c>ClearSelectionAndEdit</c> shortcut): Backspace on a multi-cell selection must
/// clear ONLY the active cell -- unlike <see cref="WorkbookSession.ClearSelectedRangeContents"/>
/// (Delete/Clear Contents), which clears the whole selection. It must also leave the selection's
/// shape untouched (unlike a normal committed edit, which collapses the selection to the edited
/// cell) so the caller's subsequent inline edit still operates within the original selection.
/// </summary>
public sealed class R75_ClearActiveCellContentsTests
{
    [Fact]
    public void ClearActiveCellContents_OnMultiCellSelection_ClearsOnlyActiveCell_LeavesRestUntouched()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(3));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectRange(new GridRange(a1, a3));

        var result = session.ClearActiveCellContents();

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(BlankValue.Instance, "Backspace must clear the active cell (A1)");
        sheet.GetCell(a2)!.Value.Should().Be(new NumberValue(2), "Backspace must NOT touch A2 -- it is not Delete/Clear Contents");
        sheet.GetCell(a3)!.Value.Should().Be(new NumberValue(3), "Backspace must NOT touch A3 -- it is not Delete/Clear Contents");
        session.SelectedRange.Should().Be(
            new GridRange(a1, a3),
            "clearing just the active cell must leave the multi-cell selection's shape untouched " +
            "(unlike a normal committed edit, which collapses the selection to the edited cell)");
    }

    [Fact]
    public void ClearSelectedRangeContents_OnMultiCellSelection_StillClearsWholeSelection()
    {
        // Sibling no-regression: the pre-existing Delete/Clear Contents full-selection clear must
        // be completely unaffected by adding the Backspace-only-active-cell path.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(3));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectRange(new GridRange(a1, a3));

        var result = session.ClearSelectedRangeContents();

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(BlankValue.Instance, "Delete/Clear Contents must still clear the whole selection");
        sheet.GetValue(a2).Should().Be(BlankValue.Instance, "Delete/Clear Contents must still clear the whole selection");
        sheet.GetValue(a3).Should().Be(BlankValue.Instance, "Delete/Clear Contents must still clear the whole selection");
    }

    [Fact]
    public void ClearActiveCellContents_OnSingleCellSelection_StillClearsThatCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(99));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectRange(new GridRange(a1, a1));

        var result = session.ClearActiveCellContents();

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(BlankValue.Instance, "Backspace on a single-cell selection must clear that cell");
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
