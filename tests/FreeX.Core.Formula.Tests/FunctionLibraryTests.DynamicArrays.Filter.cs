using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Filter_ByBoolArray_ReturnsMatchingRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(10)), (2,1,new NumberValue(20)), (3,1,new NumberValue(30)),
            (1,2,new BoolValue(true)), (2,2,new BoolValue(false)), (3,2,new BoolValue(true)));
        var result = _eval.Evaluate("=FILTER(A1:A3,B1:B3)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(10));
        rv.Cells[1, 0].Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Filter_NoMatches_ReturnsIfEmptyArg()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(10)),
            (1,2,new BoolValue(false)));
        var result = _eval.Evaluate("=FILTER(A1:A1,B1:B1,\"none\")", sheet);
        result.Should().BeOfType<RangeValue>();
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new TextValue("none"));
    }

    [Fact]
    public void Filter_TreatsScalarArrayAndIncludeAsSingleCellArrays()
    {
        var included = _eval.Evaluate("=FILTER(5,TRUE)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;
        included.RowCount.Should().Be(1);
        included.ColCount.Should().Be(1);
        included.Cells[0, 0].Should().Be(new NumberValue(5));

        var empty = _eval.Evaluate("=FILTER(5,FALSE,\"empty\")", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;
        empty.RowCount.Should().Be(1);
        empty.ColCount.Should().Be(1);
        empty.Cells[0, 0].Should().Be(new TextValue("empty"));
    }

    [Fact]
    public void Filter_NoMatchesWithoutIfEmpty_ReturnsCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=FILTER(A1:A1,B1:B1)", sheet).Should().Be(new ErrorValue("#CALC!"));
        _eval.Evaluate("=ERROR.TYPE(FILTER(A1:A1,B1:B1))", sheet).Should().Be(new NumberValue(14));
    }

    [Fact]
    public void Filter_BlankIfEmptyArgument_ReturnsCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=FILTER(A1:A1,B1:B1,)", sheet).Should().Be(new ErrorValue("#CALC!"));
    }

    [Fact]
    public void Iferror_CatchesFilterNoMatchesCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=IFERROR(FILTER(A1:A1,B1:B1),\"fallback\")", sheet)
            .Should().Be(new TextValue("fallback"));
    }

    [Fact]
    public void Ifna_DoesNotCatchFilterNoMatchesCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=IFNA(FILTER(A1:A1,B1:B1),\"fallback\")", sheet)
            .Should().Be(new ErrorValue("#CALC!"));
    }

    [Fact]
    public void Choose_DoesNotEvaluateUnselectedFilterCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=CHOOSE(2,FILTER(A1:A1,B1:B1),42)", sheet)
            .Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Filter_MultiColumn_PreservesAllColumns()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new TextValue("A")), (1,3,new BoolValue(true)),
            (2,1,new NumberValue(2)), (2,2,new TextValue("B")), (2,3,new BoolValue(false)),
            (3,1,new NumberValue(3)), (3,2,new TextValue("C")), (3,3,new BoolValue(true)));
        var result = _eval.Evaluate("=FILTER(A1:B3,C1:C3)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 1].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Filter_DateTimeIncludeCell_TreatsDateSerialAsTrue()
    {
        var includeDate = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, new TextValue("keep")), (1, 2, includeDate),
            (2, 1, new TextValue("drop")), (2, 2, new NumberValue(0)));

        var result = _eval.Evaluate("=FILTER(A1:A2,B1:B2)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new TextValue("keep"));
    }

    [Fact]
    public void Filter_BlankIncludeCell_IsFalse()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("included")),
            (2, 1, new TextValue("blank")),
            (3, 1, new TextValue("excluded")),
            (1, 2, new BoolValue(true)),
            (3, 2, new BoolValue(false)));

        var result = _eval.Evaluate("=FILTER(A1:A3,B1:B3,\"empty\")", sheet);
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new TextValue("included"));
    }

    [Fact]
    public void Filter_TextIncludeCell_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("keep")), (1, 2, new TextValue("x")),
            (2, 1, new TextValue("drop")), (2, 2, new BoolValue(false)));

        _eval.Evaluate("=FILTER(A1:A2,B1:B2,\"empty\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Filter_MismatchedIncludeRows_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(20)),
            (2, 1, new NumberValue(30)), (2, 2, new NumberValue(40)),
            (1, 3, new BoolValue(true)));

        _eval.Evaluate("=FILTER(A1:B2,C1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Filter_HorizontalInclude_ReturnsMatchingColumns()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A1")), (1, 2, new TextValue("B1")), (1, 3, new TextValue("C1")),
            (2, 1, new TextValue("A2")), (2, 2, new TextValue("B2")), (2, 3, new TextValue("C2")),
            (3, 1, new BoolValue(true)), (3, 2, new BoolValue(false)), (3, 3, new BoolValue(true)));

        var result = _eval.Evaluate("=FILTER(A1:C2,A3:C3)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A1"));
        rv.Cells[0, 1].Should().Be(new TextValue("C1"));
        rv.Cells[1, 0].Should().Be(new TextValue("A2"));
        rv.Cells[1, 1].Should().Be(new TextValue("C2"));
    }

    [Fact]
    public void Filter_IncludeRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")),
            (1, 2, ErrorValue.NA));

        _eval.Evaluate("=FILTER(A1:A1,B1:B1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Filter_AcceptsArrayComparisonIncludeExpression()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));

        var rv = _eval.Evaluate("=FILTER(A1:A3,A1:A3>1)", sheet).Should().BeOfType<RangeValue>().Subject;

        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(1);
        rv.At(1, 1).Should().Be(new NumberValue(2));
        rv.At(2, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Filter_ArrayArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new BoolValue(true)));

        _eval.Evaluate("=FILTER(NA(),A1:A1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Filter_IncludeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("A")));

        _eval.Evaluate("=FILTER(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }
}
