using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-25 fix-bucket "fastaggregate-countblank-bounded" regression tests.
///
/// R25-meta-1: Round 24 fixed COUNTBLANK(A:B)/COUNTBLANK(A:XFD) (full-column/full-row ranges)
/// by clamping the SCANNED rectangle to the sheet's used range while preserving the un-clamped
/// nominal cell count separately for the final blank-count math. But TryAcceptFastAggregateRange's
/// safety cap still checked the RAW (un-clamped) cell count for an ordinary BOUNDED range (e.g.
/// A1:B600000, 1,200,000 cells) -- since a bounded RangeRefNode never sets NominalCellCount and
/// was never clamped by the full-column/full-row-only branch, its full un-clamped cell count was
/// checked against MaxStreamingRangeCells (1,048,576) and rejected with #REF!, even though the
/// sheet's actual used range for that rectangle is tiny.
///
/// The fix generalizes the used-range clamp (and NominalCellCount bookkeeping) to apply for
/// CountBlank on ANY range shape, not just full-column/full-row, so the streaming cap is only
/// ever checked against the (small) used-range-clamped SCANNED rectangle, never the raw nominal
/// bounds of a large bounded range.
/// </summary>
public sealed class R25_CountBlankBoundedLargeRangeTests
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
    public void CountBlank_BoundedRangeOverOneMillionCells_ComputesInsteadOfRefError()
    {
        // A1:B600000 = 2 columns x 600,000 rows = 1,200,000 cells -- over
        // FormulaSafetyLimits.MaxStreamingRangeCells (1,048,576). This is an ordinary bounded
        // range (not a full column/row), so before the fix its raw cell count was checked
        // (unclamped) against the streaming cap and rejected with #REF!. The sheet's used range
        // is tiny (just the two populated cells below), so the fix must clamp the scanned
        // rectangle down to that used range and compute the count near-instantly.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (3, 2, new TextValue("x")));

        var result = _eval.Evaluate("=COUNTBLANK(A1:B600000)", sheet, wb);

        // 1,200,000 nominal cells, 2 of them non-blank.
        result.Should().Be(new NumberValue(1_200_000L - 2));
    }

    [Fact]
    public void CountBlank_BoundedRangeOverOneMillionCells_EmptySheet_ReturnsWholeNominalCountInstantly()
    {
        // No populated cells at all: the used-range clamp finds no overlap, so every nominal
        // cell in the bounded range is blank.
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");

        var result = _eval.Evaluate("=COUNTBLANK(A1:B600000)", sheet, wb);

        result.Should().Be(new NumberValue(1_200_000L));
    }

    [Fact]
    public void CountBlank_FullMultiColumnRange_StillComputesInsteadOfRefError()
    {
        // Sibling case from round 24 (must not regress): COUNTBLANK(A:B) is a full-column range
        // nominally spanning 2 * 1,048,576 = 2,097,152 cells.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (2, 2, new NumberValue(10)));

        var result = _eval.Evaluate("=COUNTBLANK(A:B)", sheet, wb);

        result.Should().Be(new NumberValue(2 * (long)CellAddress.MaxRow - 2));
    }

    [Fact]
    public void CountBlank_SmallBoundedRange_StillCountsExactly()
    {
        // An ordinary small bounded range must still count correctly through the generalized
        // clamp-and-track-nominal-count path (same expected result as before the fix).
        var (wb, sheet) = MakeWb(
            (2, 2, new NumberValue(1)),
            (4, 3, new TextValue("x")));

        var result = _eval.Evaluate("=COUNTBLANK(B2:D6)", sheet, wb);

        // B2:D6 is 3 columns x 5 rows = 15 cells; 2 are non-blank (B2, C4).
        result.Should().Be(new NumberValue(13));
    }

    [Fact]
    public void Sum_BoundedRangeOverOneMillionCells_ComputesInsteadOfRefError()
    {
        // R126 correction: this test previously asserted #REF! here and called it "the existing,
        // unchanged safety-cap behavior for non-CountBlank fast-aggregate kinds" -- but that
        // encoded the very defect Round 126 fixed (FormulaEvaluator.FastAggregates.cs:580) as
        // intended behavior. Real Excel has no such limit: A1:B600000 (1,200,000 cells) is an
        // ordinary bounded range well inside the sheet's real 1,048,576-row limit, and
        // =SUM(A1:B600000) computes correctly regardless of how much of that range is populated.
        // SUM now gets the same used-range clamp CountBlank already had (see the tests above),
        // so the streaming cap is checked against the tiny used-range-clamped rectangle, not the
        // raw 1,200,000-cell nominal bound.
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(5)));

        var result = _eval.Evaluate("=SUM(A1:B600000)", sheet, wb);

        result.Should().Be(new NumberValue(5));
    }
}
