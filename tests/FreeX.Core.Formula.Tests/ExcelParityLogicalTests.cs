using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelParityLogicalTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=AND(TRUE,1)", true)]
    [InlineData("=AND(TRUE,0)", false)]
    [InlineData("=FALSE()", false)]
    [InlineData("=IF(TRUE,TRUE,FALSE)", true)]
    [InlineData("=IFS(FALSE,FALSE,TRUE,TRUE)", true)]
    [InlineData("=NOT(FALSE)", true)]
    [InlineData("=OR(FALSE,0,1)", true)]
    [InlineData("=SWITCH(2,1,FALSE,2,TRUE,FALSE)", true)]
    [InlineData("=TRUE()", true)]
    [InlineData("=XOR(TRUE,FALSE,TRUE)", false)]
    public void LogicalFunctions_MatchExcelBooleanResults(string formula, bool expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new BoolValue(expected));
    }

    [Fact]
    public void If_ShortCircuitsUnselectedBranch()
    {
        _eval.Evaluate("=IF(TRUE,1,1/0)", Sheet()).Should().Be(new NumberValue(1));
        _eval.Evaluate("=IF(FALSE,1/0,2)", Sheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void IfError_CatchesAllExcelErrorValues()
    {
        _eval.Evaluate("=IFERROR(1/0,\"fallback\")", Sheet()).Should().Be(new TextValue("fallback"));
        _eval.Evaluate("=IFERROR(NA(),\"fallback\")", Sheet()).Should().Be(new TextValue("fallback"));
        _eval.Evaluate("=IFERROR(42,\"fallback\")", Sheet()).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void IfNa_CatchesOnlyNa()
    {
        _eval.Evaluate("=IFNA(NA(),\"fallback\")", Sheet()).Should().Be(new TextValue("fallback"));
        _eval.Evaluate("=IFNA(1/0,\"fallback\")", Sheet()).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void AndOr_PropagateErrorsWhenNoResultDeterminesOutcomeFirst()
    {
        _eval.Evaluate("=AND(TRUE,NA())", Sheet()).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=OR(FALSE,NA())", Sheet()).Should().Be(ErrorValue.NA);
    }

    // R60-formula-logical-6-1: NOT(A1:A3) must broadcast element-wise across the range
    // (matching Excel's dynamic-array spill), not silently truncate to NOT(A1).
    [Fact]
    public void Not_MultiCellRange_BroadcastsElementWise()
    {
        var sheet = Sheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new BoolValue(true));

        AssertColumn(_eval.Evaluate("=NOT(A1:A3)", sheet), false, true, false);
    }

    // Sibling no-regression: a plain scalar NOT argument must keep working unchanged.
    [Fact]
    public void Not_ScalarArgument_StillWorks()
    {
        _eval.Evaluate("=NOT(TRUE)", Sheet()).Should().Be(new BoolValue(false));
        _eval.Evaluate("=NOT(FALSE)", Sheet()).Should().Be(new BoolValue(true));
    }

    private static void AssertColumn(ScalarValue value, params bool[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.At(row + 1, 1).Should().Be(new BoolValue(expected[row]));
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
