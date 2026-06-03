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
        Action act = () => _eval.Evaluate("={1,2;3}", MakeSheet());

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void Sequence_3Rows_ReturnsColumnVector()
    {
        var result = _eval.Evaluate("=SEQUENCE(3)", MakeSheet());
        result.Should().BeOfType<RangeValue>();
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sequence_2x3_ReturnsMatrix()
    {
        var result = _eval.Evaluate("=SEQUENCE(2,3)", MakeSheet());
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 2].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Sequence_BlankLeadingArguments_UseExcelDefaults()
    {
        var cols = _eval.Evaluate("=SEQUENCE(,2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        cols.RowCount.Should().Be(1);
        cols.ColCount.Should().Be(2);
        cols.Cells[0, 0].Should().Be(new NumberValue(1));
        cols.Cells[0, 1].Should().Be(new NumberValue(2));

        var start = _eval.Evaluate("=SEQUENCE(,,5)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        start.RowCount.Should().Be(1);
        start.ColCount.Should().Be(1);
        start.Cells[0, 0].Should().Be(new NumberValue(5));

        var step = _eval.Evaluate("=SEQUENCE(,,,2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        step.RowCount.Should().Be(1);
        step.ColCount.Should().Be(1);
        step.Cells[0, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sequence_WithStartAndStep_CountsByTwos()
    {
        var result = _eval.Evaluate("=SEQUENCE(4,1,0,2)", MakeSheet());
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new NumberValue(0));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(4));
        rv.Cells[3, 0].Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sequence_HugeRowsCols_ReturnsValueError()
    {
        _eval.Evaluate("=SEQUENCE(1000,1001)", MakeSheet()).Should().Be(ErrorValue.Value,
            "rows×cols > 1,000,000 must return #VALUE! rather than allocating a massive array");
    }


    [Fact]
    public void Sequence_AcceptsSpilledScalarControlArguments()
    {
        var result = _eval.Evaluate("=SEQUENCE(SEQUENCE(1,,2),SEQUENCE(1,,3),SEQUENCE(1,,5),SEQUENCE(1,,2))", MakeSheet());

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(5));
        rv.Cells[0, 1].Should().Be(new NumberValue(7));
        rv.Cells[0, 2].Should().Be(new NumberValue(9));
        rv.Cells[1, 0].Should().Be(new NumberValue(11));
        rv.Cells[1, 1].Should().Be(new NumberValue(13));
        rv.Cells[1, 2].Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Sequence_NonFiniteRows_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SEQUENCE(A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sequence_HugeFiniteDimensions_ReturnsValueError()
    {
        _eval.Evaluate("=SEQUENCE(2147483648)", MakeSheet()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=SEQUENCE(1,2147483648)", MakeSheet()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=SEQUENCE(-2147483648)", MakeSheet()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=SEQUENCE(1,-2147483648)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sequence_NonFiniteStart_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SEQUENCE(1,1,A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Sequence_NonFiniteStep_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SEQUENCE(1,1,1,A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Sequence_OverflowingGeneratedValue_ReturnsNumError()
    {
        _eval.Evaluate("=SEQUENCE(1,2,1E308,1E308)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Sequence_ColumnsError_PropagatesError() =>
        _eval.Evaluate("=SEQUENCE(2,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Sequence_StartError_PropagatesError() =>
        _eval.Evaluate("=SEQUENCE(2,1,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Sequence_StepError_PropagatesError() =>
        _eval.Evaluate("=SEQUENCE(2,1,1,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Sum_FlattensSequenceDynamicArrayResult()
    {
        _eval.Evaluate("=SUM(SEQUENCE(3,2,1,1))", MakeSheet())
            .Should().Be(new NumberValue(21));
    }

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
    public void Sumproduct_AcceptsArrayArithmeticExpression()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=SUMPRODUCT(A1:A3+1,B1:B3)", sheet).Should().Be(new NumberValue(200));
    }

    [Fact]
    public void Aggregate_FlattensDynamicArrayArithmeticResult()
    {
        _eval.Evaluate("=SUM(SEQUENCE(2,2)*2)", MakeSheet()).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Aggregate_FlattensDynamicArrayUnaryMinusResult()
    {
        _eval.Evaluate("=SUM(-SEQUENCE(2,2))", MakeSheet()).Should().Be(new NumberValue(-10));
    }

    [Fact]
    public void Aggregate_FlattensDynamicArrayPercentResult()
    {
        _eval.Evaluate("=SUM(SEQUENCE(2,2)%)", MakeSheet()).Should().Be(new NumberValue(0.1));
    }

    [Fact]
    public void DynamicArrayBinaryExpression_BroadcastsRowAndColumnVectors()
    {
        _eval.Evaluate("=SUM(SEQUENCE(3,1)+SEQUENCE(1,3))", MakeSheet()).Should().Be(new NumberValue(36));
    }

    [Fact]
    public void Sum_FlattensFilterDynamicArrayResult()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)), (1, 2, new BoolValue(true)),
            (2, 1, new NumberValue(1)), (2, 2, new BoolValue(false)),
            (3, 1, new NumberValue(2)), (3, 2, new BoolValue(true)));

        _eval.Evaluate("=SUM(FILTER(A1:A3,B1:B3))", sheet)
            .Should().Be(new NumberValue(5));
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

    [Fact]
    public void Sort_ArrayArgumentError_PropagatesError()
    {
        _eval.Evaluate("=SORT(NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sort_TreatsScalarArrayAsSingleCellArray()
    {
        var result = _eval.Evaluate("=SORT(5)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Sort_SingleColumn_SortsAscending()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(3)), (2,1,new NumberValue(1)), (3,1,new NumberValue(2)));
        var result = _eval.Evaluate("=SORT(A1:A3)", sheet);
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sort_SingleColumn_SortsDescending()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(3)), (2,1,new NumberValue(1)), (3,1,new NumberValue(2)));
        var result = _eval.Evaluate("=SORT(A1:A3,1,-1)", sheet);
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sort_MultiColumn_SortsBySecondColumn()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("B")), (1,2,new NumberValue(2)),
            (2,1,new TextValue("A")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));
        // SORT(A1:B3, 2, 1) → sort by col 2 ascending
        var result = _eval.Evaluate("=SORT(A1:B3,2,1)", sheet);
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 0].Should().Be(new TextValue("B"));
        rv.Cells[2, 0].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Sort_AcceptsSpilledScalarControlArguments()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("B")), (1,2,new NumberValue(2)),
            (2,1,new TextValue("A")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));

        var rv = _eval.Evaluate("=SORT(A1:B3,SEQUENCE(1,,2),SEQUENCE(1,,-1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new TextValue("B"));
        rv.Cells[2, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sort_ZeroSortIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (2,1,new NumberValue(2)));
        _eval.Evaluate("=SORT(A1:A2,0)", sheet).Should().Be(ErrorValue.Value,
            "sort_index=0 is invalid (1-based) and must not cause an IndexOutOfRangeException");
    }

    [Fact]
    public void Sort_OutOfBoundsRowSortIndex_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)));

        _eval.Evaluate("=SORT(A1:B2,3)", sheet).Should().Be(ErrorValue.Value,
            "row-oriented SORT sort_index must refer to an existing column");
    }

    [Fact]
    public void Sort_OutOfBoundsColumnSortIndex_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(2)));

        _eval.Evaluate("=SORT(A1:B2,3,1,TRUE)", sheet).Should().Be(ErrorValue.Value,
            "column-oriented SORT sort_index must refer to an existing row");
    }

    [Fact]
    public void Sort_InvalidSortOrder_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)), (2,1,new NumberValue(1)));

        _eval.Evaluate("=SORT(A1:A2,1,0)", sheet).Should().Be(ErrorValue.Value,
            "Excel only accepts 1 or -1 for SORT sort_order");
    }

    [Fact]
    public void Sortby_SortsRowsBySeparateKeyArray()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(2)));

        var result = _eval.Evaluate("=SORTBY(A1:A3,B1:B3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new TextValue("B"));
        rv.Cells[1, 0].Should().Be(new TextValue("C"));
        rv.Cells[2, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sortby_OmittedSortOrder_DefaultsAscending()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(2)));

        var result = _eval.Evaluate("=SORTBY(A1:A3,B1:B3,)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.Cells[0, 0].Should().Be(new TextValue("B"));
        rv.Cells[1, 0].Should().Be(new TextValue("C"));
        rv.Cells[2, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sortby_TreatsScalarArrayAndKeyAsSingleCellArrays()
    {
        var result = _eval.Evaluate("=SORTBY(5,1)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Sortby_SortsColumnsBySeparateKeyArrayDescending()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(3)), (2,3,new NumberValue(2)));

        var result = _eval.Evaluate("=SORTBY(A1:C1,A2:C2,-1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new TextValue("B"));
        rv.Cells[0, 1].Should().Be(new TextValue("C"));
        rv.Cells[0, 2].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sortby_AcceptsSpilledScalarSortOrder()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(2)));

        var rv = _eval.Evaluate("=SORTBY(A1:A3,B1:B3,SEQUENCE(1,,-1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 0].Should().Be(new TextValue("C"));
        rv.Cells[2, 0].Should().Be(new TextValue("B"));
    }

    [Fact]
    public void Sortby_SortOrderError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(2)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)));

        _eval.Evaluate("=SORTBY(A1:A2,B1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sortby_RangeInSortOrderSlot_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)), (2,3,new NumberValue(4)));

        _eval.Evaluate("=SORTBY(A1:A2,B1:B2,C1:C2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sortby_MismatchedKeyShape_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (2,1,new TextValue("B")),
            (1,2,new NumberValue(1)));

        _eval.Evaluate("=SORTBY(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value,
            "SORTBY key arrays must align to either the sorted rows or sorted columns");
    }

    [Fact]
    public void Take_PositiveRowsAndColumns_ReturnsTopLeftSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=TAKE(A1:C3,2,2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Take_TreatsScalarArrayAsSingleCellArray()
    {
        var taken = _eval.Evaluate("=TAKE(5,1)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;
        taken.RowCount.Should().Be(1);
        taken.ColCount.Should().Be(1);
        taken.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void TakeAndDrop_AcceptSpilledScalarSliceCounts()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var taken = _eval.Evaluate("=TAKE(A1:C3,SEQUENCE(1,,2),SEQUENCE(1,,2))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        taken.RowCount.Should().Be(2);
        taken.ColCount.Should().Be(2);
        taken.Cells[1, 1].Should().Be(new NumberValue(5));

        var dropped = _eval.Evaluate("=DROP(A1:C3,SEQUENCE(1,,1),SEQUENCE(1,,1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        dropped.RowCount.Should().Be(2);
        dropped.ColCount.Should().Be(2);
        dropped.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void DynamicArrayFunctions_AcceptSpilledScalarControlArguments()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(1)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)), (2,3,new NumberValue(3)),
            (1,4,new NumberValue(5)), (1,5,new NumberValue(6)), (1,6,new NumberValue(7)));

        var toCol = _eval.Evaluate("=TOCOL(A1:B2,,SEQUENCE(1,,1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        toCol.Cells[0, 0].Should().Be(new NumberValue(1));
        toCol.Cells[1, 0].Should().Be(new NumberValue(3));
        toCol.Cells[2, 0].Should().Be(new NumberValue(2));
        toCol.Cells[3, 0].Should().Be(new NumberValue(4));

        var wrapped = _eval.Evaluate("=WRAPROWS(D1:F1,SEQUENCE(1,,2))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        wrapped.RowCount.Should().Be(2);
        wrapped.ColCount.Should().Be(2);
        wrapped.Cells[1, 0].Should().Be(new NumberValue(7));

        var expanded = _eval.Evaluate("=EXPAND(A1:A1,SEQUENCE(1,,2),SEQUENCE(1,,2),0)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        expanded.RowCount.Should().Be(2);
        expanded.ColCount.Should().Be(2);
        expanded.Cells[1, 1].Should().Be(new NumberValue(0));

        var uniqueByColumn = _eval.Evaluate("=UNIQUE(A1:C2,SEQUENCE(1,,1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        uniqueByColumn.RowCount.Should().Be(2);
        uniqueByColumn.ColCount.Should().Be(2);
        uniqueByColumn.Cells[0, 0].Should().Be(new NumberValue(1));
        uniqueByColumn.Cells[0, 1].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Take_OmittedRows_TakesRequestedColumnsFromAllRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=TAKE(A1:C3,,2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[2, 1].Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Drop_OmittedRows_DropsRequestedColumnsFromAllRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=DROP(A1:C3,,1)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Take_NegativeRowsAndColumns_ReturnsBottomRightSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=TAKE(A1:C3,-2,-2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(5));
        rv.Cells[0, 1].Should().Be(new NumberValue(6));
        rv.Cells[1, 0].Should().Be(new NumberValue(8));
        rv.Cells[1, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Drop_PositiveRowsAndColumns_RemovesTopLeftSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=DROP(A1:C3,1,1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(5));
        rv.Cells[0, 1].Should().Be(new NumberValue(6));
        rv.Cells[1, 0].Should().Be(new NumberValue(8));
        rv.Cells[1, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Drop_NegativeRowsAndColumns_RemovesBottomRightSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=DROP(A1:C3,-1,-1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Take_ZeroRows_ReturnsCalcError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=TAKE(A1:A1,0)", sheet).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Drop_ZeroRowsOrColumns_ReturnsCalcError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        _eval.Evaluate("=DROP(A1:B1,0)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(A1:B1,,0)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(5,0)", MakeSheet()).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void TakeAndDrop_HugeFiniteSliceCount_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (2,1,new NumberValue(2)));

        _eval.Evaluate("=TAKE(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=TAKE(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=TAKE(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DROP(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DROP(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DROP(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Drop_AllRows_ReturnsCalcError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=DROP(A1:A1,1)", sheet).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Chooserows_ReordersRowsAndAllowsRepeats()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:B3,3,1,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new TextValue("A"));
        rv.Cells[2, 0].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Chooserows_NegativeIndexSelectsFromEnd()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")),
            (2,1,new TextValue("B")),
            (3,1,new TextValue("C")));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:A3,-1,-3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Chooserows_AcceptsDynamicArrayRowIndexes()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:B3,VSTACK(3,1))", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[0, 1].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Choosecols_ReordersColumnsAndAllowsRepeats()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(2)), (2,3,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:C2,3,1,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[0, 1].Should().Be(new TextValue("A"));
        rv.Cells[0, 2].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Choosecols_NegativeIndexSelectsFromEnd()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:C1,-1,-3)", sheet);

        var rv = (RangeValue)result;
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[0, 1].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Choosecols_AcceptsDynamicArrayColumnIndexes()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(2)), (2,3,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:C2,HSTACK(1,3))", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[0, 1].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 1].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void ChooserowsAndChoosecols_TreatScalarArrayAsSingleCellArray()
    {
        var rows = _eval.Evaluate("=CHOOSEROWS(5,1)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        rows.RowCount.Should().Be(1);
        rows.ColCount.Should().Be(1);
        rows.Cells[0, 0].Should().Be(new NumberValue(5));

        var cols = _eval.Evaluate("=CHOOSECOLS(\"x\",1)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        cols.RowCount.Should().Be(1);
        cols.ColCount.Should().Be(1);
        cols.Cells[0, 0].Should().Be(new TextValue("x"));
    }

    [Fact]
    public void Chooserows_ZeroIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=CHOOSEROWS(A1:A1,0)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Choosecols_OutOfRangeIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=CHOOSECOLS(A1:A1,2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void ChooserowsAndChoosecols_HugeFiniteIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new TextValue("A")), (2,1,new TextValue("B")));

        _eval.Evaluate("=CHOOSEROWS(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSEROWS(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSEROWS(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSECOLS(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSECOLS(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSECOLS(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Vstack_AppendsRowsAndPadsShorterArraysWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")),
            (2,1,new TextValue("C")), (2,2,new TextValue("D")),
            (1,3,new TextValue("E")));

        var result = _eval.Evaluate("=VSTACK(A1:B2,C1:C1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new TextValue("D"));
        rv.Cells[2, 0].Should().Be(new TextValue("E"));
        rv.Cells[2, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hstack_AppendsColumnsAndPadsShorterArraysWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (2,1,new TextValue("B")),
            (1,2,new TextValue("C")));

        var result = _eval.Evaluate("=HSTACK(A1:A2,B1:B1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 0].Should().Be(new TextValue("B"));
        rv.Cells[0, 1].Should().Be(new TextValue("C"));
        rv.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void VstackAndHstack_TreatScalarArgumentsAsSingleCellArrays()
    {
        var vstack = _eval.Evaluate("=VSTACK(1,\"two\",TRUE)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        vstack.RowCount.Should().Be(3);
        vstack.ColCount.Should().Be(1);
        vstack.Cells[0, 0].Should().Be(new NumberValue(1));
        vstack.Cells[1, 0].Should().Be(new TextValue("two"));
        vstack.Cells[2, 0].Should().Be(new BoolValue(true));

        var hstack = _eval.Evaluate("=HSTACK(1,\"two\",TRUE)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        hstack.RowCount.Should().Be(1);
        hstack.ColCount.Should().Be(3);
        hstack.Cells[0, 0].Should().Be(new NumberValue(1));
        hstack.Cells[0, 1].Should().Be(new TextValue("two"));
        hstack.Cells[0, 2].Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Vstack_ScalarErrorArgument_SpillsErrorAsCell()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=VSTACK(A1:A1,NA())", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hstack_ScalarErrorArgument_SpillsErrorAsCell()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=HSTACK(A1:A1,NA())", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[0, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Torow_DefaultScan_ReturnsSingleRowByRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        var result = _eval.Evaluate("=TOROW(A1:B2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(4);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[0, 2].Should().Be(new NumberValue(3));
        rv.Cells[0, 3].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Tocol_ScanByColumn_ReturnsSingleColumnByColumns()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        var result = _eval.Evaluate("=TOCOL(A1:B2,0,TRUE)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(4);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(3));
        rv.Cells[2, 0].Should().Be(new NumberValue(2));
        rv.Cells[3, 0].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void TorowAndTocol_TreatScalarArgumentAsSingleCellArray()
    {
        var row = _eval.Evaluate("=TOROW(\"x\")", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        row.RowCount.Should().Be(1);
        row.ColCount.Should().Be(1);
        row.Cells[0, 0].Should().Be(new TextValue("x"));

        var col = _eval.Evaluate("=TOCOL(42)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        col.RowCount.Should().Be(1);
        col.ColCount.Should().Be(1);
        col.Cells[0, 0].Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Torow_IgnoreBlanksAndErrors_RemovesBoth()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,ErrorValue.NA),
            (2,2,new NumberValue(2)));

        var result = _eval.Evaluate("=TOROW(A1:B2,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void TorowAndTocol_IgnoreBlanks_KeepsZeroLengthText()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("")),
            (1,3,new NumberValue(2)));

        var row = _eval.Evaluate("=TOROW(A1:C1,1)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        row.RowCount.Should().Be(1);
        row.ColCount.Should().Be(2);
        row.Cells[0, 0].Should().Be(new TextValue(""));
        row.Cells[0, 1].Should().Be(new NumberValue(2));

        var col = _eval.Evaluate("=TOCOL(A1:C1,1)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        col.RowCount.Should().Be(2);
        col.ColCount.Should().Be(1);
        col.Cells[0, 0].Should().Be(new TextValue(""));
        col.Cells[1, 0].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void TorowAndTocol_AllValuesIgnored_ReturnCalcError()
    {
        var sheet = MakeSheet((1, 1, ErrorValue.NA));

        _eval.Evaluate("=TOROW(A1:B1,3)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=TOCOL(A1:B1,3)", sheet).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void TorowAndTocol_IgnoreScalarErrorsLikeSingleCellArrays()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=TOROW(NA(),2)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=TOCOL(NA(),2)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=TOROW(NA())", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=TOCOL(NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Tocol_InvalidIgnoreMode_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=TOCOL(A1:A1,4)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Wraprows_WrapsRowVectorAndPadsWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (1,4,new NumberValue(4)), (1,5,new NumberValue(5)));

        var result = _eval.Evaluate("=WRAPROWS(A1:E1,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 2].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
        rv.Cells[1, 2].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wraprows_UsesCustomPadValue()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)));

        var result = _eval.Evaluate("=WRAPROWS(A1:C1,2,\"x\")", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[1, 0].Should().Be(new NumberValue(3));
        rv.Cells[1, 1].Should().Be(new TextValue("x"));
    }

    [Fact]
    public void WraprowsAndWrapcols_PadWithOneCellRange_UsesScalarValue()
    {
        var rowSheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(9)));
        var rows = _eval.Evaluate("=WRAPROWS(A1:A1,2,B1:B1)", rowSheet)
            .Should().BeOfType<RangeValue>().Subject;

        rows.RowCount.Should().Be(1);
        rows.ColCount.Should().Be(2);
        rows.Cells[0, 0].Should().Be(new NumberValue(1));
        rows.Cells[0, 1].Should().Be(new NumberValue(9));

        var colSheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (1, 2, new TextValue("z")));
        var cols = _eval.Evaluate("=WRAPCOLS(A1:A1,2,B1:B1)", colSheet)
            .Should().BeOfType<RangeValue>().Subject;

        cols.RowCount.Should().Be(2);
        cols.ColCount.Should().Be(1);
        cols.Cells[0, 0].Should().Be(new TextValue("a"));
        cols.Cells[1, 0].Should().Be(new TextValue("z"));
    }

    [Fact]
    public void WraprowsAndWrapcols_OmittedPadWith_DefaultsToNA()
    {
        var rowSheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)));
        var rows = _eval.Evaluate("=WRAPROWS(A1:C1,2,)", rowSheet)
            .Should().BeOfType<RangeValue>().Subject;

        rows.Cells[1, 1].Should().Be(ErrorValue.NA);

        var colSheet = MakeSheet(
            (1,1,new NumberValue(1)), (2,1,new NumberValue(2)), (3,1,new NumberValue(3)));
        var cols = _eval.Evaluate("=WRAPCOLS(A1:A3,2,)", colSheet)
            .Should().BeOfType<RangeValue>().Subject;

        cols.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wrapcols_WrapsColumnVectorByColumns()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),
            (2,1,new NumberValue(2)),
            (3,1,new NumberValue(3)),
            (4,1,new NumberValue(4)),
            (5,1,new NumberValue(5)));

        var result = _eval.Evaluate("=WRAPCOLS(A1:A5,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
        rv.Cells[0, 1].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
        rv.Cells[2, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void WraprowsAndWrapcols_TreatScalarArgumentAsOneItemVector()
    {
        var rows = _eval.Evaluate("=WRAPROWS(1,2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        rows.RowCount.Should().Be(1);
        rows.ColCount.Should().Be(2);
        rows.Cells[0, 0].Should().Be(new NumberValue(1));
        rows.Cells[0, 1].Should().Be(ErrorValue.NA);

        var cols = _eval.Evaluate("=WRAPCOLS(\"x\",2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        cols.RowCount.Should().Be(2);
        cols.ColCount.Should().Be(1);
        cols.Cells[0, 0].Should().Be(new TextValue("x"));
        cols.Cells[1, 0].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wraprows_InvalidWrapCount_ReturnsNumError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=WRAPROWS(A1:A1,0)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void WraprowsAndWrapcols_WrapCountError_PropagatesBeforeArrayShapeValidation()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        _eval.Evaluate("=WRAPROWS(A1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=WRAPCOLS(A1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void WraprowsAndWrapcols_HugeFiniteWrapCount_ReturnsNumError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=WRAPROWS(A1:A1,2147483648)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WRAPROWS(A1:A1,-2147483648)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WRAPCOLS(A1:A1,2147483648)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WRAPCOLS(A1:A1,-2147483648)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Wrapcols_TwoDimensionalArray_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        _eval.Evaluate("=WRAPCOLS(A1:B2,2)", sheet).Should().Be(ErrorValue.Value);
    }

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
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=EXPAND(A1,1000001,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact] public void Sort_SortIndexError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=SORT(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Sort_SortOrderError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=SORT(A1:A1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Sort_ByColError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=SORT(A1:A1,1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Unique_SingleColumn_RemovesDuplicates()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (2,1,new NumberValue(2)),
            (3,1,new NumberValue(1)), (4,1,new NumberValue(3)));
        var result = _eval.Evaluate("=UNIQUE(A1:A4)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Unique_TreatsScalarArrayAsSingleCellArray()
    {
        var result = _eval.Evaluate("=UNIQUE(5)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Unique_ExactlyOnce_ReturnsOnlySingletons()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (2,1,new NumberValue(2)),
            (3,1,new NumberValue(1)), (4,1,new NumberValue(3)));
        // UNIQUE(A1:A4, FALSE, TRUE) → only values appearing exactly once
        var result = _eval.Evaluate("=UNIQUE(A1:A4,FALSE,TRUE)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Unique_ExactlyOnceWithNoSingletons_ReturnsCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(2)), (4, 1, new NumberValue(2)));

        _eval.Evaluate("=UNIQUE(A1:A4,FALSE,TRUE)", sheet)
            .Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Unique_MultiColumn_DeduplicatesRows()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)),
            (3,1,new TextValue("A")), (3,2,new NumberValue(1)));
        var result = _eval.Evaluate("=UNIQUE(A1:B3)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
    }


    [Fact]
    public void Unique_DistinguishesScalarTypesWhenDeduplicating()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new TextValue("1")),
            (3, 1, new BoolValue(true)),
            (4, 1, new TextValue("TRUE")),
            (5, 1, new NumberValue(1)));

        var result = _eval.Evaluate("=UNIQUE(A1:A5)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(4);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new TextValue("1"));
        rv.Cells[2, 0].Should().Be(new BoolValue(true));
        rv.Cells[3, 0].Should().Be(new TextValue("TRUE"));
    }

    [Fact] public void Unique_ByColError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=UNIQUE(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Unique_ArrayArgumentError_PropagatesError()
    {
        _eval.Evaluate("=UNIQUE(NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Unique_ExactlyOnceError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=UNIQUE(A1:A1,FALSE,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Transpose_Range_ReturnsTransposedMatrix()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));

        var result = _eval.Evaluate("=TRANSPOSE(A1:C2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.At(1, 1).Should().Be(new NumberValue(1));
        rv.At(1, 2).Should().Be(new NumberValue(4));
        rv.At(3, 1).Should().Be(new NumberValue(3));
        rv.At(3, 2).Should().Be(new NumberValue(6));
    }
}
