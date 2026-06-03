using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FormulaEvaluatorTests
{
    // ── THE canonical first test (§9) ──

    [Fact]
    public void SumOfRange_ReturnsExpectedTotal()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(3));

        var result = _evaluator.Evaluate("=SUM(A1:A3)", sheet);

        result.Should().Be(new NumberValue(6));
    }

    // ── SUM function ──

    [Fact]
    public void Sum_SingleValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        _evaluator.Evaluate("=SUM(A1)", sheet).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Sum_MultipleArgs()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=SUM(1,2,3)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sum_DirectNumericText_IncludesValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=SUM(\"4\",2)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sum_DirectNonNumericText_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=SUM(\"hello\",2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sum_IgnoresText()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(5));
        _evaluator.Evaluate("=SUM(A1:A3)", sheet).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Sum_RangeLogical_IgnoresValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        _evaluator.Evaluate("=SUM(A1:A2)", sheet).Should().Be(new NumberValue(5));
    }

    // ── AVERAGE function ──

    [Fact]
    public void Average_OfRange()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(2));
        sheet.SetCell(a2, new NumberValue(4));
        sheet.SetCell(a3, new NumberValue(6));
        _evaluator.Evaluate("=AVERAGE(A1:A3)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Average_DirectNumericText_IncludesValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=AVERAGE(\"4\",2)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Average_RangeLogical_IgnoresValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        _evaluator.Evaluate("=AVERAGE(A1:A2)", sheet).Should().Be(new NumberValue(5));
    }

    // ── MIN / MAX ──

    [Fact]
    public void Min_OfRange()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(8));
        _evaluator.Evaluate("=MIN(A1:A3)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Max_OfRange()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(8));
        _evaluator.Evaluate("=MAX(A1:A3)", sheet).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Min_DirectNumericText_IncludesValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=MIN(\"4\",2)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Max_DirectNumericText_IncludesValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=MAX(\"4\",2)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Min_RangeLogical_IgnoresValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        _evaluator.Evaluate("=MIN(A1:A2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Max_RangeLogical_IgnoresValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(0.5));
        _evaluator.Evaluate("=MAX(A1:A2)", sheet).Should().Be(new NumberValue(0.5));
    }

    // ── COUNT / COUNTA ──

    [Fact]
    public void Count_CountsNumbers()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new TextValue("hi"));
        sheet.SetCell(a3, new NumberValue(3));
        _evaluator.Evaluate("=COUNT(A1:A3)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Count_RangeLogical_IgnoresValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        _evaluator.Evaluate("=COUNT(A1:A2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Count_DirectNumericText_CountsValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=COUNT(\"4\",2)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Count_DirectNonNumericText_IgnoresValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=COUNT(\"hello\",2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Count_ErrorArgument_PropagatesError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=COUNT(NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void RangeOnlyAggregates_WithDirectAndNamedRanges_PreserveRangeCoercionSemantics()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("ignored"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new DateTimeValue(10));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue(""));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(4));
        workbook.DefineNamedRange("OtherInputs", new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 5, 2)));

        _evaluator.Evaluate("=SUM(A1:A5,OtherInputs)", sheet, workbook).Should().Be(new NumberValue(19));
        _evaluator.Evaluate("=AVERAGE(A1:A5,OtherInputs)", sheet, workbook).Should().Be(new NumberValue(4.75));
        _evaluator.Evaluate("=MIN(A1:A5,OtherInputs)", sheet, workbook).Should().Be(new NumberValue(2));
        _evaluator.Evaluate("=MAX(A1:A5,OtherInputs)", sheet, workbook).Should().Be(new NumberValue(10));
        _evaluator.Evaluate("=COUNT(A1:A5,OtherInputs)", sheet, workbook).Should().Be(new NumberValue(4));
        _evaluator.Evaluate("=COUNTBLANK(OtherInputs)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void RangeOnlyAggregates_PreserveErrorsAndFallbackCases()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.NA);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        workbook.DefineNamedRange("ProblemInputs", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1)));

        _evaluator.Evaluate("=SUM(ProblemInputs,A2:A2)", sheet, workbook).Should().Be(ErrorValue.NA);
        _evaluator.Evaluate("=AVERAGE(ProblemInputs,A2:A2)", sheet, workbook).Should().Be(ErrorValue.NA);
        _evaluator.Evaluate("=MIN(ProblemInputs,A2:A2)", sheet, workbook).Should().Be(ErrorValue.NA);
        _evaluator.Evaluate("=MAX(ProblemInputs,A2:A2)", sheet, workbook).Should().Be(ErrorValue.NA);
        _evaluator.Evaluate("=COUNT(ProblemInputs,A2:A2)", sheet, workbook).Should().Be(new NumberValue(1));

        _evaluator.Evaluate("=SUM(A2:A2,\"4\")", sheet, workbook).Should().Be(new NumberValue(6));
        _evaluator.Evaluate("=SUM(MissingInputs)", sheet, workbook).Should().Be(ErrorValue.Name);
    }

    [Fact]
    public void CountA_CountsNonBlanks()
    {
        var (sheet, a1, a2, _) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new TextValue("hi"));
        // a3 is blank
        _evaluator.Evaluate("=COUNTA(A1:A3)", sheet).Should().Be(new NumberValue(2));
    }
}
