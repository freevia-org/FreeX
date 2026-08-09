using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Expand_LargerRowsAndColumns_PadsWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")),
            (2,1,new TextValue("C")), (2,2,new TextValue("D")));

        var result = _eval.Evaluate("=EXPAND(A1:B2,3,4)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(4);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new TextValue("D"));
        rv.Cells[0, 2].Should().Be(ErrorValue.NA);
        rv.Cells[2, 0].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_UsesCustomPadValue()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=EXPAND(A1:A1,2,2,\"x\")", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new TextValue("x"));
        rv.Cells[1, 0].Should().Be(new TextValue("x"));
    }

    [Fact]
    public void Expand_PadWithOneCellRange_UsesScalarValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(9)));

        var result = _eval.Evaluate("=EXPAND(A1:A1,2,2,B1:B1)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(9));
        rv.Cells[1, 0].Should().Be(new NumberValue(9));
        rv.Cells[1, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Expand_OmittedPadWith_DefaultsToNA()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=EXPAND(A1:A1,2,2,)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.Cells[0, 1].Should().Be(ErrorValue.NA);
        rv.Cells[1, 0].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_TreatsScalarArgumentAsSingleCellArray()
    {
        var result = _eval.Evaluate("=EXPAND(1,2,2)", MakeSheet());

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(ErrorValue.NA);
        rv.Cells[1, 0].Should().Be(ErrorValue.NA);
        rv.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_RowsOnly_KeepsOriginalColumnCount()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        var result = _eval.Evaluate("=EXPAND(A1:B1,2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[1, 0].Should().Be(ErrorValue.NA);
        rv.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_OmittedRowsArgument_KeepsOriginalRowCount()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        var result = _eval.Evaluate("=EXPAND(A1:B1,,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[0, 2].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_SmallerTarget_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        _eval.Evaluate("=EXPAND(A1:B1,1,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Expand_RowOrColumnError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        _eval.Evaluate("=EXPAND(A1:B1,NA())", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=EXPAND(A1:B1,2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_TooManyCells_ReturnsValueError()
    {
        // R127: 100,000,001 cells is far beyond FormulaSafetyLimits.MaxMaterializedRangeCells
        // (16,777,216) and must still return #VALUE!.
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=EXPAND(A1,100000001,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Expand_1000001Cells_NowUnderRaisedCap_ReturnsExpandedRange()
    {
        // R127: EXPAND used to enforce a hardcoded 1,000,000-cell cap independent of
        // FormulaSafetyLimits.MaxMaterializedRangeCells (now 16,777,216), so this legitimate
        // 1,000,001-cell expansion used to wrongly return #VALUE!. See
        // R127_DynamicArrayGenerationCapMatchesSharedLimitTests for the full family.
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=EXPAND(A1,1000001,1)", sheet);
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1000001);
        rv.ColCount.Should().Be(1);
    }
}
