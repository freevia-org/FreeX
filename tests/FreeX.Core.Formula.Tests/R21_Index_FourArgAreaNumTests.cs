using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R21-lookup-reference-deep-1: INDEX must accept the documented 4-argument reference form
// INDEX(reference, row_num, column_num, area_num). For an ordinary single-area range,
// area_num=1 (or omitted) must work exactly like the 3-arg form; any other area_num is out of
// range (FreeX has no multi-area union ranges) and must return #REF!, matching Excel's
// documented "area_num out of range" behavior.
public class R21_Index_FourArgAreaNumTests
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
    public void Index_FourArgForm_WithAreaNumOne_ReturnsSameAsThreeArgForm()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)),
            (3, 1, new NumberValue(5)), (3, 2, new NumberValue(6)),
            (4, 1, new NumberValue(7)), (4, 2, new NumberValue(8)),
            (5, 1, new NumberValue(9)), (5, 2, new NumberValue(10)));

        _eval.Evaluate("=INDEX(CHOOSE(1,A1:B5),2,1,1)", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=INDEX(A1:B5,2,1)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Index_FourArgForm_WithAreaNumGreaterThanOne_ReturnsRefError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)));

        _eval.Evaluate("=INDEX(CHOOSE(1,A1:B2),1,1,2)", sheet).Should().Be(ErrorValue.Ref);
    }
}
