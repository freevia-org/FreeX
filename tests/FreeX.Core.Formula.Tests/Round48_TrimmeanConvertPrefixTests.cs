using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-48 fix: R48-formula-engineering-convert-3-1 — CONVERT allowed metric SI
/// prefixes on non-prefixable (imperial/named) units.
///
/// R48-formula-statistical-rank-3-1 (TRIMMEAN unimplemented) and
/// R48-formula-engineering-convert-3-3 ("da" deca prefix) are intentionally NOT
/// fixed here — see triage notes in the accompanying fix report: both conflict
/// with existing deliberately-authored tests
/// (FormulaParityCatalogTests.Registry_DoesNotContainUndocumentedFunctions and
/// ExcelParityEngineeringTests.Convert_DekaPrefixesAndExactErgMatchExcelUnits).
/// </summary>
public class Round48_TrimmeanConvertPrefixTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) MakeWb(params (int row, int col, ScalarValue val)[] cells)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return (wb, sheet);
    }

    // ── R48-formula-engineering-convert-3-1: prefix only on prefixable units ─

    [Fact]
    public void Convert_MetricPrefixOnImperialUnit_ReturnsNA()
    {
        // Pre-fix: "kmi" resolved as prefix "k" (1e3) + unit "mi" (1609.344) = 1609344.
        // Real Excel: "mi" cannot take a metric prefix -> #N/A.
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"kmi\",\"m\")", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Convert_MetricPrefixOnNamedWeightUnit_ReturnsNA()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"klbm\",\"kg\")", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Convert_MetricPrefixOnPrefixableUnit_StillWorks()
    {
        // No-regression: "cg" (centi + gram) is a genuinely prefixable metric base unit.
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"cg\",\"g\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.01, 1e-12);
    }

    [Fact]
    public void Convert_EPrefix_StillWorksForDeca()
    {
        // No-regression: Excel's actual deca prefix code "e" must keep working
        // (unaffected by the convert-3-1 fix; convert-3-3's "da" removal was reverted).
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"em\",\"m\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(10, 1e-12);
    }

    // ── No-regression: common conversions untouched by the CONVERT fix ────

    [Fact]
    public void Convert_KgToLbm_Unchanged()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"kg\",\"lbm\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(2.2046226218, 1e-6);
    }

    [Fact]
    public void Convert_KmToMi_Unchanged()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"km\",\"mi\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.6213711922, 1e-6);
    }

    [Fact]
    public void Convert_CelsiusToFahrenheit_OffsetUnchanged()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(100,\"C\",\"F\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(212, 1e-9);
    }
}
