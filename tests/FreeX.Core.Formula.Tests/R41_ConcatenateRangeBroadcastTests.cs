using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R41-formula-text-join-concat-3-1: CONCATENATE(A1:A3,B1:B3) must broadcast element-wise
// (like real Excel's dynamic-array CONCATENATE) instead of returning #VALUE! for a second
// range argument. A scalar still broadcasts across a range, and mismatched non-1x1 shapes
// still yield #VALUE!.
public class R41_ConcatenateRangeBroadcastTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    private static void AssertTextColumn(ScalarValue value, params string[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.At(row + 1, 1).Should().Be(new TextValue(expected[row]));
    }

    [Fact]
    public void Concatenate_TwoEquallyShapedRanges_SpillsElementwiseConcatenation()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new TextValue("1")),
            (2, 1, new TextValue("b")), (2, 2, new TextValue("2")),
            (3, 1, new TextValue("c")), (3, 2, new TextValue("3")));

        var result = _eval.Evaluate("=CONCATENATE(A1:A3,B1:B3)", sheet);

        AssertTextColumn(result, "a1", "b2", "c3");
    }

    [Fact]
    public void Concatenate_PlainScalarArguments_StillJoinsDirectly()
    {
        _eval.Evaluate("=CONCATENATE(\"x\",\"y\")", MakeSheet()).Should().Be(new TextValue("xy"));
    }

    [Fact]
    public void Concatenate_ScalarAndRange_StillBroadcastsScalarAcrossRange()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (2, 1, new TextValue("b")));

        var result = _eval.Evaluate("=CONCATENATE(A1:A2,\"!\")", sheet);

        AssertTextColumn(result, "a!", "b!");
    }

    [Fact]
    public void Concatenate_MismatchedRangeShapes_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new TextValue("1")),
            (2, 1, new TextValue("b")), (2, 2, new TextValue("2")),
            (3, 1, new TextValue("c")));

        var result = _eval.Evaluate("=CONCATENATE(A1:A3,B1:B2)", sheet);

        result.Should().Be(ErrorValue.Value);
    }
}
