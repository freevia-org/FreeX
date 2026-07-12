using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-29 finding R29-formula-array-eval-deep-3: the @ (implicit intersection) operator applied
/// to a spill-anchor reference (A1#) was misclassified as a "computed/dynamic-array result" by
/// ImplicitIntersectionOp's reference-node whitelist (FormulaEvaluator.Operators.cs), because that
/// whitelist never included the ANCHORARRAY FunctionCallNode shape the parser produces for A1#
/// (Parser.cs's WrapSpillAnchor). A1# is a genuine worksheet reference to a spill range, so @ must
/// positionally intersect it against the formula cell's own row/col, exactly like @A1:A5 -- not
/// collapse to the anchor's own top-left value the way @SEQUENCE(3) (a truly computed array with no
/// worksheet-anchored row/col) correctly does.
/// </summary>
public sealed class R29_ImplicitIntersectionSpillAnchorTests
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
    public void At_OnSpillAnchorReference_PositionallyIntersectsBySpillRow_NotAnchorValue()
    {
        // A1 spills A1:A5 = 1..5 (via =SEQUENCE(5)). Formula in row 3 (=@A1#) must positionally
        // intersect to A3 = 3, matching real Excel -- not always return the anchor's own value
        // (A1 = 1), which was the bug.
        var sheet = MakeSheetWithColumnSpill(1, 1, 1, 2, 3, 4, 5);

        var result = _evaluator.Evaluate("=@A1#", sheet, currentCell: new CellAddress(sheet.Id, 3, 3));

        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void At_OnSpillAnchorReference_OffAxis_ReturnsValueError()
    {
        // Formula cell's row falls entirely outside the spill extent -> #VALUE!, the same
        // reference-backed behavior as an ordinary out-of-range @A1:A5 (see
        // Backlog_ImplicitIntersectionTests.At_OnReferenceRange_OffAxis_ReturnsValueError).
        var sheet = MakeSheetWithColumnSpill(1, 1, 1, 2, 3, 4, 5);

        var result = _evaluator.Evaluate("=@A1#", sheet, currentCell: new CellAddress(sheet.Id, 20, 20));

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void At_OnComputedDynamicArray_StillReturnsTopLeftElement_SiblingCaseUnaffected()
    {
        // Sibling case this fix must NOT regress: @ on a genuinely computed array (a function call
        // result, not a worksheet reference) still returns the array's top-left element regardless
        // of the formula cell's own row/col -- exactly the pre-existing behavior for =@SEQUENCE(3).
        var sheet = new Sheet(SheetId.New(), "S");

        var result = _evaluator.Evaluate("=@SEQUENCE(5)", sheet, currentCell: new CellAddress(sheet.Id, 3, 3));

        result.Should().Be(new NumberValue(1));
    }
}
