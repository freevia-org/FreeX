using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-37 fixes for the Engineering built-in functions:
///   R37-formula-engineering-complex-1: BESSELI/BESSELJ/BESSELK/BESSELY were entirely
///     unimplemented (#NAME?); now registered + implemented via standard published
///     algorithms (Abramowitz &amp; Stegun / "Numerical Recipes" rational approximations
///     and up/downward recurrences), verified against published reference values.
///   R37-formula-engineering-complex-2: CONVERT rejected the documented "cel"/"fah"/"kel"
///     temperature-unit aliases.
///   R37-formula-engineering-complex-3: IM* functions (IMSUM/IMPRODUCT/etc.) returned
///     #NUM! for a blank cell argument instead of treating it as complex 0.
/// </summary>
public sealed class R37_EngineeringFunctionsTests
{
    private readonly FormulaEvaluator _eval = new();

    // ── BESSELJ ──────────────────────────────────────────────────────────────

    [Fact]
    public void BesselJ_Order0_MatchesPublishedReferenceValue()
    {
        var result = _eval.Evaluate("=BESSELJ(1,0)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.7651976866, 1e-6);
    }

    [Fact]
    public void BesselJ_Order1_MatchesPublishedReferenceValue()
    {
        var result = _eval.Evaluate("=BESSELJ(1,1)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.4400505857, 1e-6);
    }

    [Fact]
    public void BesselJ_Order2_UsesRecurrence_MatchesPublishedReferenceValue()
    {
        // Order >= 2 exercises the downward-recurrence (Miller's algorithm) code path,
        // not just the direct rational approximations used for J0/J1.
        var result = _eval.Evaluate("=BESSELJ(1,2)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.1149034849, 1e-6);
    }

    [Fact]
    public void BesselJ_NegativeOrder_ReturnsNum()
    {
        _eval.Evaluate("=BESSELJ(1,-1)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void BesselJ_NonIntegerOrder_IsTruncated()
    {
        // Excel truncates a non-integer n toward zero (BESSELJ(1,0.9) behaves like n=0).
        var result = _eval.Evaluate("=BESSELJ(1,0.9)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.7651976866, 1e-6);
    }

    // ── BESSELI ──────────────────────────────────────────────────────────────

    [Fact]
    public void BesselI_Order0_MatchesPublishedReferenceValue()
    {
        var result = _eval.Evaluate("=BESSELI(1,0)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(1.2660658778, 1e-6);
    }

    [Fact]
    public void BesselI_Order1_MatchesPublishedReferenceValue()
    {
        var result = _eval.Evaluate("=BESSELI(1,1)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.5651591040, 1e-6);
    }

    // ── BESSELK (requires x > 0) ─────────────────────────────────────────────

    [Fact]
    public void BesselK_Order0_MatchesPublishedReferenceValue()
    {
        var result = _eval.Evaluate("=BESSELK(1,0)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.4210244382, 1e-6);
    }

    [Fact]
    public void BesselK_NonPositiveX_ReturnsNum()
    {
        _eval.Evaluate("=BESSELK(0,0)", MakeSheet()).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=BESSELK(-1,0)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    // ── BESSELY (requires x > 0) ─────────────────────────────────────────────

    [Fact]
    public void BesselY_Order0_MatchesPublishedReferenceValue()
    {
        var result = _eval.Evaluate("=BESSELY(1,0)", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.0882569642, 1e-6);
    }

    [Fact]
    public void BesselY_NonPositiveX_ReturnsNum()
    {
        _eval.Evaluate("=BESSELY(0,0)", MakeSheet()).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=BESSELY(-1,0)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    // ── CONVERT temperature aliases ──────────────────────────────────────────

    [Fact]
    public void Convert_FahAlias_ToCelAlias_MatchesExcel()
    {
        var result = _eval.Evaluate("=CONVERT(32,\"fah\",\"cel\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void Convert_KelAlias_ToC_MatchesExcel()
    {
        var result = _eval.Evaluate("=CONVERT(273.15,\"kel\",\"C\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void Convert_ExistingCanonicalTemperatureUnits_StillWork()
    {
        // Sibling canonical-unit conversion must be unaffected by adding the aliases.
        var result = _eval.Evaluate("=CONVERT(32,\"F\",\"C\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.0, 1e-9);
    }

    // ── IM* blank-cell handling ───────────────────────────────────────────────

    [Fact]
    public void ImSum_BlankCellInRange_TreatedAsZero()
    {
        // A1=1, A2 is left unset (blank), A3="2i".
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (3, 1, new TextValue("2i")));
        _eval.Evaluate("=IMSUM(A1:A3)", sheet).Should().Be(new TextValue("1+2i"));
    }

    [Fact]
    public void ImProduct_BlankCellInRange_TreatedAsZero()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(3)), (3, 1, new TextValue("2i")));
        // 3 * 0 * 2i == 0 (the blank cell contributes a factor of 0, exactly like a literal 0).
        _eval.Evaluate("=IMPRODUCT(A1:A3)", sheet).Should().Be(new TextValue("0"));
    }

    [Fact]
    public void ImSum_NoBlankCells_StillSumsCorrectly()
    {
        // Sibling no-regression case: an all-populated range sums exactly as before.
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new TextValue("3i")));
        _eval.Evaluate("=IMSUM(A1:A3)", sheet).Should().Be(new TextValue("3+3i"));
    }

    private static Sheet MakeSheet(params (uint Row, uint Col, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
        return sheet;
    }
}
