using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
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
    public void Sequence_RowsColsOverMaterializedRangeCap_ReturnsValueError()
    {
        // R127: rows*cols must be checked against FormulaSafetyLimits.MaxMaterializedRangeCells
        // (16,777,216), not a stale hardcoded 1,000,000 -- see
        // R127_DynamicArrayGenerationCapMatchesSharedLimitTests for the corrected boundary.
        _eval.Evaluate("=SEQUENCE(100000000,1)", MakeSheet()).Should().Be(ErrorValue.Value,
            "rows×cols far beyond the shared materialized-range cap must still return #VALUE! rather than allocating a massive array");
    }

    [Fact]
    public void Sequence_1000By1001_NowUnderRaisedCap_ReturnsMatrix()
    {
        // R127: SEQUENCE(1000,1001) = 1,001,000 cells, which is over the OLD stale 1,000,000
        // hardcoded cap this function used to enforce independently of FormulaSafetyLimits, but
        // comfortably under the real shared MaxMaterializedRangeCells (16,777,216). This used to
        // wrongly return #VALUE! -- see R127_DynamicArrayGenerationCapMatchesSharedLimitTests.
        var result = _eval.Evaluate("=SEQUENCE(1000,1001)", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1000);
        rv.ColCount.Should().Be(1001);
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
}
