using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R29-performance-scale-correctness-1: TryCreateDirectRangeArgument(FormulaNode,...)'s
/// NamedRangeNode branch in FormulaEvaluator.SubtotalAggregateFastPaths.cs resolved a named
/// full-column/full-row range (e.g. Data = Sheet1!$A:$A, a common "growing table" pattern
/// created via the Name Manager) to its raw 1,048,576-row grid extent and passed that straight
/// into the shared 5-arg TryCreateDirectRangeArgument helper without ever clamping it to the
/// sheet's used range first -- unlike the literal-RangeRefNode overload just below it, which
/// calls ClampOpenEndedRangeToUsed before measuring cell count. Because the raw extent exceeds
/// FormulaSafetyLimits.MaxMaterializedRangeCells (1,000,000), SUBTOTAL/AGGREGATE (and every other
/// consumer of the same shared helper: COUNTIF/SUMIF/COUNTIFS/SUMIFS/AVERAGEIFS, TEXTJOIN/CONCAT
/// range args, LARGE/SMALL/RANK selection functions) wrongly returned #REF! for a named
/// full-column/full-row range even though the identical literal range (e.g. "=SUBTOTAL(9,A:A)")
/// already worked correctly via that clamp.
///
/// Fixed by clamping the resolved named-range extent to the sheet's used range before handing it
/// to the shared cap check, mirroring both the literal-range clamp (ClampOpenEndedRangeToUsed)
/// and the equivalent named-range clamp already present for SUM/AVERAGE/etc. in
/// FormulaEvaluator.FastAggregates.cs (TryClampFullRangeToUsed).
/// </summary>
public sealed class R29_NamedFullColumnSubtotalAggregateClampTests
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

    [Fact]
    public void Subtotal_NamedFullColumnRange_ClampsToUsedRange_InsteadOfRefError()
    {
        // Data = Sheet1!$A:$A, but only A1:A3 are populated.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        wb.DefineNamedRange(
            "Data",
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 1)));

        var result = _eval.Evaluate("=SUBTOTAL(9,Data)", sheet, wb);

        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Subtotal_LiteralFullColumnRange_StillWorks_SiblingCase()
    {
        // Sibling already-working case: the identical literal (non-named) full-column range
        // must keep returning the correct clamped result (regression guard for the fix above).
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));

        var result = _eval.Evaluate("=SUBTOTAL(9,A:A)", sheet, wb);

        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Aggregate_NamedFullColumnRange_ClampsToUsedRange_InsteadOfRefError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        wb.DefineNamedRange(
            "Data",
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 1)));

        // AGGREGATE(9,"SUM",0,options=0,ignore nothing) over the named full-column range.
        var result = _eval.Evaluate("=AGGREGATE(9,0,Data)", sheet, wb);

        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Subtotal_NamedFullColumnRange_EmptySheet_ReturnsZero_NotRefError()
    {
        // No populated cells at all: the sheet has no used range, so the clamp collapses the
        // named full-column range to zero cells (matching real Excel's SUM-of-empty-range = 0)
        // instead of exceeding the cap on the raw 1,048,576-row nominal extent.
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        wb.DefineNamedRange(
            "Data",
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 1)));

        var result = _eval.Evaluate("=SUBTOTAL(9,Data)", sheet, wb);

        result.Should().Be(new NumberValue(0));
    }
}
