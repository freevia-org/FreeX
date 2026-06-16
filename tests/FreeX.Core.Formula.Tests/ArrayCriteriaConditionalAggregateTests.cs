using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for array-criteria *IF(S) functions.
///
/// Excel rule: when a *IF(S) function receives a range/array where a scalar
/// criteria is expected, it evaluates element-by-element and returns an array
/// of results with the same shape.  SUMPRODUCT (and array-formula contexts)
/// then consume that array.
///
/// Root-cause: Budget!G4 = IFERROR(SUMPRODUCT(G7:G26,
///   SUMIFS(freqs[Multiplication factor], freqs[Frequency], H7:H26))/12, 0)
/// FreeX was treating the RangeValue criteria as AlwaysFalse → SUMIFS returned
/// 0 → SUMPRODUCT returned 0.
/// </summary>
public class ArrayCriteriaConditionalAggregateTests
{
    private readonly FormulaEvaluator _eval = new();

    // Helper: build a sheet from (row, col, value) triples.
    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // ── Phase-1 bisection ─────────────────────────────────────────────────────

    // (b) SUMIFS with scalar criteria — must still work as before.
    [Fact]
    public void Sumifs_ScalarCriteria_WorksAsBeforeAfterFix()
    {
        // sumRange = A1:A3 = {10,20,30}
        // criteriaRange = B1:B3 = {"X","Y","X"}
        // criteria = "X"  →  10+30 = 40
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("X")),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue("Y")),
            (3, 1, new NumberValue(30)), (3, 2, new TextValue("X")));
        _eval.Evaluate("=SUMIFS(A1:A3,B1:B3,\"X\")", sheet).Should().Be(new NumberValue(40));
    }

    // (c) SUMIFS with a RANGE as the criteria → must return a 3-element array.
    [Fact]
    public void Sumifs_RangeCriteria_ReturnsArrayOfResults()
    {
        // sumRange = A1:A4 = {10, 20, 30, 40}
        // criteriaRange = B1:B4 = {"X","Y","X","Y"}
        // criteriaArg (C1:C3) = {"X","Y","X"}
        // Expected: SUMIFS for "X" = 40, for "Y" = 60, for "X" = 40
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("X")), (1, 3, new TextValue("X")),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue("Y")), (2, 3, new TextValue("Y")),
            (3, 1, new NumberValue(30)), (3, 2, new TextValue("X")), (3, 3, new TextValue("X")),
            (4, 1, new NumberValue(40)), (4, 2, new TextValue("Y")));

        var result = _eval.Evaluate("=SUMIFS(A1:A4,B1:B4,C1:C3)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(40)); // "X" → 10+30
        rv.Cells[1, 0].Should().Be(new NumberValue(60)); // "Y" → 20+40
        rv.Cells[2, 0].Should().Be(new NumberValue(40)); // "X" → 10+30
    }

    // (d) SUMPRODUCT consuming SUMIFS with range criteria — the original failing pattern.
    [Fact]
    public void Sumproduct_Sumifs_RangeCriteria_MatchesBudgetPattern()
    {
        // Reproduction of Budget!G4 pattern (simplified to 3 rows):
        //   G7:G9  = values to multiply   = {2, 3, 5}
        //   H7:H9  = frequency labels     = {"Daily", "Weekly", "Monthly"}
        //   freqs[Frequency]              = {"Daily","Weekly","Monthly","Quarterly"}   (C col)
        //   freqs[Multiplication factor]  = {365,    52,      12,       4}            (D col)
        //
        //   SUMIFS(D1:D4, C1:C4, H7:H9)  →  {365, 52, 12}
        //   SUMPRODUCT(G7:G9, {365,52,12}) = 2*365 + 3*52 + 5*12 = 730+156+60 = 946
        var sheet = MakeSheet(
            // freqs table (rows 1-4)
            (1, 3, new TextValue("Daily")),     (1, 4, new NumberValue(365)),
            (2, 3, new TextValue("Weekly")),    (2, 4, new NumberValue(52)),
            (3, 3, new TextValue("Monthly")),   (3, 4, new NumberValue(12)),
            (4, 3, new TextValue("Quarterly")), (4, 4, new NumberValue(4)),
            // G7:G9 = multipliers
            (7, 7, new NumberValue(2)),
            (8, 7, new NumberValue(3)),
            (9, 7, new NumberValue(5)),
            // H7:H9 = frequency labels (criteria)
            (7, 8, new TextValue("Daily")),
            (8, 8, new TextValue("Weekly")),
            (9, 8, new TextValue("Monthly")));

        var result = _eval.Evaluate("=SUMPRODUCT(G7:G9,SUMIFS(D1:D4,C1:C4,H7:H9))", sheet);
        result.Should().Be(new NumberValue(946));
    }

    // IFERROR wrapper as in the actual formula
    [Fact]
    public void Iferror_Sumproduct_Sumifs_RangeCriteria_ReturnsCorrectResult()
    {
        var sheet = MakeSheet(
            (1, 3, new TextValue("Daily")),     (1, 4, new NumberValue(365)),
            (2, 3, new TextValue("Weekly")),    (2, 4, new NumberValue(52)),
            (3, 3, new TextValue("Monthly")),   (3, 4, new NumberValue(12)),
            (4, 3, new TextValue("Quarterly")), (4, 4, new NumberValue(4)),
            (7, 7, new NumberValue(2)),
            (8, 7, new NumberValue(3)),
            (9, 7, new NumberValue(5)),
            (7, 8, new TextValue("Daily")),
            (8, 8, new TextValue("Weekly")),
            (9, 8, new TextValue("Monthly")));

        var result = _eval.Evaluate("=IFERROR(SUMPRODUCT(G7:G9,SUMIFS(D1:D4,C1:C4,H7:H9))/1,0)", sheet);
        result.Should().Be(new NumberValue(946));
    }

    // ── COUNTIF with array criteria ───────────────────────────────────────────

    [Fact]
    public void Countif_RangeCriteria_ReturnsArrayOfResults()
    {
        // range = A1:A4 = {"X","Y","X","X"}
        // criteria (C1:C2) = {"X","Y"}
        // Expected: COUNTIF for "X" = 3, for "Y" = 1
        var sheet = MakeSheet(
            (1, 1, new TextValue("X")), (1, 3, new TextValue("X")),
            (2, 1, new TextValue("Y")), (2, 3, new TextValue("Y")),
            (3, 1, new TextValue("X")),
            (4, 1, new TextValue("X")));

        var result = _eval.Evaluate("=COUNTIF(A1:A4,C1:C2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(3)); // "X"
        rv.Cells[1, 0].Should().Be(new NumberValue(1)); // "Y"
    }

    // ── COUNTIFS with array criteria ──────────────────────────────────────────

    [Fact]
    public void Countifs_RangeCriteria_ReturnsArrayOfResults()
    {
        // rangeB = B1:B4 = {"X","Y","X","Y"}
        // criteria (C1:C2) = {"X","Y"}
        // Expected: COUNTIFS for "X" = 2, for "Y" = 2
        var sheet = MakeSheet(
            (1, 2, new TextValue("X")), (1, 3, new TextValue("X")),
            (2, 2, new TextValue("Y")), (2, 3, new TextValue("Y")),
            (3, 2, new TextValue("X")),
            (4, 2, new TextValue("Y")));

        var result = _eval.Evaluate("=COUNTIFS(B1:B4,C1:C2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(2)); // "X"
        rv.Cells[1, 0].Should().Be(new NumberValue(2)); // "Y"
    }

    // ── AVERAGEIF with array criteria ─────────────────────────────────────────

    [Fact]
    public void Averageif_RangeCriteria_ReturnsArrayOfResults()
    {
        // range = A1:A3 = {"X","Y","X"}; values = B1:B3 = {10,20,30}
        // criteria (C1:C2) = {"X","Y"}
        // Expected: avg for "X" = (10+30)/2=20, for "Y" = 20
        var sheet = MakeSheet(
            (1, 1, new TextValue("X")), (1, 2, new NumberValue(10)), (1, 3, new TextValue("X")),
            (2, 1, new TextValue("Y")), (2, 2, new NumberValue(20)), (2, 3, new TextValue("Y")),
            (3, 1, new TextValue("X")), (3, 2, new NumberValue(30)));

        var result = _eval.Evaluate("=AVERAGEIF(A1:A3,C1:C2,B1:B3)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(20)); // avg("X") = (10+30)/2
        rv.Cells[1, 0].Should().Be(new NumberValue(20)); // avg("Y") = 20
    }

    // ── AVERAGEIFS with array criteria ────────────────────────────────────────

    [Fact]
    public void Averageifs_RangeCriteria_ReturnsArrayOfResults()
    {
        // avgRange = A1:A3 = {10,20,30}
        // criteriaRange = B1:B3 = {"X","Y","X"}
        // criteriaArg (C1:C2) = {"X","Y"}
        // Expected: avg for "X" = (10+30)/2=20, for "Y" = 20
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("X")), (1, 3, new TextValue("X")),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue("Y")), (2, 3, new TextValue("Y")),
            (3, 1, new NumberValue(30)), (3, 2, new TextValue("X")));

        var result = _eval.Evaluate("=AVERAGEIFS(A1:A3,B1:B3,C1:C2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(20)); // avg("X")
        rv.Cells[1, 0].Should().Be(new NumberValue(20)); // avg("Y")
    }

    // ── 2-D criteria array ────────────────────────────────────────────────────

    [Fact]
    public void Sumifs_2D_RangeCriteria_ReturnsMatchingShape()
    {
        // sumRange = A1:A3 = {10,20,30}
        // criteriaRange = B1:B3 = {"X","Y","X"}
        // criteriaArg (C1:D1) = 1×2: {"X","Y"}  (horizontal)
        // Expected: 1×2 result: {40, 20}
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("X")),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue("Y")),
            (3, 1, new NumberValue(30)), (3, 2, new TextValue("X")),
            // C1:D1 = {"X","Y"} (horizontal 2-col array)
            (1, 3, new TextValue("X")), (1, 4, new TextValue("Y")));

        var result = _eval.Evaluate("=SUMIFS(A1:A3,B1:B3,C1:D1)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(40)); // "X" → 10+30
        rv.Cells[0, 1].Should().Be(new NumberValue(20)); // "Y" → 20
    }

    // ── SUMPRODUCT with numeric range criteria ────────────────────────────────

    [Fact]
    public void Sumproduct_Sumifs_NumericRangeCriteria_ReturnsCorrectResult()
    {
        // sumRange = A1:A4 = {100,200,300,400}
        // criteriaRange = B1:B4 = {1,2,1,2}
        // criteriaArg (C1:C2) = {1,2}
        // SUMIFS for 1 = 400, for 2 = 600
        // G1:G2 = {1,1}
        // SUMPRODUCT(G1:G2, SUMIFS(A1:A4, B1:B4, C1:C2)) = 1*400 + 1*600 = 1000
        var sheet = MakeSheet(
            (1, 1, new NumberValue(100)), (1, 2, new NumberValue(1)),
            (2, 1, new NumberValue(200)), (2, 2, new NumberValue(2)),
            (3, 1, new NumberValue(300)), (3, 2, new NumberValue(1)),
            (4, 1, new NumberValue(400)), (4, 2, new NumberValue(2)),
            (1, 3, new NumberValue(1)), // C1
            (2, 3, new NumberValue(2)), // C2
            (1, 7, new NumberValue(1)), // G1
            (2, 7, new NumberValue(1))); // G2

        var result = _eval.Evaluate("=SUMPRODUCT(G1:G2,SUMIFS(A1:A4,B1:B4,C1:C2))", sheet);
        result.Should().Be(new NumberValue(1000));
    }

    // Two array criteria of different orientations must BROADCAST into a 2-D result, not nest.
    // Mirrors Spill Formulae!D197 = SUMIFS(E,C,ANCHORARRAY(C197),D,ANCHORARRAY(D196)) where C197
    // spills a column vector and D196 spills a row vector.
    [Fact]
    public void Sumifs_TwoArrayCriteria_DifferentOrientations_BroadcastsToMatrix()
    {
        // A = amount, B = key1, D = key2 over 4 rows.
        // rows: (10,x,p),(20,y,q),(30,x,p),(40,y,q)
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("x")), (1, 4, new TextValue("p")),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue("y")), (2, 4, new TextValue("q")),
            (3, 1, new NumberValue(30)), (3, 2, new TextValue("x")), (3, 4, new TextValue("p")),
            (4, 1, new NumberValue(40)), (4, 2, new TextValue("y")), (4, 4, new TextValue("q")),
            // crit1 vertical (2x1) at F1:F2 = {x; y}
            (1, 6, new TextValue("x")), (2, 6, new TextValue("y")),
            // crit2 horizontal (1x2) at H1:I1 = {p, q}
            (1, 8, new TextValue("p")), (1, 9, new TextValue("q")));

        var result = _eval.Evaluate("=SUMIFS(A1:A4,B1:B4,F1:F2,D1:D4,H1:I1)", sheet)
            .Should().BeOfType<RangeValue>("two array criteria broadcast into a matrix, not a nested range").Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new NumberValue(40), "x AND p → rows 1,3 → 10+30");
        result.Cells[0, 1].Should().Be(new NumberValue(0), "x AND q → none");
        result.Cells[1, 0].Should().Be(new NumberValue(0), "y AND p → none");
        result.Cells[1, 1].Should().Be(new NumberValue(60), "y AND q → rows 2,4 → 20+40");
    }
}
