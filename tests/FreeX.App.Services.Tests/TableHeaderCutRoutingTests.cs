using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class TableHeaderCutRoutingTests
{
    [Theory]
    [InlineData("SUM([Charlie])", "SUM(SalesTable[Charlie])")]
    [InlineData("SUM([@Charlie])", "SUM(SalesTable[@Charlie])")]
    public void CutHeaderAndFormulaAcrossSheets_UsesMoveAndPreservesTableBinding(string original, string expected)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var body = new CellAddress(sheet.Id, 2, 1);
        var destination = new CellAddress(other.Id, 1, 8);
        sheet.SetCell(a, new TextValue("Alpha"));
        sheet.SetCell(b, new TextValue("Charlie"));
        sheet.SetCell(body, Cell.FromFormula(original));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1000));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 3, 2)), Columns = { new StructuredTableColumnModel(1, "Alpha"), new StructuredTableColumnModel(2, "Charlie") } });
        var session = new WorkbookSessionFactory().Create(new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened", false), viewportHeight: 240, viewportWidth: 320);
        session.SelectRange(new GridRange(a, body));
        var clipboard = session.CutSelectedRangeText();
        session.SelectSheet(other.Id).Should().BeTrue();
        session.SelectCell(destination);
        var result = session.PasteClipboardTextAtActiveCell(clipboard);
        result.Success.Should().BeTrue(result.ErrorMessage);
        other.GetCell(2, 8)!.FormulaText.Should().Be(expected);
        other.GetValue(2, 8).Should().Be(new NumberValue(1000));
        sheet.GetValue(a).Should().Be(new TextValue("Column1"));
        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(body)!.FormulaText.Should().Be(original);
        sheet.GetValue(a).Should().Be(new TextValue("Alpha"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PasteAcrossTables_OnlyReconcilesTableWhoseHeaderChanged(bool malformedOtherTable)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var d = new CellAddress(sheet.Id, 1, 4);
        var e = new CellAddress(sheet.Id, 1, 5);
        var source = new CellAddress(sheet.Id, 1, 7);
        var formula = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(d, new TextValue("Cost"));
        sheet.SetCell(e, new TextValue("Cost"));
        sheet.SetCell(source, new TextValue("Cost"));
        sheet.SetCell(formula, Cell.FromFormula("SUM(FirstTable[Sales])"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "FirstTable", DisplayName = "FirstTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 1)), Columns = { new StructuredTableColumnModel(1, "Sales") } });
        var other = new StructuredTableModel { Id = 2, Name = "OtherTable", DisplayName = "OtherTable", Range = new GridRange(d, new CellAddress(sheet.Id, 2, 5)), Columns = { new StructuredTableColumnModel(1, "Cost") } };
        if (!malformedOtherTable) other.Columns.Add(new StructuredTableColumnModel(2, "Cost"));
        sheet.StructuredTables.Add(other);
        var session = new WorkbookSessionFactory().Create(new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened", false), viewportHeight: 240, viewportWidth: 320);
        session.SelectCell(source);
        var clipboard = session.CopySelectedRangeText();
        var first = new GridRange(a, a);
        var second = new GridRange(d, d);
        session.SelectRanges(first, [first, second]);
        var result = session.PasteSpecialClipboardAtActiveCell(clipboard, PasteCellsMode.Values, default);
        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(a).Should().Be(new TextValue("Cost"));
        sheet.GetValue(d).Should().Be(new TextValue("Cost"));
        sheet.GetValue(e).Should().Be(new TextValue("Cost"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(FirstTable[Cost])");
        sheet.StructuredTables[1].Should().BeSameAs(other);
        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetValue(a).Should().Be(new TextValue("Sales"));
        sheet.GetValue(e).Should().Be(new TextValue("Cost"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(FirstTable[Sales])");
    }

    [Fact]
    public void PasteFormatsToMultipleMalformedTableHeaders_PreservesContentAndSucceeds()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var source = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("Cost"));
        sheet.SetCell(source, Cell.FromValue(new TextValue("Source")));
        sheet.GetCell(source)!.StyleId = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "Sales") } });
        var session = new WorkbookSessionFactory().Create(new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened", false), viewportHeight: 240, viewportWidth: 320);
        session.SelectCell(source);
        var clipboard = session.CopySelectedRangeText();
        var first = new GridRange(a, a);
        var second = new GridRange(b, b);
        session.SelectRanges(first, [first, second]);
        var result = session.PasteSpecialClipboardAtActiveCell(clipboard, PasteCellsMode.Formats, default);
        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(a).Should().Be(new TextValue("Sales"));
        sheet.GetValue(b).Should().Be(new TextValue("Cost"));
        workbook.GetStyle(sheet.GetCell(a)!.StyleId).Bold.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(b)!.StyleId).Bold.Should().BeTrue();
        sheet.StructuredTables[0].Columns.Should().ContainSingle().Which.Name.Should().Be("Sales");
        session.UndoLastEdit().Success.Should().BeTrue();
    }

    [Fact]
    public void PasteToMultipleHeaders_NormalizesAsOneBatchAndUndoes()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var source = new CellAddress(sheet.Id, 1, 4);
        var formula = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("Cost"));
        sheet.SetCell(source, new TextValue("Cost"));
        sheet.SetCell(formula, Cell.FromFormula("SUM(SalesTable[Sales])+SUM(SalesTable[Cost])"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "Sales"), new StructuredTableColumnModel(2, "Cost") } });
        var session = new WorkbookSessionFactory().Create(new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened", false), viewportHeight: 240, viewportWidth: 320);
        session.SelectCell(source);
        var clipboard = session.CopySelectedRangeText();
        var first = new GridRange(a, a);
        var second = new GridRange(b, b);
        session.SelectRanges(first, [first, second]);
        var result = session.PasteSpecialClipboardAtActiveCell(clipboard, PasteCellsMode.Values, default);
        result.Success.Should().BeTrue();
        sheet.GetValue(a).Should().Be(new TextValue("Cost"));
        sheet.GetValue(b).Should().Be(new TextValue("Cost2"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Cost])+SUM(SalesTable[Cost2])");
        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetValue(a).Should().Be(new TextValue("Sales"));
        sheet.GetValue(b).Should().Be(new TextValue("Cost"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Sales])+SUM(SalesTable[Cost])");
    }

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
