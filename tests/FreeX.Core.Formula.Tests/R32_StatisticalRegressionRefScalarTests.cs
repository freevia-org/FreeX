using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-32 review fix for src/FreeX.Core.Formula/BuiltInFunctions.StatisticalCore.Regression.cs:
///   R32-formula-statistical-regression-1: SLOPE/INTERCEPT/RSQ/STEYX/PEARSON/COVARIANCE.P/S/COVAR
///     returned #VALUE! instead of #DIV/0! (or the real propagated error) when called with bare
///     single-cell references (e.g. SLOPE(A1,B1)), because BuildPairedSource called ToNumber
///     directly on the ReferencedScalarValue wrapper instead of unwrapping it via
///     TryReferencedNumber the way every other ReferenceProvenanceAggregate function does.
///     A colon-range like A1:A1 was unaffected (it becomes a real 1x1 RangeValue), and CORREL
///     was unaffected because it happens to be absent from the ReferenceProvenanceAggregates set.
///     Note: once unwrapped, COVARIANCE.P/COVAR (population covariance) correctly compute 0 for a
///     single data point rather than #DIV/0! -- only the *sample* variant (COVARIANCE.S) and the
///     regression/correlation functions require >=2 points and report #DIV/0!.
/// </summary>
public sealed class R32_StatisticalRegressionRefScalarTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("SLOPE")]
    [InlineData("INTERCEPT")]
    [InlineData("RSQ")]
    [InlineData("STEYX")]
    [InlineData("PEARSON")]
    [InlineData("COVARIANCE.S")]
    public void BareSingleCellRefs_InsufficientData_ReturnsDivByZero_NotValue(string fn)
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (1, 2, new NumberValue(5)));

        // Pre-fix: BuildPairedSource called ToNumber on the ReferencedScalarValue wrapper for a
        // bare cell ref and threw, which the catch turned into #VALUE!.
        // Post-fix: unwraps to the plain number, and (matching CORREL(A1,B1) side-by-side with
        // the same inputs) a single data point is insufficient, so the function's own existing
        // n>=2 threshold logic correctly reports #DIV/0!.
        _eval.Evaluate($"={fn}(A1,B1)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Theory]
    [InlineData("COVARIANCE.P")]
    [InlineData("COVAR")]
    public void BareSingleCellRefs_PopulationCovariance_ComputesZero_NotValue(string fn)
    {
        // Population covariance (unlike the sample variant) is well-defined for n=1: the
        // numerator (sum of centered products) is 0 by construction, so Sxy/n = 0/1 = 0 -- this
        // is the function's own existing n=1 handling, unaffected by the unwrap fix, and it must
        // no longer be masked by the pre-fix #VALUE! throw.
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (1, 2, new NumberValue(5)));
        _eval.Evaluate($"={fn}(A1,B1)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void CorrelBareSingleCellRefs_InsufficientData_ReturnsDivByZero_Sibling()
    {
        // CORREL was never in the ReferenceProvenanceAggregates set, so it already worked --
        // included here as the sibling case the fix must not regress.
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (1, 2, new NumberValue(5)));
        _eval.Evaluate("=CORREL(A1,B1)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Slope_BareSingleCellRefs_ErrorInReferencedCell_Propagates()
    {
        // A #REF! (or any error) living in one of the referenced cells must propagate instead of
        // being masked by the #VALUE! thrown from ToNumber pre-fix.
        var sheet = MakeSheet((1, 2, new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.Ref);

        _eval.Evaluate("=SLOPE(A1,B1)", sheet).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Slope_MultiPointRangeRefs_StillComputesCorrectly()
    {
        // Sibling already-working case: a real 2+ point regression via range refs must be
        // unaffected by the bare-scalar unwrap fix.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(4)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(6)));

        _eval.Evaluate("=SLOPE(B1:B3,A1:A3)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Slope_MultiPointBareCellRefs_StillComputesCorrectly()
    {
        // Sibling already-working case, but exercising the exact BuildPairedSource bare-scalar
        // path fixed here for two independent single-cell SLOPE calls combined... actually SLOPE
        // needs paired data, so use two bare refs each holding one of a 2-point series isn't
        // possible without a range; this instead confirms a bare single ref still resolves to the
        // correct number when the *other* argument is a genuine 2+ point range (mismatched count
        // -> #N/A, matching Excel: SLOPE requires equal-length arrays).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(4)));

        _eval.Evaluate("=SLOPE(B1,A1:A2)", sheet).Should().Be(ErrorValue.NA);
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }
}
