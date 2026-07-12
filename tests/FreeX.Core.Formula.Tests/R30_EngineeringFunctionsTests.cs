using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-30 review fixes for the Engineering built-in functions:
///   R30-formula-engineering-fns-1: IMSUB/IMDIV must return #NUM! when the two operands mix
///     "i" and "j" notation, mirroring the check IMSUM/IMPRODUCT already have.
///   R30-formula-engineering-fns-2: CONVERT unit "Pica" carried a factor 10x too small.
///   R30-formula-engineering-fns-3: CONVERT is missing the "u" (atomic mass unit) Weight unit.
/// </summary>
public sealed class R30_EngineeringFunctionsTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void ImSub_MixedIJSuffixes_ReturnsNum()
    {
        _eval.Evaluate("=IMSUB(\"3+4i\",\"1+2j\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void ImDiv_MixedIJSuffixes_ReturnsNum()
    {
        _eval.Evaluate("=IMDIV(\"3+6i\",\"1-2j\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void ImSub_ConsistentSuffix_StillSubtractsCorrectly()
    {
        // Sanity check that the new mixed-suffix guard doesn't break same-notation subtraction.
        _eval.Evaluate("=IMSUB(\"3+6i\",\"1+2i\")", MakeSheet())
            .Should().Be(new TextValue("2+4i"));
    }

    [Fact]
    public void ImDiv_ConsistentSuffix_StillDividesCorrectly()
    {
        _eval.Evaluate("=IMDIV(\"10+0i\",\"2+0i\")", MakeSheet())
            .Should().Be(new TextValue("5"));
    }

    [Fact]
    public void ImSub_PureRealOperand_DoesNotConflictWithOtherOperandsSuffix()
    {
        // A plain real number has no explicit "i"/"j" notation of its own, so it must not be
        // treated as a suffix mismatch against the other (genuinely complex) operand.
        _eval.Evaluate("=IMSUB(5,\"3+2j\")", MakeSheet())
            .Should().Be(new TextValue("2-2j"));
    }

    [Fact]
    public void ImDiv_PureRealOperand_DoesNotConflictWithOtherOperandsSuffix()
    {
        _eval.Evaluate("=IMDIV(10,\"5+0j\")", MakeSheet())
            .Should().Be(new TextValue("2"));
    }

    [Fact]
    public void Convert_Pica_ToInches_MatchesExcel()
    {
        // 1 Pica = 1/6 inch = 0.166666667 in.
        var result = _eval.Evaluate("=CONVERT(1,\"Pica\",\"in\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(1.0 / 6.0, 1e-9);
    }

    [Fact]
    public void Convert_Picapt_Unaffected_ByPicaFix()
    {
        // Sibling unit "Picapt" (1 point) must be untouched by the "Pica" factor correction.
        var result = _eval.Evaluate("=CONVERT(72,\"Picapt\",\"in\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void Convert_AtomicMassUnit_ToGrams_MatchesExcel()
    {
        var result = _eval.Evaluate("=CONVERT(1,\"u\",\"g\")", MakeSheet());
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(1.66053886e-24, 1e-33);
    }

    [Fact]
    public void Convert_ExistingWeightUnit_StillWorks()
    {
        // Sibling weight-unit conversion must be unaffected by adding "u".
        _eval.Evaluate("=CONVERT(1,\"kg\",\"g\")", MakeSheet()).Should().Be(new NumberValue(1000));
    }

    private static Sheet MakeSheet(params (uint Row, uint Col, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
        return sheet;
    }
}
