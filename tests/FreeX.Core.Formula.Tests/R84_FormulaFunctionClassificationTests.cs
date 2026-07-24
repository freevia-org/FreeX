using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-84 review fixes for src/FreeX.Core.Formula/FormulaEvaluator.FunctionClassification.cs:
///
///   R84-formula-stat-regression-5-1: CORREL and FORECAST(.LINEAR) were missing from
///     ReferenceProvenanceAggregates (and DirectTextCoercingAggregates), so a bare single-cell
///     reference argument holding non-numeric text/blank/logical fell through to
///     BuiltInFunctions.StatisticalCore.Regression.cs's BuildPairedSource raw-ToNumber fallback
///     and threw #VALUE! instead of being ignored the way SLOPE/INTERCEPT/RSQ/STEYX/PEARSON/
///     COVARIANCE.P/S already ignore it (matching Excel's "text, logical values, and empty cells
///     in a reference argument are ignored" rule). Fixed by adding CORREL/FORECAST/
///     FORECAST.LINEAR to ReferenceProvenanceAggregates (and DirectTextCoercingAggregates for
///     consistency with the sibling regression functions), plus excluding FORECAST(.LINEAR)'s
///     first argument (x, coerced directly via ToNumber, never through the
///     ReferencedScalarValue-aware path) from the wrap -- mirroring the existing NPV rate-argument
///     exclusion -- so a bare numeric x reference keeps working.
///
///   R84-calc-crosssheet-3d-5-2: a 3-D sheet-span reference (e.g. Sheet1:Sheet3!A1) was silently
///     accepted and expanded for every function in AggregateFunctions, including several Excel
///     restricts to #VALUE! for 3-D spans (MEDIAN, MODE*, AND, OR, XOR, CONCAT(ENATE), GEOMEAN,
///     HARMEAN, AVEDEV, GCD, LCM, SUMSQ/SUMX2*/SUMXMY2, NPV). Fixed by introducing a narrower
///     SheetSpanAggregateFunctions set (SUM/AVERAGE(A)/COUNT(A)/MAX(A)/MIN(A)/PRODUCT/
///     STDEV(.S/A)/STDEVP(.P/A)/VAR(.S/A/P/PA) -- Excel's documented 3-D-eligible list) and
///     gating FormulaEvaluator.Functions.cs's sheet-span-expansion decision on it instead of the
///     broader AggregateFunctions set (which must stay broad for unrelated concerns: variadic
///     arity and array/named-formula RangeValue flattening).
/// </summary>
public sealed class R84_FormulaFunctionClassificationTests
{
    private readonly FormulaEvaluator _eval = new();

    // --- Finding 1: CORREL/FORECAST(.LINEAR) bare-ref text/blank ignoring -------------------

    [Fact]
    public void Correl_BareSingleCellRef_NonNumericText_IsIgnored_NotValue()
    {
        // Pre-fix: CORREL(A1,B1) with A1="x" (text) threw #VALUE! because the bare cell
        // arrived as a raw TextValue and BuildPairedSource's fallback called ToNumber on it.
        // Post-fix: matches PEARSON(A1,B1) -- ignores the non-numeric cell, leaving 0 valid
        // pairs, which the function's own n>=2 threshold reports as #DIV/0!.
        var sheet = MakeSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        _eval.Evaluate("=CORREL(A1,B1)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Pearson_BareSingleCellRef_NonNumericText_IsIgnored_Sibling()
    {
        // Sibling case: PEARSON already worked pre-fix (it was already in
        // ReferenceProvenanceAggregates) and must be unaffected by adding CORREL alongside it.
        var sheet = MakeSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        _eval.Evaluate("=PEARSON(A1,B1)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Forecast_BareSingleCellRef_NonNumericText_IsIgnored_NotValue()
    {
        // Pre-fix: FORECAST(3,A1,B1) with A1="x" threw #VALUE! for the same reason as CORREL.
        var sheet = MakeSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        _eval.Evaluate("=FORECAST(3,A1,B1)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void ForecastLinear_BareSingleCellRef_NonNumericText_IsIgnored_Sibling()
    {
        var sheet = MakeSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        _eval.Evaluate("=FORECAST.LINEAR(3,A1,B1)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Forecast_BareCellRef_NumericXArgument_StillComputesCorrectly_NoRegression()
    {
        // FORECAST's first argument (x) is coerced directly via ToNumber in
        // BuiltInFunctions.StatisticalCore.Regression.cs's Forecast() -- it never goes through
        // the ReferencedScalarValue-aware BuildPairedSource path, so it must NOT be wrapped even
        // though FORECAST is now a ReferenceProvenanceAggregate (mirroring the existing NPV
        // rate-argument exclusion). A bare numeric cell ref for x must keep working.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)),   // A1: x
            (1, 2, new NumberValue(1)), (1, 3, new NumberValue(2)),   // B1/C1: known_ys/known_xs pt1
            (2, 2, new NumberValue(2)), (2, 3, new NumberValue(4)));  // B2/C2: pt2

        _eval.Evaluate("=FORECAST(A1,B1:B2,C1:C2)", sheet).Should().Be(new NumberValue(1.5));
    }

    [Fact]
    public void Correl_MultiPointRangeRefs_StillComputesCorrectly_NoRegression()
    {
        // Sibling already-working case: a real 2+ point correlation via range refs (not bare
        // single-cell refs) must be unaffected by the classification change.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(4)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(6)));

        _eval.Evaluate("=CORREL(A1:A3,B1:B3)", sheet).Should().Be(new NumberValue(1));
    }

    // --- Finding 2: 3-D sheet spans rejected by non-3D-eligible aggregate functions ----------

    [Theory]
    [InlineData("MEDIAN")]
    [InlineData("AND")]
    [InlineData("OR")]
    [InlineData("XOR")]
    [InlineData("GCD")]
    [InlineData("MODE")]
    [InlineData("GEOMEAN")]
    public void SheetSpan3D_RejectedByNonEligibleAggregate_ReturnsValueError(string fn)
    {
        // Pre-fix: AggregateFunctions gated span expansion, and it wrongly included these
        // functions, so e.g. MEDIAN(Sheet1:Sheet3!A1) silently expanded to {1,2,3} and returned
        // 2 instead of #VALUE!. Post-fix: only the real Excel 3-D-eligible subset expands spans.
        var workbook = ThreeSheetWorkbook(out var sheet1, out _, out _);

        _eval.Evaluate($"={fn}(Sheet1:Sheet3!A1)", sheet1, workbook).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void SheetSpan3D_AcceptedBySum_NoRegression()
    {
        // Sibling already-working case: SUM is genuinely 3-D-eligible in Excel and must still
        // expand the span across sheets exactly as before.
        var workbook = ThreeSheetWorkbook(out var sheet1, out _, out _);

        _eval.Evaluate("=SUM(Sheet1:Sheet3!A1)", sheet1, workbook).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void SheetSpan3D_AcceptedByAverage_NoRegression()
    {
        var workbook = ThreeSheetWorkbook(out var sheet1, out _, out _);

        _eval.Evaluate("=AVERAGE(Sheet1:Sheet3!A1)", sheet1, workbook).Should().Be(new NumberValue(2));
    }

    private static Workbook ThreeSheetWorkbook(out Sheet sheet1, out Sheet sheet2, out Sheet sheet3)
    {
        var workbook = new Workbook("Test");
        sheet1 = workbook.AddSheet("Sheet1");
        sheet2 = workbook.AddSheet("Sheet2");
        sheet3 = workbook.AddSheet("Sheet3");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));
        return workbook;
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }
}
