using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class StructuredReferenceFormulaTests
{
    [Fact]
    public void Lexer_CombinedTableSelector_EmitsSingleSelectorToken()
    {
        var tokens = new Lexer("=SUM(Sales[[#Data],[Amount]])").Tokenize();

        tokens.Select(token => $"{token.Type}:{token.Value}")
            .Should().Contain("StructuredReferenceSelector:[#Data],[Amount]");
    }

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

    [Theory]
    [InlineData("=COLUMNS(Sales[#Headers])", 2)]
    [InlineData("=ROWS(Sales[#Data])", 2)]
    [InlineData("=ROWS(Sales[#All])", 3)]
    public void TableSelectorReference_ResolvesModeledTableSections(string formula, double expected)
    {
        var (workbook, sheet) = CreateSalesWorkbook();
        var evaluator = new FormulaEvaluator();

        var result = evaluator.Evaluate(formula, sheet, workbook);

        result.Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=COLUMNS(Sales[[#Headers],[Amount]])", 1)]
    [InlineData("=SUM(Sales[[#Data],[Amount]])", 30)]
    [InlineData("=ROWS(Sales[[#Totals],[Amount]])", 1)]
    public void CombinedTableSelectorReference_ResolvesSectionColumnIntersections(string formula, double expected)
    {
        var (workbook, sheet) = CreateSalesWorkbookWithTotals();
        var evaluator = new FormulaEvaluator();

        var result = evaluator.Evaluate(formula, sheet, workbook);

        result.Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=SUM(Sales[[Amount]:[Tax]])", 33)]
    [InlineData("=SUM(Sales[[#Data],[Amount]:[Tax]])", 33)]
    [InlineData("=COLUMNS(Sales[[#Headers],[Amount]:[Tax]])", 2)]
    public void MultiColumnStructuredReference_ResolvesColumnRange(string formula, double expected)
    {
        var (workbook, sheet) = CreateSalesWorkbookWithTax();
        var evaluator = new FormulaEvaluator();

        var result = evaluator.Evaluate(formula, sheet, workbook);

        result.Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=[@Amount]", 10)]
    [InlineData("=Sales[@Amount]", 10)]
    [InlineData("=SUM(Sales[[#This Row],[Amount]:[Tax]])", 11)]
    public void CurrentRowStructuredReferences_ResolveRelativeToFormulaCell(string formula, double expected)
    {
        var (workbook, sheet) = CreateSalesWorkbookWithTax();
        var evaluator = new FormulaEvaluator();
        var formulaCell = new CellAddress(sheet.Id, 2, 3);

        var result = evaluator.Evaluate(formula, sheet, workbook, formulaCell);

        result.Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void StructuredReferenceColumnNames_AreMatchedCaseInsensitivelyAndMayContainSpaces()
    {
        var (workbook, sheet) = CreateSalesWorkbookWithSpacedColumn();
        var evaluator = new FormulaEvaluator();

        var result = evaluator.Evaluate("=SUM(Sales[net amount])", sheet, workbook);

        result.Should().Be(new NumberValue(27));
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

    private static (Workbook Workbook, Sheet Sheet) CreateSalesWorkbookWithTotals()
    {
        var (workbook, sheet) = CreateSalesWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        sheet.StructuredTables.Clear();
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            HasAutoFilter = true,
            TotalsRowShown = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateSalesWorkbookWithTax()
    {
        var workbook = new Workbook("StructuredReferenceTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Tax"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 3)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Tax"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateSalesWorkbookWithSpacedColumn()
    {
        var workbook = new Workbook("StructuredReferenceTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Net Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(9));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(18));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 3)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Net Amount"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet);
    }
}
