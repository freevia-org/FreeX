using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-13 bucket S15 regression tests.
/// </summary>
public class FreeXR13S15Tests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    /// <summary>
    /// R13-formula-array-cse-1: string concatenation (&amp;) over an array must propagate a
    /// per-element error as that error, not stringify it into corrupted literal text.
    /// Excel: A1=1, A2=1/0 (#DIV/0!), A3=3, =A1:A3&amp;"x" spills {"1x"; #DIV/0!; "3x"}.
    /// </summary>
    [Fact]
    public void Concatenate_OverArrayWithErrorElement_PropagatesErrorInsteadOfStringifyingIt()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(3)));

        var result = _eval.Evaluate("=A1:A3&\"x\"", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new TextValue("1x"));
        rv.Cells[1, 0].Should().Be(ErrorValue.DivByZero, "the error cell must propagate as #DIV/0!, not become the literal text \"#DIV/0!x\"");
        rv.Cells[2, 0].Should().Be(new TextValue("3x"));
    }

    /// <summary>
    /// Same bug via an array literal containing an error: ={1,#N/A}&amp;"z" must keep #N/A as
    /// an error, not turn it into the text "#N/Az".
    /// </summary>
    [Fact]
    public void Concatenate_ArrayLiteralWithErrorElement_PropagatesErrorInsteadOfStringifyingIt()
    {
        var sheet = MakeSheet();

        var result = _eval.Evaluate("={1,#N/A}&\"z\"", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.Cells[0, 0].Should().Be(new TextValue("1z"));
        rv.Cells[0, 1].Should().Be(ErrorValue.NA, "the #N/A element must propagate as an error, not become the literal text \"#N/Az\"");
    }
}
