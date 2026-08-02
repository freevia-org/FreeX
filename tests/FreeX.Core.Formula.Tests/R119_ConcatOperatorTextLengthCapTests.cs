using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R119-formula-concat-operator-text-length-cap: real Excel caps any cell's text content
// (including formula results) at 32,767 characters. CONCAT/CONCATENATE/TEXTJOIN/REPT already
// enforce this via TextResult()/ExceedsExcelTextLimit(), but the `&` binary operator
// (ConcatScalarOp in FormulaEvaluator.Operators.cs) did not -- it produced an oversized
// TextValue that then threw an unhandled ArgumentOutOfRangeException from ClosedXML when the
// workbook was saved to .xlsx, instead of evaluating to #VALUE! like every sibling text
// function. See FormulaEvaluator.Operators.cs:ConcatScalarOp.
public class R119_ConcatOperatorTextLengthCapTests
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
    public void Concat_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue(new string('x', 20000))),
            (2, 1, new TextValue(new string('y', 20000))));

        // 20000 + 20000 = 40000 > 32767 -> #VALUE!, matching REPT/CONCATENATE/TEXTJOIN.
        _eval.Evaluate("=A1&A2", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Concat_ResultExactlyAtExcelCellLimit_StillReturnsFullText()
    {
        var left = new string('x', 16000);
        var right = new string('y', 16767);
        var sheet = MakeSheet(
            (1, 1, new TextValue(left)),
            (2, 1, new TextValue(right)));

        // 16000 + 16767 = 32767 -- exactly at the limit, must still succeed (no off-by-one).
        _eval.Evaluate("=A1&A2", sheet).Should().Be(new TextValue(left + right));
    }

    [Fact]
    public void Concat_ResultOneCharacterOverExcelCellLimit_ReturnsValueError()
    {
        var left = new string('x', 16000);
        var right = new string('y', 16768);
        var sheet = MakeSheet(
            (1, 1, new TextValue(left)),
            (2, 1, new TextValue(right)));

        // 16000 + 16768 = 32768 -- one character over the limit -> #VALUE!.
        _eval.Evaluate("=A1&A2", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Concat_RepeatedChainOverLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('z', 20000))));

        // =A1&A1 chains the same oversized cell against itself, exactly the DESCRIPTION's example.
        _eval.Evaluate("=A1&A1", sheet).Should().Be(ErrorValue.Value);
    }

    // ── No-regression sibling coverage ──────────────────────────────────────

    [Fact]
    public void Concat_NormalShortStrings_StillJoinsNormally()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _eval.Evaluate("=\"foo\"&\"bar\"", sheet).Should().Be(new TextValue("foobar"));
    }

    [Fact]
    public void Concat_ArrayOperand_ErrorInsideRangeStillPropagatesAsThatError()
    {
        // Sibling behavior that must not regress: an error value inside a range operand still
        // propagates as that error (not stringified to its error code text), even after adding
        // the length-cap check in ConcatScalarOp.
        var sheet = MakeSheet(
            (1, 1, ErrorValue.NA),
            (2, 1, new TextValue("b")),
            (1, 2, new TextValue("!")),
            (2, 2, new TextValue("!")));

        var result = _eval.Evaluate("=A1:A2&B1:B2", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.At(1, 1).Should().Be(ErrorValue.NA);
        range.At(2, 1).Should().Be(new TextValue("b!"));
    }

    [Fact]
    public void Concat_ArrayOperand_OneOversizedElementReturnsValueErrorForThatCell()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue(new string('x', 20000))),
            (2, 1, new TextValue("short")),
            (1, 2, new TextValue(new string('y', 20000))),
            (2, 2, new TextValue("!")));

        var result = _eval.Evaluate("=A1:A2&B1:B2", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.At(1, 1).Should().Be(ErrorValue.Value);
        range.At(2, 1).Should().Be(new TextValue("short!"));
    }
}
