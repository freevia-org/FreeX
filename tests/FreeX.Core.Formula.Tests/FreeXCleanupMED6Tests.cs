using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch MED6 (P74, P104, P76).
/// </summary>
public class FreeXCleanupMED6Tests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook Workbook, Sheet Sheet) MakeWb(params (int row, int col, ScalarValue val)[] cells)
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return (wb, sheet);
    }

    // ── P74: OFFSET with a full-column/full-row base must clamp to the used range ─────────

    [Fact]
    public void Offset_FullColumnBase_ClampsToUsedRangeAndSums()
    {
        // =SUM(OFFSET(A:A,0,1)) — a common "sum the column to the right of A" idiom. A:A as an
        // OFFSET base nominally spans 1,048,576 rows; without clamping to the sheet's used range,
        // the materialized 1,048,576 x 1 offset range exceeds MaxMaterializedRangeCells and the
        // whole formula returns #REF!, even though direct =SUM(A:A) works fine via the same clamp.
        var (wb, sheet) = MakeWb(
            (1, 2, new NumberValue(10)),
            (2, 2, new NumberValue(20)),
            (3, 2, new NumberValue(30)));

        var result = _eval.Evaluate("=SUM(OFFSET(A:A,0,1))", sheet, wb);

        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Offset_FullRowBase_ClampsToUsedRangeAndSums()
    {
        // Mirror of the full-column case along the row axis: =SUM(OFFSET(1:1,1,0)) should sum the
        // row below row 1 within the sheet's used range, not overflow the materialization cap.
        var (wb, sheet) = MakeWb(
            (2, 1, new NumberValue(1)),
            (2, 2, new NumberValue(2)),
            (2, 3, new NumberValue(3)));

        var result = _eval.Evaluate("=SUM(OFFSET(1:1,1,0))", sheet, wb);

        result.Should().Be(new NumberValue(6));
    }

    // ── P104: OFFSET base (and reference-argument sites) must honour sheet-scope name
    //          precedence — a sheet-scoped named FORMULA shadows a same-named workbook-global
    //          named RANGE, matching bare-name resolution (O50-class fix). ──────────────────

    [Fact]
    public void Offset_NamedRangeBase_PrefersSheetScopedFormulaOverWorkbookGlobalRange()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(100)), // A1 — global range Foo points here
            (2, 2, new NumberValue(1)),   // B2 — scoped formula's OFFSET base
            (3, 2, new NumberValue(2)),
            (4, 2, new NumberValue(3)),
            (5, 2, new NumberValue(4)),
            (6, 2, new NumberValue(5)));

        // Workbook-global: Foo = $A$1:$A$5 (a plain range).
        wb.DefineNamedRange("Foo", new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)));

        // Sheet1-scoped: Foo = OFFSET($B$1,1,0,5,1) (a formula that itself evaluates to a
        // reference) — must shadow the global range on Sheet1, per Excel's per-name (not
        // per-kind) sheet-scope precedence.
        wb.DefineNamedFormula("Foo", "OFFSET($B$1,1,0,5,1)", sheet.Id);

        // =OFFSET(Foo,0,0) must use the SCOPED formula's reference (B2:B6) as its base, not the
        // global range ($A$1:$A$5) — summing the scoped formula's cells, not the global ones.
        var result = _eval.Evaluate("=SUM(OFFSET(Foo,0,0))", sheet, wb);

        result.Should().Be(new NumberValue(15), "OFFSET's named-range base must resolve the sheet-scoped formula, not the shadowed global range");
    }

    // ── P76: IF with an array condition must broadcast vector (1xN / Nx1) branches instead of
    //         requiring every branch to already match the condition's shape. ──────────────────

    [Fact]
    public void If_ArrayCondition_BroadcastsVectorTrueBranchIntoSpilledMatrix()
    {
        // =IF(A1:A3>0, B1:D1, 0) — Excel broadcasts the 3x1 condition against the 1x3 true branch
        // into a 3x3 result. FreeX previously fixed the result shape to the condition's (3x1) shape
        // and rejected the mismatched 1x3 branch as #VALUE! in every cell.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),  // A1 > 0 → true
            (2, 1, new NumberValue(-1)), // A2 > 0 → false
            (3, 1, new NumberValue(2)),  // A3 > 0 → true
            (1, 2, new NumberValue(10)), // B1
            (1, 3, new NumberValue(20)), // C1
            (1, 4, new NumberValue(30))); // D1

        var result = _eval.Evaluate("=IF(A1:A3>0,B1:D1,0)", sheet, wb);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(3);
        range.ColCount.Should().Be(3);
        // Row 1 (true): broadcasts the true branch's row across all 3 columns.
        range.Cells[0, 0].Should().Be(new NumberValue(10));
        range.Cells[0, 1].Should().Be(new NumberValue(20));
        range.Cells[0, 2].Should().Be(new NumberValue(30));
        // Row 2 (false): broadcasts the scalar false branch (0) across all 3 columns.
        range.Cells[1, 0].Should().Be(new NumberValue(0));
        range.Cells[1, 1].Should().Be(new NumberValue(0));
        range.Cells[1, 2].Should().Be(new NumberValue(0));
        // Row 3 (true): broadcasts the true branch's row again.
        range.Cells[2, 0].Should().Be(new NumberValue(10));
        range.Cells[2, 1].Should().Be(new NumberValue(20));
        range.Cells[2, 2].Should().Be(new NumberValue(30));
    }
}
