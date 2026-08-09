using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R132: SWITCH's scalar-expr path (expr itself is NOT a range) evaluated each value_i comparand
/// through the legacy <c>EvaluateNode</c> -> <c>EvaluateRange</c> implicit-intersection path instead
/// of the array-aware <c>EvaluateArrayOperand</c> every sibling short-circuit function (IF's
/// branches, IFERROR/IFNA's fallback, CHOOSE's/IFS's branches, and SWITCH's OWN result_i argument
/// two lines below the bug) already uses. A multi-cell value_i range therefore either:
///  - silently collapsed to the TOP-LEFT cell of the range when there is no current-cell context
///    (e.g. a direct Evaluate(text, sheet) call) -- "refuses to spill", or
///  - returned #VALUE! whenever the current formula cell sits off the range's row/column -- "wrongly
///    errors" -- even though nothing about SWITCH itself is undefined for that shape.
/// The fix makes value_i array-aware and, when a value_i comparand IS a multi-cell range, spills the
/// whole SWITCH result across that range's shape (comparing the fixed scalar expr against each
/// element in turn) -- Excel's own implicit-array behavior for a range argument in a scalar-typed
/// function-argument position.
/// </summary>
public partial class FunctionLibraryTests
{
    [Fact]
    public void Switch_MultiCellValueRange_NoCurrentCell_SpillsInsteadOfCollapsingToTopLeft()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),               // A1: expr
            (1, 2, new NumberValue(1)),                // B1
            (2, 2, new NumberValue(2)),                // B2
            (3, 2, new NumberValue(3)),                // B3
            (1, 4, new TextValue("one")),              // D1
            (2, 4, new TextValue("two")),               // D2
            (3, 4, new TextValue("three")));            // D3

        // =SWITCH(A1, B1:B3, D1:D3, "none") -- expr(2) is scalar, value1 (B1:B3) is a 3-cell range.
        // Before the fix (no currentCell supplied -> EvaluateRange's "no current-cell context"
        // fallback reads only B1's top-left value = 1), 2 != 1 never matches, falls through to the
        // (scalar) default "none" -- the WHOLE formula collapses to a single TextValue("none"),
        // never spilling and never actually comparing against B2 or B3 at all.
        var result = _eval.Evaluate("=SWITCH(A1,B1:B3,D1:D3,\"none\")", sheet);

        var range = result.Should().BeOfType<RangeValue>(
            "a multi-cell value_i comparand must spill the whole SWITCH result across its shape, " +
            "not collapse to a single scalar").Subject;

        range.RowCount.Should().Be(3);
        range.ColCount.Should().Be(1);
        range.Cells[0, 0].Should().Be(new TextValue("none"), "row 1: B1=1 != expr(2) -> default");
        range.Cells[1, 0].Should().Be(new TextValue("two"), "row 2: B2=2 == expr(2) -> D2 (matching row of the result range)");
        range.Cells[2, 0].Should().Be(new TextValue("none"), "row 3: B3=3 != expr(2) -> default");
    }

    [Fact]
    public void Switch_MultiCellValueRange_CurrentCellOffAxis_DoesNotReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),               // A1: expr
            (1, 2, new NumberValue(1)),                // B1
            (2, 2, new NumberValue(2)),                // B2
            (3, 2, new NumberValue(3)));               // B3

        // A currentCell far outside B1:B3's row range (row 10) used to make the legacy
        // EvaluateRange implicit-intersection path return #VALUE! for a value_i argument that used
        // no intersection semantics at all -- SWITCH's value_i isn't an implicit-intersection
        // operand, it's a plain function argument, so nothing about being "off-axis" from some
        // unrelated formula cell should ever produce #VALUE! here.
        var result = _eval.Evaluate(
            "=SWITCH(A1,B1:B3,\"matched\",\"none\")",
            sheet,
            currentCell: new CellAddress(sheet.Id, 10, 5));

        var range = result.Should().BeOfType<RangeValue>(
            "an off-axis current cell must never turn a plain function-argument range into #VALUE! -- " +
            "SWITCH's value_i isn't an implicit-intersection operand").Subject;
        range.Cells[1, 0].Should().Be(new TextValue("matched"));
    }

    [Fact]
    public void Switch_MultiCellValueRange_LaterPairAfterNonMatchingScalarPairs_SpillsFromThatPairOnward()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),                // A1: expr
            (1, 2, new NumberValue(0)),                 // B1: scalar value1 (never matches)
            (1, 3, new NumberValue(5)),                 // C1
            (2, 3, new NumberValue(9)),                 // C2   (value2 range)
            (1, 5, new TextValue("hit")));              // E1: result2 (scalar, broadcasts)

        // value1 (B1, a scalar 0) never matches expr(5) -- confirmed by the outer scan before the
        // range at value2 (C1:C2) is even reached. The per-cell scan for the spill driven by C1:C2
        // must start AT that pair (not re-scan value1 for every output cell) yet still produce a
        // correct per-cell result: row1 C1=5 matches -> "hit"; row2 C2=9 doesn't match -> #N/A
        // (no default supplied).
        var result = _eval.Evaluate("=SWITCH(A1,B1,\"never\",C1:C2,E1)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new TextValue("hit"));
        result.Cells[1, 0].Should().Be(ErrorValue.NA);
    }

    // --- Sibling no-regression: plain scalar value_i arguments (the overwhelmingly common case)
    // must behave exactly as before -- proves the array-aware swap didn't change ordinary SWITCH. ---

    [Fact]
    public void Switch_AllScalarValueArguments_StillMatchesAndReturnsScalarResult()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),   // A1: expr
            (1, 2, new NumberValue(1)),   // B1: value1
            (1, 3, new NumberValue(2)),   // C1: value2
            (1, 4, new NumberValue(3)));  // D1: value3

        _eval.Evaluate("=SWITCH(A1,B1,\"one\",C1,\"two\",D1,\"three\",\"none\")", sheet)
            .Should().Be(new TextValue("two"));
    }

    [Fact]
    public void Switch_AllScalarValueArguments_NoMatch_ReturnsDefault()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(99)),
            (1, 2, new NumberValue(1)),
            (1, 3, new NumberValue(2)));

        _eval.Evaluate("=SWITCH(A1,B1,\"one\",C1,\"two\",\"none\")", sheet)
            .Should().Be(new TextValue("none"));
    }

    [Fact]
    public void Switch_AllScalarValueArguments_NoMatchNoDefault_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(99)),
            (1, 2, new NumberValue(1)));

        _eval.Evaluate("=SWITCH(A1,B1,\"one\")", sheet).Should().Be(ErrorValue.NA);
    }

    // Sibling no-regression: the pre-existing exprRange (expr itself is the array) path -- which
    // shares EvaluateSwitchElement with the new value-range path after this refactor -- must still
    // broadcast correctly.
    [Fact]
    public void Switch_ExprRangePath_StillBroadcastsOneRowResultRange()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(1)),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(1)),
            (3, 1, new NumberValue(1)), (3, 2, new NumberValue(1)),
            (1, 4, new NumberValue(10)), (1, 5, new NumberValue(20)));

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
}
