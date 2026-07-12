using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R25-aggregate-subtotal-deep-3: SUBTOTAL/AGGREGATE decide whether to apply hidden-row and
/// nested-aggregate exclusion by asking whether their RangeValue operand is a genuine worksheet
/// reference. The original guard inferred that from the coordinates
/// (<c>SheetName is not null || StartRow != 1 || StartCol != 1</c>), but a genuine same-sheet
/// reference anchored at A1 has StartRow=1/StartCol=1/SheetName=null — field-for-field identical to
/// a computed/virtual array's default coordinates (FILTER/SEQUENCE/… via <c>new RangeValue(cells)</c>).
/// No heuristic over those fields can separate the two, so the guard now reads the explicit
/// <see cref="RangeValue.IsSheetReference"/> provenance flag, set only where a reference is actually
/// materialized (BuildRangeValue / OFFSET / INDIRECT). These tests pin both directions: a genuine
/// A1-anchored reference must still exclude hidden rows, while a virtual array must not — the two
/// R19 tests (<see cref="R19_subtotal_coords_Tests"/>) cover the mirror case for computed arrays.
/// </summary>
public sealed class R25_SubtotalAggregateReferenceProvenanceTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void Subtotal_GenuineSameSheetA1AnchoredReference_ExcludesHiddenRow_ForStatisticalFunction()
    {
        // The exact ambiguous case: a genuine same-sheet reference anchored at A1
        // (StartRow=1/StartCol=1/SheetName=null) collides with a computed array's default
        // coordinates. Statistical func 107 (STDEV.S) bypasses the direct-range fast path, so the
        // reference reaches the coordinate-aware slow path where the old heuristic wrongly treated
        // it as a virtual array and *included* the hidden outlier.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(100)), // hidden outlier
            (3, 1, new NumberValue(4)),
            (4, 1, new NumberValue(4)));
        sheet.GroupHiddenRows.Add(2);

        var withHiddenExcluded = _eval.Evaluate("=SUBTOTAL(107,A1:A4)", sheet);
        var withHiddenIncluded = _eval.Evaluate("=SUBTOTAL(7,A1:A4)", sheet);

        // 107 skips the hidden 100, leaving {2,4,4}: STDEV.S = 1.1547...
        withHiddenExcluded.Should().BeOfType<NumberValue>()
            .Which.Value.Should().BeApproximately(1.1547005383792515, 1e-9);
        // 7 keeps the hidden 100, so the result must differ — proving the row was genuinely read as
        // hidden rather than the exclusion being disabled outright.
        withHiddenIncluded.Should().BeOfType<NumberValue>()
            .Which.Value.Should().NotBeApproximately(1.1547005383792515, 1e-6);
    }

    [Fact]
    public void Subtotal_GenuineReferenceViaIndirect_ExcludesHiddenRow()
    {
        // INDIRECT("A1:A4") materializes a genuine A1-anchored reference (StartRow=1/StartCol=1/
        // SheetName=null) and always bypasses the direct-range fast path, so its provenance flag —
        // not the coordinates — must drive hidden-row exclusion for the hidden-row-skipping func 109.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(100)), // hidden
            (3, 1, new NumberValue(30)),
            (4, 1, new NumberValue(40)));
        sheet.GroupHiddenRows.Add(2);

        // 109 = SUM skipping hidden rows: 10 + 30 + 40 = 80.
        _eval.Evaluate("=SUBTOTAL(109,INDIRECT(\"A1:A4\"))", sheet).Should().Be(new NumberValue(80));
        // 9 = SUM keeping every row: 10 + 100 + 30 + 40 = 180 (control: the reference is genuinely read).
        _eval.Evaluate("=SUBTOTAL(9,INDIRECT(\"A1:A4\"))", sheet).Should().Be(new NumberValue(180));
    }

    [Fact]
    public void Aggregate_GenuineReferenceViaOffset_ExcludesHiddenRow()
    {
        // AGGREGATE's direct-range fast path handles every function number, so a genuine reference
        // only reaches the coordinate-aware slow path when wrapped in a reference-returning call.
        // OFFSET(A1,0,0,4,1) anchors at (1,1)/null-SheetName — the same ambiguous coordinates a
        // computed array uses — so the old copied heuristic wrongly kept the hidden row.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(100)), // hidden
            (3, 1, new NumberValue(30)),
            (4, 1, new NumberValue(40)));
        sheet.GroupHiddenRows.Add(2);

        // func 9 = SUM, option 1 = ignore hidden rows: 10 + 30 + 40 = 80.
        _eval.Evaluate("=AGGREGATE(9,1,OFFSET(A1,0,0,4,1))", sheet).Should().Be(new NumberValue(80));
        // option 0 = keep hidden rows: 10 + 100 + 30 + 40 = 180 (control).
        _eval.Evaluate("=AGGREGATE(9,0,OFFSET(A1,0,0,4,1))", sheet).Should().Be(new NumberValue(180));
    }

    [Fact]
    public void Aggregate_ComputedArrayOperand_IncludesAllElements_DespiteHiddenSheetRow()
    {
        // Mirror of the R19 SUBTOTAL guard for AGGREGATE: SEQUENCE(4) is a virtual array anchored at
        // the default (1,1)/null coordinates, so even with option 1 (ignore hidden rows) and sheet
        // row 2 hidden, its elements — which have no sheet position — must all be counted.
        var sheet = MakeSheet();
        sheet.GroupHiddenRows.Add(2);

        // {1;2;3;4} summed in full regardless of the hidden sheet row => 10.
        _eval.Evaluate("=AGGREGATE(9,1,SEQUENCE(4))", sheet).Should().Be(new NumberValue(10));
    }
}
