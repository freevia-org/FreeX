using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    // R45-formula-lookup-choose-switch-3-1: IFERROR/IFNA's array fallback must broadcast a
    // 1x1 (or 1xN/Nx1) fallback range across every error position, the same broadcasting
    // convention IF/CHOOSE already apply, instead of requiring an exact shape match.
    [Fact]
    public void Iferror_OneByOneFallbackRange_BroadcastsAcrossArrayErrors()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(3)),
            (1, 2, new NumberValue(99)));

        // =IFERROR(A1:A3, B1:B1) — B1:B1 is a genuine (1x1) range reference, not a scalar.
        var result = _eval.Evaluate("=IFERROR(A1:A3,B1:B1)", sheet)
            .Should().BeOfType<RangeValue>("a range primary argument must produce an array result").Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(1), "non-error cells pass through untouched");
        result.Cells[1, 0].Should().Be(new NumberValue(99), "the error cell is replaced by the broadcast 1x1 fallback");
        result.Cells[2, 0].Should().Be(new NumberValue(3), "non-error cells pass through untouched");
    }

    // Sibling no-regression: IFNA shares ReplaceRangeErrors with IFERROR and must broadcast
    // identically, but only for #N/A (not #DIV/0!).
    [Fact]
    public void Ifna_OneByOneFallbackRange_BroadcastsAcrossNaErrorsOnly()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.NA),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(3)),
            (1, 2, new NumberValue(7)));

        var result = _eval.Evaluate("=IFNA(A1:A3,B1:B1)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new NumberValue(7), "the #N/A cell is replaced by the broadcast 1x1 fallback");
        result.Cells[1, 0].Should().Be(ErrorValue.DivByZero, "IFNA must not catch #DIV/0!");
        result.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    // No-regression: an exact-shape fallback range must still work exactly as before (per-cell
    // fallback, not broadcast).
    [Fact]
    public void Iferror_ExactShapeFallbackRange_ReplacesOnlyMatchingErrorCells()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(3)),
            (1, 2, new NumberValue(10)),
            (2, 2, new NumberValue(20)),
            (3, 2, new NumberValue(30)));

        var result = _eval.Evaluate("=IFERROR(A1:A3,B1:B3)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new NumberValue(20), "same-shape fallback still replaces the error with its own corresponding cell");
        result.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    // No-regression: a fallback range shape that is neither an exact match nor broadcastable on
    // either axis must still produce #VALUE!, matching Excel's incompatible-array-shape behavior.
    [Fact]
    public void Iferror_IncompatibleFallbackRangeShape_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(3)),
            (1, 2, new NumberValue(1)), (1, 3, new NumberValue(2)),
            (2, 2, new NumberValue(3)), (2, 3, new NumberValue(4)));

        // Fallback is 2x2 (B1:C2) against a 3x1 primary range: neither axis is 1 nor equal, so
        // this cannot broadcast on either dimension.
        _eval.Evaluate("=IFERROR(A1:A3,B1:C2)", sheet).Should().Be(ErrorValue.Value);
    }

    // R45-formula-lookup-choose-switch-3-2: IFS's condition/result ranges must broadcast per-axis
    // (a 1-row or 1-column range legitimately broadcasts against a target with a matching other
    // axis), matching IF/CHOOSE's existing broadcasting, instead of only accepting an exact shape
    // match or a fully-1x1 range.
    [Fact]
    public void Ifs_OneRowResultRange_BroadcastsAcrossAllTargetRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)),
            (3, 1, new NumberValue(5)), (3, 2, new NumberValue(6)),
            (1, 4, new NumberValue(10)), (1, 5, new NumberValue(20)));

        // A1:B3 > 0 is a 3x2 all-true condition array; D1:E1 is a 1x2 result row that must
        // broadcast down across all 3 output rows (column count already matches).
        var result = _eval.Evaluate("=IFS(A1:B3>0,D1:E1,TRUE,0)", sheet)
            .Should().BeOfType<RangeValue>("a range condition must produce a spilled array result").Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(2);
        for (int r = 0; r < 3; r++)
        {
            result.Cells[r, 0].Should().Be(new NumberValue(10), $"row {r} broadcasts the single result row");
            result.Cells[r, 1].Should().Be(new NumberValue(20), $"row {r} broadcasts the single result row");
        }
    }

    // Sibling no-regression: SWITCH shares PickRangeElementForArrayResult with IFS and must
    // broadcast a 1-row value/result range identically.
    [Fact]
    public void Switch_OneRowResultRange_BroadcastsAcrossAllTargetRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(1)),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(1)),
            (3, 1, new NumberValue(1)), (3, 2, new NumberValue(1)),
            (1, 4, new NumberValue(10)), (1, 5, new NumberValue(20)));

        // A1:B3 is a 3x2 array all equal to 1; D1:E1 is the 1x2 result row for the "=1" case that
        // must broadcast down across all 3 output rows.
        var result = _eval.Evaluate("=SWITCH(A1:B3,1,D1:E1,0)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(2);
        for (int r = 0; r < 3; r++)
        {
            result.Cells[r, 0].Should().Be(new NumberValue(10));
            result.Cells[r, 1].Should().Be(new NumberValue(20));
        }
    }

    // No-regression: exact-shape IFS result ranges must still be picked per-cell as before.
    [Fact]
    public void Ifs_ExactShapeResultRange_PicksPerCellAsBefore()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)),
            (1, 4, new NumberValue(10)), (1, 5, new NumberValue(20)),
            (2, 4, new NumberValue(30)), (2, 5, new NumberValue(40)));

        var result = _eval.Evaluate("=IFS(A1:B2>0,D1:E2)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new NumberValue(10));
        result.Cells[0, 1].Should().Be(new NumberValue(20));
        result.Cells[1, 0].Should().Be(new NumberValue(30));
        result.Cells[1, 1].Should().Be(new NumberValue(40));
    }

    // No-regression: a result range shape that is neither an exact match nor broadcastable on
    // either axis must still yield #VALUE!, matching Excel's incompatible-array-shape behavior.
    [Fact]
    public void Ifs_IncompatibleResultRangeShape_ReturnsValueErrorPerCell()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)),
            (3, 1, new NumberValue(5)), (3, 2, new NumberValue(6)),
            (1, 4, new NumberValue(10)), (1, 5, new NumberValue(20)),
            (2, 4, new NumberValue(30)), (2, 5, new NumberValue(40)));

        // A1:B3 > 0 is 3x2; D1:E2 is 2x2 — neither axis is 1 nor equal to the 3x2 target, so this
        // cannot broadcast on either dimension.
        var result = _eval.Evaluate("=IFS(A1:B3>0,D1:E2,TRUE,0)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 2; c++)
                result.Cells[r, c].Should().Be(ErrorValue.Value);
    }
}
