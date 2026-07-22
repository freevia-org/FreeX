using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R65-commands-insert-delete-cells-6-1: band-scoped Insert Cells (Shift Right/Down) had no
/// dedicated range-rewrite case, so it fell to the generic per-corner rewrite. For =SUM(D1:D5)
/// with an Insert-Cells-Shift-Right band on row 1 only, Start=D1 (row 1, in band) shifted to E1
/// but End=D5 (row 5, outside the band) stayed D5, yielding =SUM(E1:D5), which normalizes to the
/// D1:E5 bounding box -- silently pulling in unrelated cells. The fix only shifts a range whose
/// ENTIRE row/column span sits inside the shift band; otherwise the whole range is left untouched
/// (mirrors the DeleteCellsShiftUp/Left band-containment guard, but returns unchanged instead of
/// falling back to a per-endpoint rewrite, since a partial-band Insert Cells can't be represented
/// by shifting one corner alone).
/// </summary>
public class R65_InsertCellsRangeShiftTests
{
    // ── InsertCellsShiftRightOp ───────────────────────────────────────────────

    [Fact]
    public void InsertCellsShiftRight_RangeStraddlesRowBand_LeftEntirelyUnchanged()
    {
        // Band is row 1 only (BandStartRow=BandEndRow=1); D1:D5 spans rows 1-5, so it straddles
        // the band. Before the fix this partially shifted Start (D1->E1) leaving End (D5)
        // untouched, producing SUM(E1:D5) -> normalized D1:E5 bounding box.
        var op = new InsertCellsShiftRightOp("Sheet1",
            BandStartRow: 1, BandEndRow: 1,
            RangeStartCol: 1, BandEndCol: 16384,
            InsertBeforeCol: 4, Count: 1);
        var result = FormulaRewriter.Rewrite("SUM(D1:D5)", op, "Sheet1");
        result.Should().BeNull("no reference in a straddling range may be shifted");
    }

    [Fact]
    public void InsertCellsShiftRight_RangeFullyInsideRowBand_ShiftsCorrectly()
    {
        // Band is row 1 only; D1:F1 also spans only row 1, so it's fully inside the band and
        // shifts normally: D(4)>=InsertBeforeCol(4) shifts to E, F(6)>=4 shifts to G.
        var op = new InsertCellsShiftRightOp("Sheet1",
            BandStartRow: 1, BandEndRow: 1,
            RangeStartCol: 1, BandEndCol: 16384,
            InsertBeforeCol: 4, Count: 1);
        var result = FormulaRewriter.Rewrite("SUM(D1:F1)", op, "Sheet1");
        result.Should().Be("SUM(E1:G1)");
    }

    [Fact]
    public void InsertCellsShiftRight_RangeWhollyOutsideRowBand_Unchanged()
    {
        // Band is row 1 only; D10:D12 doesn't touch row 1 at all.
        var op = new InsertCellsShiftRightOp("Sheet1",
            BandStartRow: 1, BandEndRow: 1,
            RangeStartCol: 1, BandEndCol: 16384,
            InsertBeforeCol: 4, Count: 1);
        var result = FormulaRewriter.Rewrite("SUM(D10:D12)", op, "Sheet1");
        result.Should().BeNull();
    }

    // ── InsertCellsShiftDownOp ─────────────────────────────────────────────────

    [Fact]
    public void InsertCellsShiftDown_RangeStraddlesColumnBand_LeftEntirelyUnchanged()
    {
        // Band is column A only (RangeStartCol=RangeEndCol=1); A1:B5 spans columns A-B, so it
        // straddles the band (column B is outside it).
        var op = new InsertCellsShiftDownOp("Sheet1",
            BandStartRow: 3, BandEndRow: 1048576,
            RangeStartCol: 1, RangeEndCol: 1,
            InsertBeforeRow: 3, Count: 1);
        var result = FormulaRewriter.Rewrite("SUM(A1:B5)", op, "Sheet1");
        result.Should().BeNull("no reference in a straddling range may be shifted");
    }

    [Fact]
    public void InsertCellsShiftDown_RangeFullyInsideColumnBand_ShiftsCorrectly()
    {
        // Band is column A only; A1:A5 spans only column A, so it's fully inside the band and
        // shifts normally: row 1 < InsertBeforeRow(3) stays, row 5 >= 3 shifts to 6.
        var op = new InsertCellsShiftDownOp("Sheet1",
            BandStartRow: 3, BandEndRow: 1048576,
            RangeStartCol: 1, RangeEndCol: 1,
            InsertBeforeRow: 3, Count: 1);
        var result = FormulaRewriter.Rewrite("SUM(A1:A5)", op, "Sheet1");
        result.Should().Be("SUM(A1:A6)");
    }

    [Fact]
    public void InsertCellsShiftDown_RangeWhollyOutsideColumnBand_Unchanged()
    {
        // Band is column A only; C1:C5 doesn't touch column A at all.
        var op = new InsertCellsShiftDownOp("Sheet1",
            BandStartRow: 3, BandEndRow: 1048576,
            RangeStartCol: 1, RangeEndCol: 1,
            InsertBeforeRow: 3, Count: 1);
        var result = FormulaRewriter.Rewrite("SUM(C1:C5)", op, "Sheet1");
        result.Should().BeNull();
    }
}
