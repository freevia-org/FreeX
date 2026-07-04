using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for G20: cut+paste (a MOVE) must land the ActiveCell/selection on the
/// pasted DESTINATION range, matching Excel, instead of leaving ActiveCell on the now-blank
/// source cell. MoveRangeCommand's AffectedCells lists the source cell before the destination,
/// so anchoring naively on the first affected cell picks the wrong address.
/// </summary>
public sealed class WorkbookSessionCutMoveActiveCellTests
{
    [Fact]
    public void PasteClipboardTextAtActiveCell_CutPasteMove_ActiveCellLandsOnDestinationNotSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(b1, new NumberValue(5));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b1);
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d1);

        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        // Excel selects the pasted destination after a cut+paste move, not the blank source.
        session.ActiveCell.Should().Be(d1);
        session.ActiveSheet.ActiveRow.Should().Be(d1.Row);
        session.ActiveSheet.ActiveCol.Should().Be(d1.Col);
        session.SelectedRange.Should().Be(new GridRange(d1, d1));
        sheet.GetCell(b1).Should().BeNull();
        sheet.GetCell(d1)!.Value.Should().Be(new NumberValue(5));
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_CutPasteMoveOfMultiCellRange_ActiveCellLandsOnDestinationRangeStart()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var d2 = new CellAddress(sheet.Id, 2, 4);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, a2));
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d1);

        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        session.ActiveCell.Should().Be(d1);
        session.SelectedRange.Should().Be(new GridRange(d1, d2));
        sheet.GetCell(a1).Should().BeNull();
        sheet.GetCell(a2).Should().BeNull();
        sheet.GetCell(d1)!.Value.Should().Be(new NumberValue(1));
        sheet.GetCell(d2)!.Value.Should().Be(new NumberValue(2));
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
