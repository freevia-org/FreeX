using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class StructuredReferenceFormulaTests
{
    [Fact]
    public void Sum_TableColumnReference_UsesTableDataBodyCells()
    {
        var (workbook, sheet) = CreateSalesWorkbook();
        var evaluator = new FormulaEvaluator();

        var result = evaluator.Evaluate("=SUM(Sales[Amount])", sheet, workbook);

        result.Should().Be(new NumberValue(30));
    }

    [Fact]
    public void TableColumnReference_ReturnsNameErrorForUnknownColumn()
    {
        var (workbook, sheet) = CreateSalesWorkbook();
        var evaluator = new FormulaEvaluator();

        var result = evaluator.Evaluate("=SUM(Sales[Missing])", sheet, workbook);

        result.Should().Be(ErrorValue.Name);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateSalesWorkbook()
    {
        var workbook = new Workbook("StructuredReferenceTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet);
    }
}
