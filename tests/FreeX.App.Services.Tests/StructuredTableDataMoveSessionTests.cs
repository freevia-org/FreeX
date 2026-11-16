using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class StructuredTableDataMoveSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CutTableDataOutsideTable_PreservesFormulaValueAndUndoRedo(bool crossSheet)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new TextValue("Alpha"));
        sheet.SetCell(b1, new TextValue("Charlie"));
        sheet.SetFormula(a2, "[@Charlie]");
        sheet.SetCell(a3, new NumberValue(20));
        sheet.SetCell(b2, new NumberValue(1000));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a1, new CellAddress(sheet.Id, 3, 2)), Columns = { new StructuredTableColumnModel(1, "Alpha"), new StructuredTableColumnModel(2, "Charlie") } });
        var session = new WorkbookSessionFactory().Create(new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened", false), viewportHeight: 240, viewportWidth: 320);
        session.RecalculateWorkbook();
        session.SelectRange(new GridRange(a2, a3));
        var clipboard = session.CutSelectedRangeText();
        var destinationSheet = crossSheet ? other : sheet;
        if (crossSheet) session.SelectSheet(other.Id).Should().BeTrue();
        var target = new CellAddress(destinationSheet.Id, 2, 8);
        session.SelectCell(target);
        var result = session.PasteClipboardTextAtActiveCell(clipboard);
        result.Success.Should().BeTrue(result.ErrorMessage);
        destinationSheet.GetCell(target)!.FormulaText.Should().Be("SalesTable[@Charlie]");
        destinationSheet.GetValue(target).Should().Be(new NumberValue(1000));
        sheet.GetValue(a2).Should().Be(BlankValue.Instance);
        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(a2)!.FormulaText.Should().Be("[@Charlie]");
        sheet.GetValue(a2).Should().Be(new NumberValue(1000));
        session.RedoLastEdit().Success.Should().BeTrue();
        destinationSheet.GetCell(target)!.FormulaText.Should().Be("SalesTable[@Charlie]");
        destinationSheet.GetValue(target).Should().Be(new NumberValue(1000));
        if (session.ActiveSheet.Id != sheet.Id) session.SelectSheet(sheet.Id).Should().BeTrue();
        session.SelectCell(b2);
        session.CommitCellText("1500").Success.Should().BeTrue();
        destinationSheet.GetValue(target).Should().Be(new NumberValue(1500));
    }
}
