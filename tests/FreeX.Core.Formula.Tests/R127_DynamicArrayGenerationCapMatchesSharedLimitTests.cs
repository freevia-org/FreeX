using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R127: dynamic-array generation/reshaping functions (SEQUENCE, RANDARRAY, MAKEARRAY, VSTACK,
/// HSTACK, TOROW, TOCOL, WRAPROWS, WRAPCOLS, CHOOSEROWS, CHOOSECOLS, EXPAND) each independently
/// hardcoded a raw `&gt; 1_000_000` cell-count guard, disconnected from
/// FormulaSafetyLimits.MaxMaterializedRangeCells -- the exact shared constant r126 raised from
/// 1,000,000 to 16,777,216 (16 full worksheet columns' worth of rows) because the old value was
/// "deliberately just UNDER a single full worksheet column's real height". r126 only touched the
/// shared constant's own call sites (BuildRangeValue/OFFSET/INDIRECT/aggregates); these sibling
/// functions kept their own independent, stale 1,000,000 literal, so a legitimate result well
/// under the real 16,777,216-cell cap (and far under Excel's true 1,048,576 x 16,384 sheet limit)
/// was still wrongly rejected with #VALUE!/#NUM! by every one of them.
///
/// Every case below uses a result strictly between 1,000,000 and 16,777,216 cells: it must have
/// failed under the old per-function 1,000,000 literal and must succeed now that every site
/// shares FormulaSafetyLimits.MaxMaterializedRangeCells. A second case per function proves the
/// cap is not simply removed: a result far beyond 16,777,216 cells must still return an error.
/// </summary>
public sealed class R127_DynamicArrayGenerationCapMatchesSharedLimitTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet() => new(SheetId.New(), "S");

    // ── SEQUENCE ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sequence_1_2MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        var result = _eval.Evaluate("=SEQUENCE(1200,1000)", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1200);
        rv.ColCount.Should().Be(1000);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sequence_FarBeyondSharedCap_StillReturnsValueError()
    {
        _eval.Evaluate("=SEQUENCE(20000000,1)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    // ── RANDARRAY ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Randarray_1_1MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        var result = _eval.Evaluate("=RANDARRAY(1100,1000)", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1100);
        rv.ColCount.Should().Be(1000);
    }

    [Fact]
    public void Randarray_FarBeyondSharedCap_StillReturnsValueError()
    {
        _eval.Evaluate("=RANDARRAY(20000000,1)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    // ── MAKEARRAY ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Makearray_1_0MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        // Parameter names "rr"/"cc" (not "r"/"c", which the parser treats as R1C1-style
        // reference tokens elsewhere) to isolate this test to the materialized-cell-count cap.
        var result = _eval.Evaluate("=MAKEARRAY(1001,1000,LAMBDA(rr,cc,rr+cc))", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1001);
        rv.ColCount.Should().Be(1000);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Makearray_FarBeyondSharedCap_StillReturnsValueError()
    {
        _eval.Evaluate("=MAKEARRAY(20000000,1,LAMBDA(rr,cc,rr))", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    // ── VSTACK / HSTACK ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Vstack_1_2MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        var result = _eval.Evaluate("=VSTACK(SEQUENCE(600000,1),SEQUENCE(600000,1))", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1200000);
        rv.ColCount.Should().Be(1);
    }

    [Fact]
    public void Vstack_FarBeyondSharedCap_StillReturnsValueError()
    {
        _eval.Evaluate("=VSTACK(SEQUENCE(10000000,1),SEQUENCE(10000000,1))", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Hstack_1_2MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        var result = _eval.Evaluate("=HSTACK(SEQUENCE(1,600000),SEQUENCE(1,600000))", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(1200000);
    }

    [Fact]
    public void Hstack_FarBeyondSharedCap_StillReturnsValueError()
    {
        _eval.Evaluate("=HSTACK(SEQUENCE(1,10000000),SEQUENCE(1,10000000))", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    // ── TOROW / TOCOL ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Torow_1_05MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        var result = _eval.Evaluate("=TOROW(SEQUENCE(1050,1000))", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(1050000);
    }

    [Fact]
    public void Tocol_1_05MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        var result = _eval.Evaluate("=TOCOL(SEQUENCE(1050,1000))", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1050000);
        rv.ColCount.Should().Be(1);
    }

    // ── WRAPROWS / WRAPCOLS ──────────────────────────────────────────────────────────────

    [Fact]
    public void Wraprows_1_05MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        var result = _eval.Evaluate("=WRAPROWS(TOROW(SEQUENCE(1,1050000)),1000)", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1050);
        rv.ColCount.Should().Be(1000);
    }

    [Fact]
    public void Wrapcols_1_05MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        var result = _eval.Evaluate("=WRAPCOLS(TOROW(SEQUENCE(1,1050000)),1000)", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1000);
        rv.ColCount.Should().Be(1050);
    }

    // ── CHOOSEROWS / CHOOSECOLS ──────────────────────────────────────────────────────────

    [Fact]
    public void Chooserows_1_05MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        // arr = 1 row x 1050 cols; index list repeats row 1, 1000 times => 1000 x 1050 = 1,050,000 cells.
        var result = _eval.Evaluate("=CHOOSEROWS(SEQUENCE(1,1050),SEQUENCE(1000,1,1,0))", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1000);
        rv.ColCount.Should().Be(1050);
    }

    [Fact]
    public void Choosecols_1_05MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        // arr = 1050 rows x 1 col; index list repeats col 1, 1000 times => 1050 x 1000 = 1,050,000 cells.
        var result = _eval.Evaluate("=CHOOSECOLS(SEQUENCE(1050,1),SEQUENCE(1,1000,1,0))", MakeSheet());
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1050);
        rv.ColCount.Should().Be(1000);
    }

    // ── EXPAND (the "one more stacking site" in the same file family) ──────────────────────

    [Fact]
    public void Expand_1_05MillionCells_BetweenOldAndNewCap_Succeeds()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=EXPAND(SEQUENCE(1,1),1050000,1)", sheet);
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1050000);
        rv.ColCount.Should().Be(1);
    }

    [Fact]
    public void Expand_FarBeyondSharedCap_StillReturnsValueError()
    {
        _eval.Evaluate("=EXPAND(SEQUENCE(1,1),20000000,1)", MakeSheet()).Should().Be(ErrorValue.Value);
    }
}
