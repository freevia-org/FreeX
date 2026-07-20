using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R51-formula-spill-anchor-intersect-3-1 / -3-2: EvaluateAnchorArray (FormulaEvaluator.Functions.cs,
/// backing the A1# spill-anchor operator and ANCHORARRAY()) constructed its result RangeValue without
/// setting IsSheetReference = true, unlike every other reference-materializing call site
/// (BuildRangeValue, OFFSET, INDIRECT). Two symptoms followed from the same one-line gap:
///   1. SINGLE(A1#) always returned the spill anchor's own top-left value instead of positionally
///      intersecting against the formula cell's row/col (ImplicitIntersectionOp's scalar-only overload
///      branches on IsSheetReference).
///   2. SUBTOTAL/AGGREGATE's hidden-row and nested-aggregate exclusion silently did not apply when the
///      argument was a spill-range operator (the same IsSheetReference gate).
/// </summary>
public sealed class R51_SpillAnchorReferenceProvenanceTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static Sheet MakeSheetWithColumnSpill(uint anchorRow, uint anchorCol, params double[] spillValues)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var anchorAddr = new CellAddress(sheet.Id, anchorRow, anchorCol);
        sheet.SetCell(anchorAddr, new NumberValue(spillValues[0]));
        var cells = new ScalarValue[spillValues.Length, 1];
        for (var i = 0; i < spillValues.Length; i++)
            cells[i, 0] = new NumberValue(spillValues[i]);
        var rv = new RangeValue(cells, anchorRow, anchorCol);
        sheet.SetSpillRange(anchorAddr, rv);
        return sheet;
    }

    [Fact]
    public void Single_OnSpillAnchorReference_PositionallyIntersectsBySpillRow_NotAnchorValue()
    {
        // A1 spills A1:A5 = 1,2,3,4,5. SINGLE(A1#) evaluated with the formula cell at row 3 must
        // positionally intersect to A3 = 3 (like the @ operator does), not always return the anchor's
        // own value (A1 = 1) -- the bug prior to setting IsSheetReference = true.
        var sheet = MakeSheetWithColumnSpill(1, 1, 1, 2, 3, 4, 5);

        var result = _evaluator.Evaluate("=SINGLE(A1#)", sheet, currentCell: new CellAddress(sheet.Id, 3, 3));

        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Single_OnComputedDynamicArray_StillReturnsTopLeftElement_SiblingCaseUnaffected()
    {
        // Sibling no-regression case: SINGLE() on a genuinely computed array (not a worksheet
        // reference) must still return the array's top-left element regardless of the formula cell's
        // own row/col -- this fix must not make every RangeValue look like a reference.
        var sheet = new Sheet(SheetId.New(), "S");

        var result = _evaluator.Evaluate("=SINGLE(SEQUENCE(5))", sheet, currentCell: new CellAddress(sheet.Id, 3, 3));

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Subtotal_OverSpillAnchorReference_ExcludesHiddenRow()
    {
        // A1 spills A1:A5 = 1,2,3,4,5; row 3 (value 3) is hidden. SUBTOTAL(109, A1#) (109 = SUM,
        // ignore hidden rows) must exclude the hidden row: 1+2+4+5 = 12, not 15.
        var sheet = MakeSheetWithColumnSpill(1, 1, 1, 2, 3, 4, 5);
        sheet.GroupHiddenRows.Add(3);

        var result = _evaluator.Evaluate("=SUBTOTAL(109,A1#)", sheet);

        result.Should().Be(new NumberValue(12));
    }

    [Fact]
    public void Subtotal_OverComputedDynamicArray_IncludesAllElements_DespiteHiddenSheetRow_SiblingCaseUnaffected()
    {
        // Sibling no-regression case: a virtual/computed array (SEQUENCE, not a spill-anchor
        // reference) has no sheet position, so hidden-row exclusion must never apply to it even when
        // the corresponding sheet row happens to be hidden.
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.GroupHiddenRows.Add(3);

        var result = _evaluator.Evaluate("=SUBTOTAL(109,SEQUENCE(5))", sheet);

        result.Should().Be(new NumberValue(15));
    }
}
