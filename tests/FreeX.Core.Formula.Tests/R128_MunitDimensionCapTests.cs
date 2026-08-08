using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-128 regression tests for the MED-severity defect at BuiltInFunctions.Matrix.cs:74 --
/// Munit(args, ctx) hard-coded `raw > 1024` as the dimension ceiling for MUNIT(dimension). Real
/// Excel's MUNIT has no dedicated dimension cap beyond ordinary worksheet/array-size limits, and
/// FreeX's own general-purpose array-materialization safety guard
/// (FormulaSafetyLimits.MaxMaterializedRangeCells = 16,777,216 cells) would itself allow a
/// dimension up to sqrt(16,777,216) ~= 4096 before any memory concern kicks in. The fix replaces
/// the fixed 1024 ceiling with the same `(long)dimension * dimension > MaxMaterializedRangeCells`
/// pattern used by SEQUENCE/RANDARRAY/EXPAND/etc.
/// </summary>
public sealed class R128_MunitDimensionCapTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet()
    {
        var wb = new Workbook("Test");
        return wb.AddSheet("Sheet1");
    }

    [Fact]
    public void Munit_DimensionAbove1024ButWithinMaterializationCap_ComputesInsteadOfValueError()
    {
        // The headline repro from the finding: a 1500x1500 identity matrix is well within both
        // real Excel's worksheet limits and FreeX's own MaxMaterializedRangeCells budget
        // (1500*1500 = 2,250,000 cells, comfortably under 16,777,216), but was previously rejected
        // by the bespoke, unexplained `raw > 1024` ceiling.
        var result = _eval.Evaluate("=MUNIT(1500)", MakeSheet());

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(1500);
        range.ColCount.Should().Be(1500);
        range.At(1, 1).Should().Be(new NumberValue(1));
        range.At(1, 2).Should().Be(new NumberValue(0));
        range.At(1500, 1500).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Munit_DimensionExceedingMaterializationCap_StillReturnsValueError_NoRegression()
    {
        // No-regression sibling: a dimension whose square genuinely exceeds
        // MaxMaterializedRangeCells (16,777,216) must still be rejected -- the fix widens the
        // ceiling to match the shared memory-safety budget, it does not remove it. 5000*5000 =
        // 25,000,000 > 16,777,216.
        var result = _eval.Evaluate("=MUNIT(5000)", MakeSheet());

        result.Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=MUNIT(0)")]
    [InlineData("=MUNIT(-1)")]
    [InlineData("=MUNIT(\"x\")")]
    public void Munit_InvalidDimension_StillReturnsValueError_NoRegression(string formula)
    {
        // No-regression sibling for the pre-existing invalid-dimension cases (zero, negative,
        // non-numeric), untouched by this fix.
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Value);
    }
}
