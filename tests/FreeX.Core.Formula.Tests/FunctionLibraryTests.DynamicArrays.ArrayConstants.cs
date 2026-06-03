using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void ArrayConstant_CanBeSummedAsInlineRow()
    {
        _eval.Evaluate("=SUM({1,2,3})", MakeSheet())
            .Should().Be(new NumberValue(6));
    }

    [Fact]
    public void ArrayConstant_CanBeIndexedAsTwoDimensionalLiteral()
    {
        _eval.Evaluate("=INDEX({1,2;3,4},2,1)", MakeSheet())
            .Should().Be(new NumberValue(3));
    }

    [Fact]
    public void ArrayConstant_SupportsTextBooleanAndErrorLiterals()
    {
        var result = _eval.Evaluate("={\"x\",TRUE,#N/A}", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(3);
        result.Cells[0, 0].Should().Be(new TextValue("x"));
        result.Cells[0, 1].Should().Be(new BoolValue(true));
        result.Cells[0, 2].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void ArrayConstant_RejectsRaggedRows()
    {
        // A ragged array constant is rejected as an error value (Excel returns #VALUE!), not by
        // throwing out of Evaluate. The strict parser still throws — see ParserSafetyTests — but the
        // Evaluate entry point maps a parse failure to #VALUE!, matching a recalc.
        _eval.Evaluate("={1,2;3}", MakeSheet()).Should().Be(ErrorValue.Value);
    }
}
