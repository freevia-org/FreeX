using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityDatabaseTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=DSTDEV(A1:C5,\"Salary\",E1:E2)", 141.4213562373095)]
    [InlineData("=DSTDEVP(A1:C5,\"Salary\",E1:E2)", 100.0)]
    [InlineData("=DVAR(A1:C5,\"Salary\",E1:E2)", 20000.0)]
    [InlineData("=DVARP(A1:C5,\"Salary\",E1:E2)", 10000.0)]
    [InlineData("=DSTDEV(A1:C5,3,E1:E2)", 141.4213562373095)]
    [InlineData("=DVAR(A1:C5,3,E1:E2)", 20000.0)]
    public void StatisticalDatabaseFunctions_MatchExcelSampleAndPopulationSemantics(string formula, double expected)
    {
        var result = _eval.Evaluate(formula, DatabaseSheet());

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void DatabaseCriteriaRows_AreOrConditionsAndCriteriaColumnsAreAndConditions()
    {
        var sheet = DatabaseSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Age"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new NumberValue(40));

        _eval.Evaluate("=DVARP(A1:C5,\"Salary\",E1:E3)", sheet)
            .Should().Be(new NumberValue(10000));
    }

    [Theory]
    [InlineData("=DSUM(A1:C5,\"Salary\",G1:G2)", 1000.0)]
    [InlineData("=DCOUNT(A1:C5,\"Salary\",G1:G2)", 4.0)]
    public void DatabaseBlankCriteriaRow_MatchesAllRecords(string formula, double expected)
    {
        var sheet = DatabaseSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), new TextValue("Age"));

        _eval.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void DatabaseFunctions_IgnoreNonNumericFieldValuesForNumericAggregates()
    {
        var sheet = DatabaseSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new TextValue("n/a"));

        _eval.Evaluate("=DSTDEVP(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(0));
    }

    [Theory]
    [InlineData("=DSTDEV(A1:C5,\"Salary\",G1:G2)")]
    [InlineData("=DVAR(A1:C5,\"Salary\",G1:G2)")]
    public void SampleDatabaseFunctions_ReturnDiv0WhenFewerThanTwoNumericMatches(string formula)
    {
        var sheet = DatabaseSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), new TextValue("Age"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 7), new NumberValue(25));

        _eval.Evaluate(formula, sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Theory]
    [InlineData("=DSTDEVP(A1:C5,\"Salary\",G1:G2)")]
    [InlineData("=DVARP(A1:C5,\"Salary\",G1:G2)")]
    public void PopulationDatabaseFunctions_ReturnDiv0WhenNoNumericMatches(string formula)
    {
        var sheet = DatabaseSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), new TextValue("Age"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 7), new NumberValue(99));

        _eval.Evaluate(formula, sheet).Should().Be(ErrorValue.DivByZero);
    }

    private static Sheet DatabaseSheet()
    {
        var sheet = new Sheet(SheetId.New(), "S");

        Set(sheet, 1, 1, new TextValue("Name"));
        Set(sheet, 1, 2, new TextValue("Age"));
        Set(sheet, 1, 3, new TextValue("Salary"));
        Set(sheet, 2, 1, new TextValue("Alice"));
        Set(sheet, 2, 2, new NumberValue(30));
        Set(sheet, 2, 3, new NumberValue(100));
        Set(sheet, 3, 1, new TextValue("Bob"));
        Set(sheet, 3, 2, new NumberValue(25));
        Set(sheet, 3, 3, new NumberValue(200));
        Set(sheet, 4, 1, new TextValue("Carol"));
        Set(sheet, 4, 2, new NumberValue(30));
        Set(sheet, 4, 3, new NumberValue(300));
        Set(sheet, 5, 1, new TextValue("Dave"));
        Set(sheet, 5, 2, new NumberValue(40));
        Set(sheet, 5, 3, new NumberValue(400));

        Set(sheet, 1, 5, new TextValue("Age"));
        Set(sheet, 2, 5, new NumberValue(30));

        return sheet;
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
}
