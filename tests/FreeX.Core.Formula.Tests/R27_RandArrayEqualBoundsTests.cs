using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R27-math-trig-remaining-2: RANDARRAY used a `min >= max` guard that spuriously rejected the
/// degenerate/constant case where min equals max, unlike the sibling RANDBETWEEN (which only
/// rejects `bottom > top`, tolerating equal bounds and returning that single value). Fixed to use
/// `min > max` so equal bounds produce a constant array, matching RANDBETWEEN and real Excel.
/// </summary>
public sealed class R27_RandArrayEqualBoundsTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Randarray_WholeNumber_MinEqualsMax_ReturnsConstantArray()
    {
        // Bug case: previously returned #VALUE! because of the `min >= max` guard.
        var result = _eval.Evaluate("=RANDARRAY(1,1,5,5,TRUE)", MakeSheet());

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(1);
        rv.At(1, 1).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Randarray_Decimal_MinEqualsMax_ReturnsConstantArray()
    {
        // Bug case: previously returned #VALUE! for non-integer equal bounds too.
        var result = _eval.Evaluate("=RANDARRAY(2,2,2.5,2.5)", MakeSheet());

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        foreach (var value in rv.Cells)
            value.Should().Be(new NumberValue(2.5));
    }

    [Fact]
    public void Randarray_MinGreaterThanMax_StillReturnsValueError()
    {
        // Sibling already-working case: a genuinely inverted range must remain an error.
        _eval.Evaluate("=RANDARRAY(1,1,10,1)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_WholeNumber_MinLessThanMax_StillReturnsWithinInclusiveBounds()
    {
        // Sibling already-working case: a normal (non-degenerate) whole-number range is unaffected.
        var result = _eval.Evaluate("=RANDARRAY(2,2,1,3,TRUE)", MakeSheet());

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        foreach (var value in rv.Cells)
        {
            var number = value.Should().BeOfType<NumberValue>().Subject.Value;
            number.Should().BeOneOf(1, 2, 3);
            number.Should().Be(Math.Truncate(number));
        }
    }

    private static Sheet MakeSheet(params (uint Row, uint Col, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
        return sheet;
    }
}
