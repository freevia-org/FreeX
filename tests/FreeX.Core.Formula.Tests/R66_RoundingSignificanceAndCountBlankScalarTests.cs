using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-66 fix-bucket "fml-round-stat" regression tests.
///
/// R66-formula-rounding-int-6-1: ISO.CEILING/CEILING.PRECISE/CEILING.MATH/FLOOR.PRECISE/FLOOR.MATH
/// conflated an OMITTED significance argument (no second argument at all) with a PRESENT-but-BLANK
/// one (an explicit trailing comma, or a reference to an empty cell). Excel coerces a present-but-
/// blank significance to 0 (so the result is 0), but only defaults to 1 when the argument slot is
/// truly omitted.
///
/// R66-formula-statistics-basic-6-1: COUNTBLANK returned #VALUE! for any non-RangeValue argument
/// (a bare scalar, a literal "", or a function call like INDEX(...) that resolves to a scalar)
/// instead of wrapping it into a 1x1 range like its StructuredRangeFunction siblings
/// (LARGE/SMALL/PERCENTILE/...).
/// </summary>
public sealed class R66_RoundingSignificanceAndCountBlankScalarTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook Workbook, Sheet Sheet) MakeWb(params (uint row, uint col, ScalarValue val)[] cells)
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, r, c), v);
        return (wb, sheet);
    }

    // ── Present-but-blank significance -> 0 (not the omitted-slot default of 1) ──────────

    [Fact]
    public void CeilingMath_PresentBlankSignificance_ReturnsZero()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CEILING.MATH(4.3,)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void IsoCeiling_PresentBlankSignificance_ReturnsZero()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISO.CEILING(4.3,)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void CeilingPrecise_PresentBlankSignificance_ReturnsZero()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CEILING.PRECISE(4.3,)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void FloorPrecise_PresentBlankSignificance_ReturnsZero()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=FLOOR.PRECISE(4.3,)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void FloorMath_PresentBlankSignificance_ReturnsZero()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=FLOOR.MATH(4.3,)", sheet, wb).Should().Be(new NumberValue(0));
    }

    // ── Sibling no-regression: omitted significance slot still defaults to 1 / 2 works ──

    [Fact]
    public void CeilingMath_OmittedSignificance_StillDefaultsToOne()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CEILING.MATH(4.3)", sheet, wb).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void CeilingMath_ExplicitSignificance_StillUnaffected()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CEILING.MATH(4.3,2)", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void IsoCeiling_OmittedSignificance_StillDefaultsToOne()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISO.CEILING(4.3)", sheet, wb).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void FloorMath_OmittedSignificance_StillDefaultsToOne()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=FLOOR.MATH(4.3)", sheet, wb).Should().Be(new NumberValue(4));
    }

    // ── COUNTBLANK on scalar / non-range arguments ────────────────────────────────────────

    [Fact]
    public void Countblank_NumericScalar_ReturnsZeroInsteadOfValueError()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=COUNTBLANK(5)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Countblank_EmptyStringLiteral_ReturnsOne()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=COUNTBLANK(\"\")", sheet, wb).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countblank_IndexResolvingToBlankCell_ReturnsOne()
    {
        // A2 is blank; INDEX(A1:A3,2) resolves to a scalar (not a RangeValue).
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (3, 1, new NumberValue(3)));

        _eval.Evaluate("=COUNTBLANK(INDEX(A1:A3,2))", sheet, wb).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countblank_IndexResolvingToPopulatedCell_ReturnsZero()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(7)),
            (3, 1, new NumberValue(3)));

        _eval.Evaluate("=COUNTBLANK(INDEX(A1:A3,2))", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Countblank_RangeArgument_StillWorksAsBefore()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (3, 1, new NumberValue(3)));

        _eval.Evaluate("=COUNTBLANK(A1:A3)", sheet, wb).Should().Be(new NumberValue(1));
    }
}
