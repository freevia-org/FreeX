namespace FreeW.Core.Model.Tests;

public class TableFormulaEvaluatorTests
{
    // Build a table whose cell texts are taken row-major from the given grid.
    private static Table Grid(params string[][] rows)
    {
        var table = new Table();
        foreach (var cells in rows)
        {
            var row = new TableRow();
            foreach (var text in cells)
                row.Cells.Add(new TableCell(text));
            table.Rows.Add(row);
        }
        return table;
    }

    [Fact]
    public void Sum_Above_AddsNumericCellsInTheColumn()
    {
        var table = Grid(["10"], ["20"], ["30"], [""]);
        var formula = new TableFormulaField("=SUM(ABOVE)");

        // Formula sits in the last row (index 3); it sums the three cells above it.
        TableFormulaEvaluator.Evaluate(table, 3, 0, formula).Should().Be("60");
    }

    [Fact]
    public void Sum_Left_AddsNumericCellsInTheRow()
    {
        var table = Grid(["1", "2", "3", ""]);
        var formula = new TableFormulaField("=SUM(LEFT)");

        TableFormulaEvaluator.Evaluate(table, 0, 3, formula).Should().Be("6");
    }

    [Fact]
    public void Average_Above_AveragesTheColumn()
    {
        var table = Grid(["2"], ["4"], ["6"], [""]);
        var formula = new TableFormulaField("=AVERAGE(ABOVE)");

        TableFormulaEvaluator.Evaluate(table, 3, 0, formula).Should().Be("4");
    }

    [Fact]
    public void Count_Above_CountsNumericCells()
    {
        var table = Grid(["5"], ["7"], ["9"], [""]);
        var formula = new TableFormulaField("=COUNT(ABOVE)");

        TableFormulaEvaluator.Evaluate(table, 3, 0, formula).Should().Be("3");
    }

    [Fact]
    public void Product_Left_MultipliesTheRow()
    {
        var table = Grid(["2", "3", "4", ""]);
        var formula = new TableFormulaField("=PRODUCT(LEFT)");

        TableFormulaEvaluator.Evaluate(table, 0, 3, formula).Should().Be("24");
    }

    [Fact]
    public void MaxAndMin_Above_PickExtremes()
    {
        var table = Grid(["3"], ["9"], ["1"], [""]);

        TableFormulaEvaluator.Evaluate(table, 3, 0, new TableFormulaField("=MAX(ABOVE)")).Should().Be("9");
        TableFormulaEvaluator.Evaluate(table, 3, 0, new TableFormulaField("=MIN(ABOVE)")).Should().Be("1");
    }

    [Fact]
    public void Range_StopsAtFirstNonNumericCell()
    {
        // A header label terminates the ABOVE range, matching Word: only the contiguous numbers count.
        var table = Grid(["Score"], ["10"], ["20"], [""]);
        var formula = new TableFormulaField("=SUM(ABOVE)");

        TableFormulaEvaluator.Evaluate(table, 3, 0, formula).Should().Be("30");
    }

    [Fact]
    public void NumericCells_ToleranceCurrencyAndThousands()
    {
        var table = Grid(["$1,200.50"], ["$2,000.00"], [""]);
        var formula = new TableFormulaField("=SUM(ABOVE)", "#,##0.00");

        TableFormulaEvaluator.Evaluate(table, 2, 0, formula).Should().Be("3,200.50");
    }

    [Fact]
    public void PlainArithmeticExpression_IsEvaluated()
    {
        var table = Grid([""]);
        var formula = new TableFormulaField("=2*(3+4)");

        TableFormulaEvaluator.Evaluate(table, 0, 0, formula).Should().Be("14");
    }

    [Fact]
    public void NumberFormat_AppliesPicture()
    {
        var table = Grid(["1234.5"], [""]);
        var formula = new TableFormulaField("=SUM(ABOVE)", "#,##0.00");

        TableFormulaEvaluator.Evaluate(table, 1, 0, formula).Should().Be("1,234.50");
    }

    [Fact]
    public void GeneralFormat_DropsTrailingZeros()
    {
        var table = Grid(["1.5"], ["1.5"], [""]);
        var formula = new TableFormulaField("=SUM(ABOVE)");

        TableFormulaEvaluator.Evaluate(table, 2, 0, formula).Should().Be("3");
    }

    [Fact]
    public void SyntaxError_ReturnsWordErrorMarker()
    {
        var table = Grid([""]);
        var formula = new TableFormulaField("=2 +* 3");

        TableFormulaEvaluator.Evaluate(table, 0, 0, formula).Should().Be("!Syntax Error");
    }

    [Fact]
    public void BareExpression_StripsLeadingEquals()
    {
        new TableFormulaField("=SUM(ABOVE)").BareExpression.Should().Be("SUM(ABOVE)");
        new TableFormulaField("SUM(LEFT)").BareExpression.Should().Be("SUM(LEFT)");
    }
}
