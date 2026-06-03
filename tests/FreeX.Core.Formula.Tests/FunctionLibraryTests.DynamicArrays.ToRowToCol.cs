using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
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
}
