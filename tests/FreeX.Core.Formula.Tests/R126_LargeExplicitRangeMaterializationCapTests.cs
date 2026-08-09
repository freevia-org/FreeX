using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-126 regression tests for the HIGH-severity defect at
/// FormulaEvaluator.References.cs:1045 (BuildRangeValue) and :2272 (OFFSET) — the general-purpose
/// range materializer INDEX's slow path, VLOOKUP/HLOOKUP/MATCH/XLOOKUP fallback paths, MMULT,
/// structured-reference functions and ISREF's 2-D path all reach through, plus OFFSET's own
/// materializer, threw/returned #REF! for any explicit RangeRefNode (or OFFSET height/width) whose
/// row*col product exceeded FormulaSafetyLimits.MaxMaterializedRangeCells (previously exactly
/// 1,000,000 -- deliberately just UNDER a single full worksheet column's real height,
/// CellAddress.MaxRow = 1,048,576). That made perfectly ordinary explicit bounded ranges like
/// A1:C500000 (3 cols x 500,000 rows = 1,500,000 cells) and well-known idioms like
/// =OFFSET($A$1,0,0,ROWS($A:$A),1) (a dynamic "whole column" reference, 1,048,576 x 1 = 1,048,576
/// cells) deterministically return #REF!, even though both are trivially valid in real Excel
/// (1,048,576-row x 16,384-column sheet).
///
/// The fix has two parts:
///  1) Raise FormulaSafetyLimits.MaxMaterializedRangeCells from 1,000,000 to 16,777,216 (16 full
///     worksheet columns' worth of rows -- comfortably covers realistic explicit multi-column
///     ranges including a full column's height) for the call sites that genuinely allocate an
///     O(cells) in-memory array (BuildRangeValue, OFFSET, ISFORMULA/FORMULATEXT's multi-cell path,
///     the LARGE/SMALL/PERCENTILE selection buffer, INDIRECT's array materializer), while still
///     bounding worst-case memory (~134MB) against a truly pathological explicit whole-sheet-scale
///     reference (e.g. A1:XFD1048576, ~17.2 billion cells).
///  2) Remove the same cap check entirely from the VLOOKUP/HLOOKUP/MATCH/XLOOKUP direct-table fast
///     paths in FormulaEvaluator.LookupFastPaths.cs, which only ever track
///     (startRow,startCol,rowCount,colCount) coordinates and lazily read cells one at a time via
///     DirectLookupRangeReader -- they never allocate a rowCount x colCount array, so the cap
///     served no real memory-safety purpose there (the actual vector length is always bounded by a
///     single sheet axis, <= CellAddress.MaxRow or <= CellAddress.MaxCol, regardless of the other
///     dimension).
/// </summary>
public sealed class R126_LargeExplicitRangeMaterializationCapTests
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
    public void Index_ExplicitOneAndHalfMillionCellBoundedRange_ComputesInsteadOfRefError()
    {
        // The exact headline repro from the finding: A1:C500000 = 3 cols x 500,000 rows =
        // 1,500,000 cells, over the old MaxMaterializedRangeCells (1,000,000) but trivially inside
        // Excel's real 1,048,576-row sheet. INDEX's 2-D positional form forces the general
        // BuildRangeValue materializer (not a coordinate-only fast path), since it must actually
        // index into the resulting array.
        var (wb, sheet) = MakeWb((400_000, 2, new NumberValue(77)));

        var result = _eval.Evaluate("=INDEX(A1:C500000,400000,2)", sheet, wb);

        result.Should().Be(new NumberValue(77));
    }

    [Fact]
    public void Vlookup_ExplicitOneAndHalfMillionCellBoundedRange_ComputesInsteadOfRefError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("ten")),
            (250_000, 1, new NumberValue(99)), (250_000, 2, new TextValue("found")));

        var result = _eval.Evaluate("=VLOOKUP(99,A1:C500000,2,FALSE)", sheet, wb);

        result.Should().Be(new TextValue("found"));
    }

    [Fact]
    public void Hlookup_ExplicitBoundedRangeOverOldCap_ComputesInsteadOfRefError()
    {
        // Transposed sibling of the VLOOKUP case: a wide (many-column) x short table whose product
        // exceeds the old cap. 3 rows x 400,000 columns would be absurd/impossible (sheet only has
        // 16,384 columns), so exercise the row-count-drives-the-product shape instead: a lookup
        // table that is tall enough in ROWS but only 2 columns wide still went through the same
        // TryCreateDirectLookupArrayFormVectors gate as VLOOKUP.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(10)), (2, 2, new NumberValue(20)), (2, 3, new NumberValue(30)));

        var result = _eval.Evaluate("=HLOOKUP(2,A1:C2,2,FALSE)", sheet, wb);

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Offset_RowsOfFullColumnIdiom_SumsWholeColumnInsteadOfRefError()
    {
        // The well-known "reference an entire column dynamically" idiom named in the finding:
        // =OFFSET($A$1,0,0,ROWS($A:$A),1). ROWS($A:$A) always evaluates to 1,048,576 (matching
        // Excel), so the explicit height argument is exactly a full column's cell count --
        // previously over MaxMaterializedRangeCells (1,000,000), now comfortably under the raised
        // cap (16,777,216). OFFSET allocates its own array directly (FormulaEvaluator.References.cs
        // EvaluateOffsetReference), independent of BuildRangeValue, and is gated by the same shared
        // constant.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(3)),
            (500_000, 1, new NumberValue(4)),
            (CellAddress.MaxRow, 1, new NumberValue(5)));

        var result = _eval.Evaluate("=SUM(OFFSET($A$1,0,0,ROWS($A:$A),1))", sheet, wb);

        result.Should().Be(new NumberValue(12));
    }

    [Fact]
    public void Mmult_ExplicitBoundedRangeOverOldCap_ComputesInsteadOfRefError()
    {
        // MMULT is named explicitly in the finding as a BuildRangeValue consumer. MMULT requires
        // every cell in its range argument to already be numeric (BuiltInFunctions.Matrix.cs
        // TryCellNumber rejects BlankValue with #VALUE!, unrelated to this defect), so a
        // 1,048,000-row x 1-column vector (over the OLD 1,000,000 cap, under both the new cap and
        // CellAddress.MaxRow) must be fully populated to isolate the materialization-cap behavior
        // from that separate blank-cell rule. Multiplying by the 1x1 identity MUNIT(1) leaves the
        // vector unchanged, which keeps the assertion simple while still exercising
        // BuildRangeValue end-to-end on a genuinely large explicit bounded range.
        const uint rows = 1_048_000;
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= rows; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r == rows ? 42 : 1));

        var result = _eval.Evaluate("=MMULT(A1:A1048000,MUNIT(1))", sheet, wb);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be((int)rows);
        range.ColCount.Should().Be(1);
        range.Cells[0, 0].Should().Be(new NumberValue(1));
        range.Cells[(int)rows - 1, 0].Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Index_TrulyExcessiveExplicitBoundedRange_StillReturnsRefError_NoRegression()
    {
        // No-regression sibling: the raised cap must still reject a genuinely pathological
        // explicit range once its product exceeds the new 16,777,216 ceiling: 20 columns (A:T) x
        // 1,048,575 rows = 20,971,500 cells. The end row is deliberately ONE LESS than
        // CellAddress.MaxRow (not the literal max-row sentinel) so ClampOpenEndedRangeToUsed's
        // full-column/full-row heuristic does NOT fire and shrink it first. The 4-argument
        // area_num form (area_num=1) is used to force INDEX past its coordinate-only direct-cell
        // fast path (TryEvaluateIndexDirectRange, which has no arm for 4 arguments) into the
        // generic registry Index() implementation, whose range argument is wrapped via
        // BuildRangeValueOrError -- so this genuinely exercises the materialization-cap check
        // instead of the always-uncapped single-cell fast path, preserving the original
        // memory-safety intent of the cap against a truly excessive explicit bounded range.
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(1)));

        var result = _eval.Evaluate($"=INDEX(A1:T{CellAddress.MaxRow - 1},1,1,1)", sheet, wb);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Vlookup_SmallOrdinaryBoundedRange_StillComputesExactly_NoRegression()
    {
        // No-regression sibling: an ordinary small VLOOKUP table (nowhere near either cap) must
        // still resolve correctly now that the coordinate-only fast-path gate has been removed.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)), (1, 2, new TextValue("a")),
            (2, 1, new NumberValue(2)), (2, 2, new TextValue("b")));

        var result = _eval.Evaluate("=VLOOKUP(2,A1:B2,2,FALSE)", sheet, wb);

        result.Should().Be(new TextValue("b"));
    }

    [Fact]
    public void Vlookup_MissingKeyInLargeBoundedRange_StillReturnsNA_NoRegression()
    {
        // No-regression sibling: removing the materialization-cap gate must not affect ordinary
        // exact-match "not found" behavior for a range that is large but well under both caps.
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(1)), (1, 2, new TextValue("a")));

        var result = _eval.Evaluate("=VLOOKUP(999,A1:B500000,2,FALSE)", sheet, wb);

        result.Should().Be(ErrorValue.NA);
    }
}
