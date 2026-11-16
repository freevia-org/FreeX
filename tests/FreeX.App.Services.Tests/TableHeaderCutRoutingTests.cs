using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class TableHeaderCutRoutingTests
{
    [Fact]
    public void TransposedCutBetweenHeaders_UsesOneHeaderTransactionAndUndoes()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var formula = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("Cost"));
        sheet.SetCell(formula, Cell.FromFormula("SUM(SalesTable[Sales])+SUM(SalesTable[Cost])"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "Sales"), new StructuredTableColumnModel(2, "Cost") } });
        var session = new WorkbookSessionFactory().Create(new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened", false), viewportHeight: 240, viewportWidth: 320);
        session.SelectCell(a);
        var clipboard = session.CutSelectedRangeText();
        session.SelectCell(b);
        var result = session.PasteSpecialClipboardAtActiveCell(clipboard, PasteCellsMode.All, new PasteSpecialOptions(Transpose: true));
        result.Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("Column1"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Sales"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Column1])+SUM(SalesTable[Sales])");
        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("Sales"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Cost"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Sales])+SUM(SalesTable[Cost])");
    }
}
