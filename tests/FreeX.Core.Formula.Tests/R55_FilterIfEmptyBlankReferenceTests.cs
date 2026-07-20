using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R55-formula-dynamic-array-spill-5-1: FILTER's if_empty argument (args[2]) conflated a
/// genuinely-omitted argument slot (no third argument at all) with an explicitly-supplied argument
/// that evaluates to BlankValue (e.g. a reference to an empty cell), always returning #CALC! for
/// the latter. Only a genuinely-absent slot should keep the #CALC! default -- once the slot is
/// supplied, whatever it evaluates to (including a blank) must be used as FILTER's if_empty result
/// instead of being forced back to #CALC!.
/// </summary>
public sealed class R55_FilterIfEmptyBlankReferenceTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void Filter_IfEmptyExplicitBlankCellReference_PassesBlankThrough_NotCalcError()
    {
        // A1=10, B1=FALSE (no rows match), C1 is an explicit blank-cell reference used as
        // if_empty. The slot IS supplied (not omitted) -- it just evaluates to blank.
        var sheet = Sheet((1, 1, new NumberValue(10)), (1, 2, new BoolValue(false)));

        var rv = _eval.Evaluate("=FILTER(A1:A1,B1:B1,C1)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        rv.Cells[0, 0].Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void Filter_IfEmptyGenuinelyOmitted_StillReturnsCalcError_SiblingNoRegression()
    {
        // No third argument at all -- the if_empty slot is genuinely absent, so FILTER's
        // documented #CALC! default must still apply, unaffected by the blank-reference fix above.
        var sheet = Sheet((1, 1, new NumberValue(10)), (1, 2, new BoolValue(false)));

        _eval.Evaluate("=FILTER(A1:A1,B1:B1)", sheet).Should().Be(new ErrorValue("#CALC!"));
    }
}
