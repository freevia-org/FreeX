using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-72 fix-bucket "fml-dynarray" regression tests.
///
/// R72-formula-dynamic-array-4-1 / -4-2: SEQUENCE and RANDARRAY shared a single guard
/// (`rows &lt; 1 || cols &lt; 1`) that treated a zero-sized dimension the same as a negative
/// one, returning #VALUE! for both. Excel distinguishes them: a zero dimension is a
/// well-formed request for an empty array, which Excel rejects with #CALC! ("empty arrays
/// are not allowed"), while a negative dimension is a genuinely invalid argument and stays
/// #VALUE!.
///
/// R72-formula-math-rounding-4-1: CEILING/FLOOR's sign-mismatch guard multiplied the number
/// by the significance (`n * sig &lt; 0`) to detect an invalid sign combination. For tiny
/// subnormal-range operands the product can underflow to signed zero (e.g.
/// -5E-200 * 5E-200 = -2.5E-399, which underflows to -0.0), and `-0.0 &lt; 0` is false, so the
/// #NUM! was skipped and a finite (wrong) result was returned instead. Both scalars now use
/// a direct sign comparison (mirroring MROUND's already-correct guard) that can't underflow.
/// </summary>
public sealed class R72_DynamicArrayZeroDimensionAndRoundingUnderflowTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet() => new(SheetId.New(), "S");

    // ── SEQUENCE ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sequence_ZeroRows_ReturnsCalcError()
    {
        _eval.Evaluate("=SEQUENCE(0,1)", MakeSheet()).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Sequence_NegativeRows_StillReturnsValueError()
    {
        _eval.Evaluate("=SEQUENCE(-1,1)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sequence_SingleArgument_StillSpillsColumnVector()
    {
        // Sibling no-regression: the ordinary positive-dimension path is unaffected.
        var result = _eval.Evaluate("=SEQUENCE(3)", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    // ── RANDARRAY ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Randarray_ZeroRows_ReturnsCalcError()
    {
        _eval.Evaluate("=RANDARRAY(0,1)", MakeSheet()).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Randarray_NegativeRows_StillReturnsValueError()
    {
        _eval.Evaluate("=RANDARRAY(-1,1)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_2x2_StillSpillsFourNumbers()
    {
        // Sibling no-regression: normal positive dimensions still spill the expected shape.
        var result = _eval.Evaluate("=RANDARRAY(2,2)", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        foreach (var value in rv.Cells)
            value.Should().BeOfType<NumberValue>();
    }

    // ── CEILING / FLOOR subnormal-underflow sign check ──────────────────────────────────

    [Fact]
    public void Ceiling_TinySubnormalSignMismatch_ReturnsNumErrorInsteadOfUnderflowingToZero()
    {
        _eval.Evaluate("=CEILING(-5E-200,5E-200)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Floor_TinySubnormalSignMismatch_ReturnsNumErrorInsteadOfUnderflowingToZero()
    {
        _eval.Evaluate("=FLOOR(-5E-200,5E-200)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Ceiling_SameSignNegativeOperands_StillWorks()
    {
        _eval.Evaluate("=CEILING(-4.2,-1)", MakeSheet()).Should().Be(new NumberValue(-5));
    }

    [Fact]
    public void Ceiling_OrdinaryPositiveOperands_Unchanged()
    {
        _eval.Evaluate("=CEILING(4.2,1)", MakeSheet()).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Ceiling_ZeroSignificance_StillReturnsZero()
    {
        _eval.Evaluate("=CEILING(2.5,0)", MakeSheet()).Should().Be(new NumberValue(0));
    }
}
