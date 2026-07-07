using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Cleanup batch MED5 — round-10 MED finding P77 (FreeX.Core.Formula).
/// </summary>
public sealed class FreeXCleanupMED5Tests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }

    // ── P77: elementwise operators on mismatched (non-1) array dimensions must expand to the ──
    // ── bounding shape with #N/A padding the uncovered cells, not collapse to one #VALUE!. ─────

    [Fact]
    public void Add_MismatchedRowCounts_ExpandsToBoundingShapeWithNAPadding()
    {
        // A1:A2 (2x1) + B1:B3 (3x1) — Excel yields a 3x1 spill {A1+B1; A2+B2; #N/A}.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)),
            (3, 2, new NumberValue(3)));

        var result = _eval.Evaluate("=A1:A2+B1:B3", sheet);

        var range = result.Should().BeOfType<RangeValue>(
            "P77: mismatched dimensions must spill a per-element result, not collapse to one #VALUE!").Subject;
        range.RowCount.Should().Be(3);
        range.ColCount.Should().Be(1);
        range.Cells[0, 0].Should().Be(new NumberValue(11));
        range.Cells[1, 0].Should().Be(new NumberValue(22));
        range.Cells[2, 0].Should().Be(ErrorValue.NA, "the uncovered 3rd row of the shorter (2x1) operand pads with #N/A");
    }

    [Fact]
    public void Add_MismatchedColumnCounts_ExpandsToBoundingShapeWithNAPadding()
    {
        // A1:B1 (1x2) + A2:C2 (1x3) on row axis via columns — 1x3 result, last column #N/A.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(100)),
            (2, 2, new NumberValue(200)),
            (2, 3, new NumberValue(300)));

        var result = _eval.Evaluate("=A1:B1+A2:C2", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(1);
        range.ColCount.Should().Be(3);
        range.Cells[0, 0].Should().Be(new NumberValue(101));
        range.Cells[0, 1].Should().Be(new NumberValue(202));
        range.Cells[0, 2].Should().Be(ErrorValue.NA, "the uncovered 3rd column of the shorter (1x2) operand pads with #N/A");
    }

    [Fact]
    public void Add_SingleRowBroadcastsAgainstMultiRow_StillBroadcastsNormally()
    {
        // Sanity check: a genuine 1-dimension broadcast (not a real mismatch) must still work
        // exactly as before — this must NOT get #N/A padding since dimension 1 always broadcasts.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)),
            (1, 2, new NumberValue(100)));

        var result = _eval.Evaluate("=A1:A3+B1", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(3);
        range.Cells[0, 0].Should().Be(new NumberValue(101));
        range.Cells[1, 0].Should().Be(new NumberValue(102));
        range.Cells[2, 0].Should().Be(new NumberValue(103));
    }
}
